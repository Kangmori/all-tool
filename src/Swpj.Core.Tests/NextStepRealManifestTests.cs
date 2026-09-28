using Swpj.Core.Execution;
using Swpj.Core.Manifest;

namespace Swpj.Core.Tests;

/// <summary>
/// 拿**仓库里真实的工具包清单**验证"下一步"推荐。
///
/// 为什么要用真实清单：推荐规则是写在清单里的正则，而正则最容易犯的错是
/// "凭印象写模式"——最初的 scoop status 规则写的是 `Updates are available`，
/// 但 `scoop status --local` 实际打印的是表格，真实输出里 0 命中，
/// 界面上就是"什么也不推荐"。这个测试把真实输出与真实清单钉在一起，
/// 以后再改清单或改匹配逻辑都会立刻发现。
/// </summary>
public class NextStepRealManifestTests
{
    /// <summary>2026-09-27 在本机实测的 `scoop status --local` 输出（原样抄下来，含表头与分隔行）。</summary>
    private const string ScoopStatusOutput = """
        Name        Installed Version Latest Version   Missing Dependencies Info
        ----        ----------------- --------------   -------------------- ----
        ffmpeg      9.0.1             9.0.2                                 
        mpc-hc-fork 2.8.1             2.8.2                                 
        nodejs      26.8.2            26.10.0                               
        osulazer    2026.804.2-lazer  2026.921.0-lazer                      
        zulu-jdk    26.32.203         27.28.101                             

        """;

    /// <summary>全部都是最新时的输出（只有表头，没有数据行）。</summary>
    private const string ScoopStatusUpToDateOutput = """
        Name        Installed Version Latest Version   Missing Dependencies Info
        ----        ----------------- --------------   -------------------- ----

        """;

    private static (ToolManifest Manifest, ManifestAction Action) Load(string pluginId, string actionId)
    {
        var manifests = ManifestLoader.LoadAll(Path.Combine(TestRepo.Root, "plugins"));
        var manifest = manifests.Single(m => m.Manifest.Id == pluginId).Manifest;
        var action = manifest.Actions!.Single(a => a.Id == actionId);

        return (manifest, action);
    }

    [Fact]
    public void 真实清单里_scoop_status_在真有更新时推荐更新与清理()
    {
        var (manifest, status) = Load("scoop", "status");

        var suggestions = NextStepMatcher.Match(status, manifest.Actions!, ScoopStatusOutput);

        var titles = suggestions.Select(s => s.Title).ToList();
        Assert.Contains("一键更新全部", titles);
        Assert.Contains("清掉旧版本释放空间", titles);

        // 目标动作必须真的存在，否则点了才报错
        Assert.Equal("update", suggestions.First(s => s.Title == "一键更新全部").Target.Id);
        Assert.Equal("cleanup", suggestions.First(s => s.Title == "清掉旧版本释放空间").Target.Id);
    }

    [Fact]
    public void 真实清单里_scoop_status_在没有更新时不推荐()
    {
        var (manifest, status) = Load("scoop", "status");

        var suggestions = NextStepMatcher.Match(status, manifest.Actions!, ScoopStatusUpToDateOutput);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void 真实清单里_scoop_list_总是推荐去看状态()
    {
        var (manifest, list) = Load("scoop", "list");

        var suggestions = NextStepMatcher.Match(list, manifest.Actions!, "随便什么输出");

        Assert.Equal("status", Assert.Single(suggestions).Target.Id);
    }

    [Fact]
    public void 真实清单里_7zip_查看内容之后推荐解压()
    {
        var (manifest, list) = Load("7zip", "list");

        var suggestions = NextStepMatcher.Match(list, manifest.Actions!, "任意输出");

        Assert.Equal("extract", Assert.Single(suggestions).Target.Id);
    }

    [Fact]
    public void 所有工具包声明的_nextSteps_目标动作都必须存在()
    {
        // 这条守的是"写错动作 id"这种低级错误：宿主会静默忽略，作者却以为生效了
        var problems = new List<string>();

        foreach (var (_, manifest) in ManifestLoader.LoadAll(Path.Combine(TestRepo.Root, "plugins")))
        {
            var ids = (manifest.Actions ?? []).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var action in manifest.Actions ?? [])
            {
                foreach (var step in action.NextSteps ?? [])
                {
                    if (step.Action is null || !ids.Contains(step.Action))
                    {
                        problems.Add($"{manifest.Id}/{action.Id} → {step.Action}");
                    }
                }
            }
        }

        Assert.Empty(problems);
    }
}
