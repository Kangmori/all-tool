using AllTool.Core.Execution;
using AllTool.Core.Manifest;

namespace AllTool.Core.Tests;

/// <summary>
/// "从输出里生成多个下一步选项"的测试。
///
/// 这是产品负责人要的体验：`list disk` 跑完，直接列出「选择磁盘 0 / 选择磁盘 1 …」，
/// 点一下就把磁盘号填进 select-disk，而不是让用户自己回去看输出、再手打编号。
/// 规则仍写在清单里（when 正则命中几次就生成几个选项），所以这里测的是纯函数。
/// </summary>
public class NextStepOptionsTests
{
    private static ManifestAction Action(string id, List<NextStepSpec>? nextSteps = null) => new()
    {
        Id = id,
        Title = id,
        Command = "",
        NextSteps = nextSteps,
    };

    private static NextStepSpec Rule(
        string title,
        string action,
        string when,
        int? max = null,
        Dictionary<string, object?>? values = null) => new()
    {
        Title = title,
        Action = action,
        When = when,
        Values = values,
        MaxOptions = max,
    };

    [Fact]
    public void 输出里命中几次就生成几个选项_并用捕获组填标题与值()
    {
        var rule = Rule("选择磁盘 {1}", "select-disk", @"(?m)^\s*Disk (\d+)\s", max: 8,
            values: new Dictionary<string, object?> { ["index"] = "{1}" });
        var listDisk = Action("list-disk", [rule]);
        var selectDisk = Action("select-disk");

        var output = "  Disk ###  Status   Size\r\n  Disk 0    Online  476 GB\r\n  Disk 1    Online  931 GB\r\n";

        var suggestions = NextStepMatcher.Match(listDisk, [listDisk, selectDisk], output);

        Assert.Equal(2, suggestions.Count);
        Assert.Equal("选择磁盘 0", suggestions[0].Title);
        Assert.Equal("选择磁盘 1", suggestions[1].Title);
        Assert.Equal("0", suggestions[0].Values["index"]);
        Assert.Equal("1", suggestions[1].Values["index"]);
    }

    [Fact]
    public void maxOptions_限制生成的选项数量()
    {
        var rule = Rule("选择卷 {1}", "select-volume", @"(?m)^\s*Volume (\d+)\s", max: 2);
        var listVolume = Action("list-volume", [rule]);
        var target = Action("select-volume");

        var output = string.Join("\n", Enumerable.Range(0, 10).Select(i => $"  Volume {i}   Data"));

        var suggestions = NextStepMatcher.Match(listVolume, [listVolume, target], output);

        Assert.Equal(2, suggestions.Count);
    }

    [Fact]
    public void 没写when时只推荐一条_且values原样保留()
    {
        var rule = new NextStepSpec
        {
            Title = "查看详情",
            Action = "detail-disk",
            Values = new Dictionary<string, object?> { ["index"] = "0" },
        };

        var source = Action("select-disk", [rule]);
        var target = Action("detail-disk");

        var suggestions = NextStepMatcher.Match(source, [source, target], "任何输出");

        var only = Assert.Single(suggestions);
        Assert.Equal("查看详情", only.Title);
        Assert.Equal("0", only.Values["index"]);
    }

    [Fact]
    public void 正则写错时既不推荐也不崩()
    {
        var rule = Rule("坏的 {1}", "select-disk", "([未闭合");
        var source = Action("list-disk", [rule]);

        var suggestions = NextStepMatcher.Match(source, [source, Action("select-disk")], "输出");

        Assert.Empty(suggestions);
    }
}
