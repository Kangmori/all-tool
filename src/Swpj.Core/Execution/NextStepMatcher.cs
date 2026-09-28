using System.Text.RegularExpressions;
using Swpj.Core.Manifest;

namespace Swpj.Core.Execution;

/// <summary>一条"下一步"建议。</summary>
public sealed record NextStepSuggestion(
    string Title,
    ManifestAction Target,
    string? Reason,
    IReadOnlyDictionary<string, object?> Values);

/// <summary>
/// 根据刚执行完的动作与它的输出，算出可以推荐哪些"下一步"。
///
/// 推荐规则**写在清单里**（动作的 <c>nextSteps</c>），不是宿主硬编码的——理由有三：
///   1. 符合本项目的底线：参数与行为的知识都来自清单，可审计、可追溯出处；
///   2. 不同软件该推荐什么，只有工具包作者知道（scoop status → update，uv lock → sync …）；
///   3. 纯正则匹配，宿主不需要理解任何具体软件的语义。
///
/// 这个类是纯函数，所以推荐逻辑本身可以被单测覆盖，而不必靠点界面去试。
/// </summary>
public static class NextStepMatcher
{
    public static IReadOnlyList<NextStepSuggestion> Match(
        ManifestAction completed,
        IEnumerable<ManifestAction> availableActions,
        string? output)
    {
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(availableActions);

        if (completed.NextSteps is null || completed.NextSteps.Count == 0)
        {
            return [];
        }

        var lookup = availableActions
            .Where(a => !string.IsNullOrEmpty(a.Id))
            .GroupBy(a => a.Id!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var text = output ?? string.Empty;
        var result = new List<NextStepSuggestion>();

        foreach (var step in completed.NextSteps)
        {
            if (string.IsNullOrWhiteSpace(step.Action) || string.IsNullOrWhiteSpace(step.Title))
            {
                continue;
            }

            // 目标动作必须真的存在——写错了应该在界面上看不见，而不是点了才报错
            if (!lookup.TryGetValue(step.Action, out var target))
            {
                continue;
            }

            // 指定了 when 就必须命中（对输出做多行正则匹配）；不指定则总是推荐
            if (!string.IsNullOrWhiteSpace(step.When))
            {
                bool matched;
                try
                {
                    matched = Regex.IsMatch(text, step.When, RegexOptions.Multiline);
                }
                catch (ArgumentException)
                {
                    // 正则写错属于工具包作者的错：不推荐、也不让界面崩
                    continue;
                }

                if (!matched)
                {
                    continue;
                }
            }

            result.Add(new NextStepSuggestion(
                step.Title!,
                target,
                step.Reason,
                step.Values ?? new Dictionary<string, object?>(StringComparer.Ordinal)));
        }

        return result;
    }
}
