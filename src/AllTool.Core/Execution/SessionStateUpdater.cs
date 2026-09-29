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
        string? establishes,
        string command,
        string? output,
        SessionState state,
        out string? matchedKey,
        IReadOnlyDictionary<string, object?>? fields = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        matchedKey = null;

        // **只有声明了 establishes 的动作才写状态**，而且只写它声明的那一个。
        //
        // 原来这里是"遍历 session.state 取第一个"，结果是：任何执行成功的动作都会去写第一个状态。
        // 实测后果：select disk 0 建立 disk=0 之后，跑一条 list partition（它本该什么都不建立）
        // 会把 disk 覆盖成 yes（它的字段里没有 index，取不到值就回落成 yes），
        // 用户看到状态从「已选中磁盘 0」变成「已选中磁盘 yes」，后续动作随之行为异常。
        if (string.IsNullOrWhiteSpace(establishes))
        {
            return false;
        }

        var spec = session?.State?.FirstOrDefault(
            s => string.Equals(s.Key, establishes, StringComparison.Ordinal));

        if (spec is null || string.IsNullOrWhiteSpace(spec.Key))
        {
            return false;
        }

        var text = output ?? string.Empty;

        // 失败判定：命中任一条错误模式就不确立状态（支持多语言多条）。
        // 成功**不看提示文案**：本地化程序（中文系统上的 diskpart）不输出英文提示。
        foreach (var pattern in session!.AllErrorPatterns())
        {
            if (SafeMatch(pattern, text))
            {
                return false;
            }
        }

        state.Establish(spec.Key!, CaptureValue(spec, text, fields), actionId, command);
        matchedKey = spec.Key;

        return true;
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
