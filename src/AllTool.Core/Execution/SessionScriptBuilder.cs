using System.Text;
using AllTool.Core.Manifest;

namespace AllTool.Core.Execution;

/// <summary>
/// 会话型程序的执行计划。
///
/// **先说清楚这里的"会话"是什么**：不是在后台上挂一个解释器进程，而是**脚本重放**——
/// 把"当前生效的选择类命令"和"这次要跑的命令"拼成一个脚本，一次性喂给程序
/// （diskpart 用 <c>/s 脚本</c> 就是这么用的）。
///
/// 为什么不用长驻进程：
///   1. 效果一样。diskpart 的 `select disk 0` 只改会话内部状态，重放一遍与保持进程等价；
///   2. **更安全**。不必在后台留一个已提权的解释器等输入；
///   3. 更好验收：每一步都能看到完整脚本（R6），而长驻会话的"上一句是什么"很容易说不清。
///   4. 逻辑变成纯函数，可以单测——长驻进程的状态只能在真机上试。
///
/// 代价：每条命令都要重新执行一遍选择类命令（它们只改状态，无副作用），输出里会看到它们。
/// 对 diskpart 这类工具这是**正常且透明**的做法；真正需要来回交互的程序走
/// <c>execution: terminal</c>（在真终端里打开）。
/// </summary>
public sealed record SessionCommand(string ActionId, string Command);

/// <summary>会话当前的状态：哪些前置条件已满足、捕获到的值是什么、靠哪些命令满足的。</summary>
public sealed class SessionState
{
    private readonly Dictionary<string, string?> _captures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionCommand> _establishedBy = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    /// <summary>已满足的状态键。</summary>
    public IReadOnlyCollection<string> ActiveKeys => _order;

    /// <summary>捕获到的值（例如 disk → "0"）。</summary>
    public IReadOnlyDictionary<string, string?> Captures => _captures;

    public bool IsEmpty => _order.Count == 0;

    /// <summary>是否满足这些前置条件（null / 空表示无要求）。</summary>
    public bool Satisfies(IEnumerable<string>? requires)
    {
        if (requires is null)
        {
            return true;
        }

        foreach (var key in requires)
        {
            if (!string.IsNullOrWhiteSpace(key) && !_captures.ContainsKey(key))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>记录"这条命令确立了状态 X"（同一个键只保留最后一次，重放时不会重复选）。</summary>
    public void Establish(string key, string? value, string actionId, string command)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!_captures.ContainsKey(key))
        {
            _order.Add(key);
        }

        _captures[key] = value;
        _establishedBy[key] = new SessionCommand(actionId, command);
    }

    /// <summary>按确立顺序取出需要重放的命令（同一个键只留最后一次）。</summary>
    public IReadOnlyList<SessionCommand> ReplayCommands() =>
        _order.Where(_establishedBy.ContainsKey).Select(key => _establishedBy[key]).ToList();

    public void Clear()
    {
        _captures.Clear();
        _establishedBy.Clear();
        _order.Clear();
    }

    /// <summary>给界面看的一句话状态（"已选中磁盘 0、已选中卷 3"）。</summary>
    public string Describe(IReadOnlyList<SessionStateSpec>? specs)
    {
        if (_order.Count == 0)
        {
            return "未开始（没有前置条件被满足）";
        }

        var parts = new List<string>();

        foreach (var key in _order)
        {
            var title = specs?.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal))?.Title ?? key;
            var value = _captures.GetValueOrDefault(key);

            parts.Add(string.IsNullOrWhiteSpace(value) ? title : $"{title} {value}");
        }

        return string.Join("、", parts);
    }
}

/// <summary>把会话状态 + 本次命令拼成要交给程序的脚本。</summary>
public static class SessionScriptBuilder
{
    public static string Build(SessionState state, string command, string? exitCommand = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var lines = new List<string>();

        // 先重放"当前生效的选择类命令"（同一个状态键只有最后一次）
        foreach (var replay in state.ReplayCommands())
        {
            if (!string.IsNullOrWhiteSpace(replay.Command) && !lines.Contains(replay.Command, StringComparer.Ordinal))
            {
                lines.Add(replay.Command);
            }
        }

        if (!string.IsNullOrWhiteSpace(command) && !lines.Contains(command, StringComparer.Ordinal))
        {
            lines.Add(command);
        }

        if (!string.IsNullOrWhiteSpace(exitCommand) && !lines.Contains(exitCommand, StringComparer.Ordinal))
        {
            lines.Add(exitCommand);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 把命令模板里的 <c>{字段id}</c> 换成用户填的值（例如 "select disk {index}" → "select disk 0"）。
    /// 找不到的占位符原样保留 —— 那说明清单写错了字段 id，界面上能一眼看出来，比静默替换成空串好。
    /// </summary>
    public static string Substitute(string template, IReadOnlyDictionary<string, object?>? values)
    {
        if (string.IsNullOrEmpty(template) || values is null)
        {
            return template ?? string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(template, @"\{(?<name>[A-Za-z0-9_]+)\}", match =>
        {
            var name = match.Groups["name"].Value;

            if (!values.TryGetValue(name, out var value) || value is null)
            {
                return match.Value;
            }

            return value switch
            {
                string text => text,
                bool flag => flag ? "yes" : "no",
                System.Collections.IEnumerable items => string.Join(",", items.Cast<object?>()),
                _ => value.ToString() ?? string.Empty,
            };
        });
    }

    /// <summary>脚本里包含哪些命令（界面预览与"这条命令到底跑了什么"都用它）。</summary>
    public static IReadOnlyList<string> Describe(string script) =>
        script.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
}
