using Swpj.Core.Execution;
using Swpj.Core.Manifest;

namespace Swpj.Core.Tests;

/// <summary>
/// 用仓库里真实的工具包清单做集成测试。
///
/// 这类测试的价值：验证"规范 + 真实的 manifest"确实能驱动出正确的 argv，
/// 而不是只验证我自己构造的测试夹具。清单改了、加载器改坏了，这里会立刻响。
/// </summary>
public class RealManifestTests
{
    private static readonly string RepoRoot = TestRepo.Root;

    private static ToolManifest Load7Zip() =>
        ManifestLoader.LoadFromFile(Path.Combine(RepoRoot, "plugins", "7zip", "manifest.yaml"));

    [Fact]
    public void 仓库里的_7zip_清单能被加载并通过结构校验()
    {
        var manifest = Load7Zip();

        Assert.Equal(1, manifest.Spec);
        Assert.Equal("7zip", manifest.Id);
        Assert.NotNull(manifest.Locate);
        Assert.Equal("7z", manifest.Locate!.Executable);
        Assert.Equal(11, manifest.Actions!.Count);
    }

    [Fact]
    public void 加载全部工具包都不应抛异常()
    {
        var plugins = ManifestLoader.LoadAll(Path.Combine(RepoRoot, "plugins"));

        Assert.NotEmpty(plugins);
        Assert.All(plugins, p => Assert.False(string.IsNullOrWhiteSpace(p.Manifest.Id)));
    }

    /// <summary>
    /// 宿主会先把字段默认值填进值字典，再交给 ArgvBuilder（空值不输出）。
    /// 这里模拟同样的做法，验证真实清单能生成预期 argv。
    /// </summary>
    private static Dictionary<string, object?> Defaults(ManifestAction action, params (string Id, object? Value)[] overrides)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var field in action.Fields ?? [])
        {
            values[field.Id!] = field.Type == "bool" ? false : field.Default;
        }

        foreach (var (id, value) in overrides)
        {
            values[id] = value;
        }

        return values;
    }

    [Fact]
    public void 解压动作能生成预期的_argv()
    {
        var manifest = Load7Zip();
        var action = manifest.Actions!.Single(a => a.Id == "extract");

        var argv = ArgvBuilder.Build(action, Defaults(action, ("archive", @"D:\a.zip"), ("outputDir", @"D:\out")));

        // 顺序即字段声明顺序：archive → outputDir → overwrite → … → recurse → … → logLevel
        Assert.Equal(["x", @"D:\a.zip", @"-oD:\out", "-aoa", "-r-", "-bb0"], argv);
    }

    [Fact]
    public void 添加动作能生成预期的_argv()
    {
        var manifest = Load7Zip();
        var action = manifest.Actions!.Single(a => a.Id == "add");

        var argv = ArgvBuilder.Build(action, Defaults(action,
            ("archive", "a.7z"), ("inputs", new[] { "f.txt", "sub" })));

        Assert.Equal(["a", "a.7z", "f.txt", "sub", "-t7z", "-mx5", "-r-", "-bb0"], argv);
    }

    [Fact]
    public void 重命名动作能把每行一对参数展开成位置参数()
    {
        var manifest = Load7Zip();
        var action = manifest.Actions!.Single(a => a.Id == "rename");

        var argv = ArgvBuilder.Build(action, Defaults(action,
            ("archive", "a.7z"), ("renamePairs", "old.txt new.txt\n2.txt folder\\2new.txt")));

        Assert.Equal(["rn", "a.7z", "old.txt", "new.txt", "2.txt", "folder\\2new.txt"], argv);
    }

    [Fact]
    public void 每个动作用默认值都能生成_argv_且不抛异常()
    {
        var manifest = Load7Zip();

        foreach (var action in manifest.Actions!)
        {
            var exception = Record.Exception(() => ArgvBuilder.Build(action, Defaults(action)));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void 展示用的命令行不含空_token()
    {
        var manifest = Load7Zip();
        var action = manifest.Actions!.Single(a => a.Id == "test");
        var argv = ArgvBuilder.Build(action, Defaults(action, ("archive", "a.zip")));

        var display = ArgvBuilder.FormatForDisplay("7z", argv);

        Assert.DoesNotContain("  ", display, StringComparison.Ordinal);
        Assert.StartsWith("7z t a.zip", display, StringComparison.Ordinal);
    }
}
