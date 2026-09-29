using System.Text.RegularExpressions;
using AllTool.Core.Manifest;

namespace AllTool.Core.Execution;

/// <summary>
/// 根据一条命令的输出，判断它**成功了吗**、**确立了哪些状态**。
///
/// 全声明式：成功靠清单里的 <c>successPattern</c> 正则，失败靠 <c>errorPattern</c>。
/// 宿主不认识 diskpart，也不该认识——它只做正则匹配（与 <c>nextSteps.when</c>、
/// <c>output.progress</c> 同一套思路）。
/// </summary>
public static class SessionStateUpdater
{
    /// <summary>
    /// 把输出喂进来，更新状态。
    /// 返回：这条命令是否被判定为成功（true = 确立状态；false = 什么都没变）。
    /// </summary>
    public static bool Apply(
        SessionSpec? session,
        string actionId,
        string command,
        string? output,
        SessionState state,
        out string? matchedKey,
        IReadOnlyDictionary<string, object?>? fields = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        matchedKey = null;

        if (session?.State is null || session.State.Count == 0)
        {
            return false;
        }

        var text = output ?? string.Empty;

        // ---- 失败判定：命中任一条错误模式就算失败（支持多语言多条）----
        // 这是唯一的失败信号。**不拿成功提示当门槛**：本地化程序（中文系统上的 diskpart）
        // 根本不会输出英文提示，拿英文正则当门槛会让状态永远不确立（实测踩过）。
        foreach (var pattern in session.AllErrorPatterns())
        {
            if (SafeMatch(pattern, text))
            {
                return false;
            }
        }

        foreach (var spec in session.State)
        {
            if (string.IsNullOrWhiteSpace(spec.Key))
            {
                continue;
            }

            state.Establish(spec.Key, CaptureValue(spec, text, fields), actionId, command);
            matchedKey = spec.Key;

            return true;
        }

        return false;
    }

    /// <summary>
    /// 取这个状态的值：优先用字段值（captureField），其次用 successPattern 的捕获组，
    /// 都没有就记 yes。值只用于界面展示与 confirmPhrase 占位，不参与判定。
    /// </summary>
    private static string CaptureValue(
        SessionStateSpec spec,
        string text,
        IReadOnlyDictionary<string, object?>? fields)
    {
        if (!string.IsNullOrWhiteSpace(spec.CaptureField) && fields is not null
            && fields.TryGetValue(spec.CaptureField!, out var fieldValue) && fieldValue is not null)
        {
            var rendered = fieldValue.ToString();

            if (!string.IsNullOrWhiteSpace(rendered))
            {
                return rendered!;
            }
        }

        if (!string.IsNullOrWhiteSpace(spec.SuccessPattern))
        {
            var match = SafeMatchResult(spec.SuccessPattern!, text);

            if (match is not null)
            {
                if (!string.IsNullOrWhiteSpace(spec.Capture))
                {
                    var group = match.Groups[spec.Capture!];

                    if (group.Success)
                    {
                        return group.Value;
                    }
                }

                if (match.Groups.Count > 1 && match.Groups[1].Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }

        return "yes";
    }

    private static bool SafeMatch(string pattern, string text)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.Multiline);
        }
        catch (ArgumentException)
        {
            // 正则写错属于清单作者的错：当作"没命中"，不要让界面崩
            return false;
        }
    }

    private static Match? SafeMatchResult(string pattern, string text)
    {
        try
        {
            var match = Regex.Match(text, pattern, RegexOptions.Multiline);

            return match.Success ? match : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
