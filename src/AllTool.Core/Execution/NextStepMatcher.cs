using System.Text.RegularExpressions;
using AllTool.Core.Manifest;

namespace AllTool.Core.Execution;

/// <summary>一条"下一步"建议。</summary>
public sealed record NextStepSuggestion(
    string Title,
    ManifestAction Target,
    string? Reason,
    IReadOnlyDictionary<string, object?> Values,
    string? GroupId = null,
    string? GroupTitle = null);

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

            var template = step.Values ?? new Dictionary<string, object?>(StringComparer.Ordinal);

            // 没写 when：总是推荐一条（values 原样使用）
            if (string.IsNullOrWhiteSpace(step.When))
            {
                result.Add(new NextStepSuggestion(
                    step.Title!, target, step.Reason, template, GroupKey(step), GroupTitle(step)));

                continue;
            }

            // 写了 when：**命中几次就生成几个选项**，标题与 values 里的 {1}/{2}/{name}
            // 用该次匹配的捕获组填充。
            // 这样 list disk 之后可以直接列出"选择磁盘 0 / 1 / 2…"，
            // 而 scoop status 之后可以直接列出"更新某个应用"——规则仍然只写在清单里。
            MatchCollection matches;
            try
            {
                matches = Regex.Matches(text, step.When, RegexOptions.Multiline);
            }
            catch (ArgumentException)
            {
                // 正则写错属于工具包作者的错：不推荐、也不让界面崩
                continue;
            }

            if (matches.Count == 0)
            {
                continue;
            }

            // **只有用了占位符的规则才按匹配次数展开**。
            // 否则像 scoop status 的「一键更新全部」（when 能命中好几行、但没写占位符）会被展开成
            // 好几个一模一样的按钮——这是我引入多选项机制时漏掉的一环。
            var usesPlaceholder = (step.Title?.Contains('{') ?? false)
                || template.Values.Any(v => v?.ToString()?.Contains('{') == true);

            if (!usesPlaceholder)
            {
                result.Add(new NextStepSuggestion(
                    step.Title!, target, step.Reason, template, GroupKey(step), GroupTitle(step)));
                continue;
            }

            var limit = step.MaxOptions is > 0 ? step.MaxOptions!.Value : 6;

            foreach (Match match in matches.Take(limit))
            {
                var values = template.ToDictionary(
                    kv => kv.Key,
                    kv => (object?)Fill(kv.Value?.ToString(), match),
                    StringComparer.Ordinal);

                result.Add(new NextStepSuggestion(
                    Fill(step.Title, match) ?? step.Title!,
                    target,
                    step.Reason,
                    values,
                    GroupKey(step),
                    GroupTitle(step)));
            }
        }

        return result;
    }

    /// <summary>
    /// 把模板里的 <c>{1}</c>…<c>{9}</c>（编号组）与 <c>{名字}</c>（命名组）
    /// 换成这次匹配捕获到的内容。取不到就原样保留——界面上能一眼看出清单写错了。
    /// </summary>
    private static string? Fill(string? template, Match match)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        return Regex.Replace(template, @"\{(?<name>[A-Za-z0-9_]+)\}", m =>
        {
            var key = m.Groups["name"].Value;

            if (int.TryParse(key, out var index) && index > 0 && index < match.Groups.Count)
            {
                return match.Groups[index].Value;
            }

            var named = match.Groups[key];

            return named.Success ? named.Value : m.Value;
        });
    }

    /// <summary>同一条规则生成的多条建议共用一个 GroupId，界面据此把它们收进一个列表选择。</summary>
    private static string GroupKey(NextStepSpec step) => $"{step.Action}|{step.Title}";

    /// <summary>分组标题：把规则标题里的占位符去掉（例如「更新 {1}」→「更新」）。</summary>
    private static string GroupTitle(NextStepSpec step)
    {
        var title = Regex.Replace(step.Title ?? string.Empty, @"\{[A-Za-z0-9_]+\}", string.Empty);

        return title.Trim();
    }
}
