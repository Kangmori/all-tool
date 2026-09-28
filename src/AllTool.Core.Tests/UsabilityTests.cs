using AllTool.Core.Execution;
using AllTool.Core.Manifest;
using AllTool.Core.Settings;

namespace AllTool.Core.Tests;

public class NextStepMatcherTests
{
    private static ManifestAction Make(string id, params NextStepSpec[] steps) => new()
    {
        Id = id,
        Title = id,
        Command = id,
        Sources = [new ManifestSource { Title = "t", Retrieved = "2026-09-27" }],
        NextSteps = steps.Length == 0 ? null : [.. steps],
    };

    [Fact]
    public void 没有声明下一步时不推荐任何东西()
    {
        var completed = Make("status");

        Assert.Empty(NextStepMatcher.Match(completed, [completed], "有可用更新"));
    }

    [Fact]
    public void 输出命中_when_时推荐对应的动作()
    {
        var status = Make("status", new NextStepSpec
        {
            Title = "一键更新全部",
            Action = "update",
            When = "Updates are available|有可用更新",
            Reason = "检测到有可用更新",
        });
        var update = Make("update");

        var suggestions = NextStepMatcher.Match(status, [status, update], "WARN  Updates are available for: git");

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("一键更新全部", suggestion.Title);
        Assert.Equal("update", suggestion.Target.Id);
        Assert.Equal("检测到有可用更新", suggestion.Reason);
    }

    [Fact]
    public void 输出没命中时不推荐()
    {
        var status = Make("status", new NextStepSpec
        {
            Title = "一键更新",
            Action = "update",
            When = "Updates are available",
        });
        var update = Make("update");

        Assert.Empty(NextStepMatcher.Match(status, [status, update], "Everything is up to date"));
    }

    [Fact]
    public void 正则按多行匹配_所以能匹配到非首行的内容()
    {
        var status = Make("status", new NextStepSpec
        {
            Title = "更新",
            Action = "update",
            When = "^Updates are available",
        });
        var update = Make("update");

        var output = "Scoop is up to date\nUpdates are available for: git\n";

        Assert.Single(NextStepMatcher.Match(status, [status, update], output));
    }

    [Fact]
    public void 不写_when_时总是推荐()
    {
        var list = Make("list", new NextStepSpec { Title = "看看状态", Action = "status" });
        var status = Make("status");

        Assert.Single(NextStepMatcher.Match(list, [list, status], "随便什么输出"));
        Assert.Single(NextStepMatcher.Match(list, [list, status], null));
    }

    [Fact]
    public void 目标动作不存在时静默跳过_而不是点了才报错()
    {
        var status = Make("status", new NextStepSpec { Title = "更新", Action = "不存在的动作" });

        Assert.Empty(NextStepMatcher.Match(status, [status], "任意输出"));
    }

    [Fact]
    public void 正则写错时跳过而不是让界面崩()
    {
        var status = Make("status", new NextStepSpec { Title = "更新", Action = "update", When = "([未闭合" });
        var update = Make("update");

        Assert.Empty(NextStepMatcher.Match(status, [status, update], "任意输出"));
    }

    [Fact]
    public void 缺少_title_或_action_的条目被忽略()
    {
        var status = Make("status",
            new NextStepSpec { Action = "update" },
            new NextStepSpec { Title = "没有目标" },
            new NextStepSpec { Title = "正常", Action = "update" });
        var update = Make("update");

        var suggestions = NextStepMatcher.Match(status, [status, update], "x");

        Assert.Single(suggestions);
        Assert.Equal("正常", suggestions[0].Title);
    }

    [Fact]
    public void 预填值会带出来()
    {
        var status = Make("status", new NextStepSpec
        {
            Title = "只更新 git",
            Action = "update",
            Values = new Dictionary<string, object?> { ["apps"] = "git" },
        });
        var update = Make("update");

        var suggestion = Assert.Single(NextStepMatcher.Match(status, [status, update], "x"));

        Assert.Equal("git", suggestion.Values["apps"]);
    }
}

public class PathDropLogicTests
{
    [Fact]
    public void 单值字段_用拖入的第一个路径替换()
    {
        var result = PathDropLogic.Apply(@"C:\old.zip", [@"D:\新 文件.zip", @"D:\另一个.zip"], multiValue: false);

        Assert.Equal(@"D:\新 文件.zip", result);
    }

    [Fact]
    public void 多值字段_追加并去重()
    {
        var result = PathDropLogic.Apply(
            @"C:\已有.txt",
            [@"C:\已有.txt", @"D:\新.txt"],
            multiValue: true);

        var lines = PathDropLogic.SplitLines(result);

        Assert.Equal([@"C:\已有.txt", @"D:\新.txt"], lines);
    }

    [Fact]
    public void 多值字段_去重不区分大小写()
    {
        var result = PathDropLogic.Apply(@"C:\A.TXT", [@"c:\a.txt"], multiValue: true);

        Assert.Single(PathDropLogic.SplitLines(result));
    }

    [Fact]
    public void 多值字段_能处理用回车换行的已有内容()
    {
        // WinUI 的 TextBox 用 \r 换行
        var result = PathDropLogic.Apply("a.txt\rb.txt", ["c.txt"], multiValue: true);

        Assert.Equal(["a.txt", "b.txt", "c.txt"], PathDropLogic.SplitLines(result));
    }

    [Fact]
    public void 拖入内容为空时保持原样()
    {
        Assert.Equal("原样", PathDropLogic.Apply("原样", [], multiValue: true));
        Assert.Equal("原样", PathDropLogic.Apply("原样", ["   "], multiValue: false));
        Assert.Equal(string.Empty, PathDropLogic.Apply(null, [], multiValue: false));
    }

    [Fact]
    public void 拖入多个到单值字段_只取第一个()
    {
        var result = PathDropLogic.Apply(null, ["a", "b", "c"], multiValue: false);

        Assert.Equal("a", result);
    }

    [Fact]
    public void 提示文案会说明拖入了几项()
    {
        Assert.Contains("2 项", PathDropLogic.DescribeDrop(["a", "b"]));
        Assert.Contains("没有识别到", PathDropLogic.DescribeDrop([]));
    }
}

/// <summary>
/// 危险级别的规范化。这里守的是一个真实的 bug：可空的 Danger 直接拿去比会误判。
/// </summary>
public class DangerLevelTests
{
    private static ManifestAction Make(string? danger) => new()
    {
        Id = "x",
        Title = "x",
        Command = "x",
        Danger = danger,
        Sources = [new ManifestSource { Title = "t", Retrieved = "2026-09-27" }],
    };

    [Fact]
    public void 没写_danger_就是_none_而不是被当成会覆盖数据()
    {
        Assert.Equal(DangerLevel.None, Make(null).DangerOrDefault);
        Assert.Equal(DangerLevel.None, Make("").DangerOrDefault);
        Assert.Equal(DangerLevel.None, Make("   ").DangerOrDefault);
    }

    [Fact]
    public void 写了_danger_就按写的来_并去掉空白()
    {
        Assert.Equal(DangerLevel.Overwrite, Make("overwrite").DangerOrDefault);
        Assert.Equal(DangerLevel.Destructive, Make(" destructive ").DangerOrDefault);
    }

    [Fact]
    public void 没写_danger_的动作不应被判为需要二次确认()
    {
        // 二次确认的条件是 == destructive，这里守的是"null 不等于 destructive"这条常识
        Assert.NotEqual(DangerLevel.Destructive, Make(null).DangerOrDefault);
        Assert.Equal(DangerLevel.Destructive, Make("destructive").DangerOrDefault);
    }
}

public class GroupingStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "alltool-grouping-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void 工具包与动作的分组能保存并读回()
    {
        var path = TempPath();

        try
        {
            var store = new GroupingStore(path);
            store.SetPackageGroup("scoop", "包管理");
            store.SetActionGroup("scoop", "install", "安装与卸载");
            store.Save();

            var reloaded = new GroupingStore(path);

            Assert.Equal("包管理", reloaded.GetPackageGroup("scoop"));
            Assert.Equal("安装与卸载", reloaded.GetActionGroup("scoop", "install"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 不同工具包的同名动作互不干扰()
    {
        var path = TempPath();

        try
        {
            var store = new GroupingStore(path);
            store.SetActionGroup("scoop", "list", "查询");
            store.SetActionGroup("uv", "list", "别的");
            store.Save();

            var reloaded = new GroupingStore(path);

            Assert.Equal("查询", reloaded.GetActionGroup("scoop", "list"));
            Assert.Equal("别的", reloaded.GetActionGroup("uv", "list"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 设成空值等于恢复默认分组()
    {
        var path = TempPath();

        try
        {
            var store = new GroupingStore(path);
            store.SetPackageGroup("scoop", "包管理");
            store.SetPackageGroup("scoop", "   ");

            Assert.Null(store.GetPackageGroup("scoop"));
            Assert.Equal(0, store.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 文件损坏时按空处理且仍然可用()
    {
        var path = TempPath();

        try
        {
            File.WriteAllText(path, "这不是 JSON");
            var store = new GroupingStore(path);

            Assert.Null(store.GetPackageGroup("scoop"));

            store.SetPackageGroup("scoop", "包管理");
            store.Save();

            Assert.Equal("包管理", new GroupingStore(path).GetPackageGroup("scoop"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 没设过时返回_null_表示用清单里的默认值()
    {
        var store = new GroupingStore(TempPath());

        Assert.Null(store.GetPackageGroup("uv"));
        Assert.Null(store.GetActionGroup("uv", "sync"));
    }
}
