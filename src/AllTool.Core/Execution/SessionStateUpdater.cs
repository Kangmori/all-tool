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
        out string? matchedKey)
    {
        ArgumentNullException.ThrowIfNull(state);

        matchedKey = null;

        if (session?.State is null || session.State.Count == 0)
        {
            return false;
        }

        var text = output ?? string.Empty;

        // 先看失败：清单给了 errorPattern 且命中，就不确立任何状态
        if (!string.IsNullOrWhiteSpace(session.ErrorPattern) && SafeMatch(session.ErrorPattern, text))
        {
            return false;
        }

        foreach (var spec in session.State)
        {
            if (string.IsNullOrWhiteSpace(spec.Key) || string.IsNullOrWhiteSpace(spec.SuccessPattern))
            {
                continue;
            }

            var match = SafeMatchResult(spec.SuccessPattern, text);

            if (match is null)
            {
                continue;
            }

            // capture 指定了捕获组名，就取那个组的值；否则用第一个组（没有组就记 "yes"）
            string? value = null;

            if (!string.IsNullOrWhiteSpace(spec.Capture))
            {
                var group = match.Groups[spec.Capture];

                value = group.Success
                    ? group.Value
                    : match.Groups.Count > 1 && match.Groups[1].Success ? match.Groups[1].Value : null;
            }
            else if (match.Groups.Count > 1 && match.Groups[1].Success)
            {
                value = match.Groups[1].Value;
            }

            state.Establish(spec.Key, value ?? "yes", actionId, command);
            matchedKey = spec.Key;

            return true;
        }

        return false;
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
