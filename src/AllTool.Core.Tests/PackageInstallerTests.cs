using System.IO.Compression;
using AllTool.Core.Discovery;

namespace AllTool.Core.Tests;

/// <summary>
/// 工具包安装/卸载的测试。
///
/// 界面上"拖一个工具包进来就装上"没法自动化，但**装/卸本身的文件语义**可以钉死：
/// 装进哪、同名怎么办、语法错的清单要不要收、卸载是删还是留后路。
/// </summary>
public class PackageInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "alltool-install-" + Guid.NewGuid().ToString("N"));

    public PackageInstallerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响结论
        }
    }

    private string PluginsRoot => Path.Combine(_root, "plugins");

    private static string ManifestYaml(string id, string name = "演示包") => $"""
        spec: 1
        id: {id}
        name: {name}
        manifestVersion: 0.1.0
        appVersion: "1.0"
        description: 测试用的最小清单
        sources:
          - title: 测试来源
            url: https://example.com
            retrieved: "2026-09-27"
        locate:
          executable: cmd.exe
        runtime:
          encoding: utf-8
        actions:
          - id: hello
            title: 打招呼
            command: /c
            sources:
              - title: 测试来源
                url: https://example.com
                retrieved: "2026-09-27"
            fields:
              - id: who
                label: 谁
                type: text
                style: separate
                prefix: "--name"
        """;

    private string MakePackageFolder(string folderName, string id, string? extraFile = null)
    {
        var directory = Path.Combine(_root, folderName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.yaml"), ManifestYaml(id));

        if (extraFile is not null)
        {
            File.WriteAllText(Path.Combine(directory, extraFile), "随包一起装过来的文件");
        }

        return directory;
    }

    [Fact]
    public void 从目录安装会把整个包复制到_plugins_下()
    {
        var source = MakePackageFolder("来源", "demo", extraFile: "NOTES.md");

        var result = PackageInstaller.Install(source, PluginsRoot);

        Assert.True(result.Success, result.Message);
        Assert.Equal("demo", result.PackageId);
        Assert.True(File.Exists(Path.Combine(PluginsRoot, "demo", "manifest.yaml")));
        Assert.True(File.Exists(Path.Combine(PluginsRoot, "demo", "NOTES.md")));
    }

    [Fact]
    public void 目标目录名按清单里的_id_而不是来源目录名()
    {
        // 规范要求 id 必须等于目录名，所以安装时要按 id 建目录
        var source = MakePackageFolder("随便叫什么都行", "demo");

        var result = PackageInstaller.Install(source, PluginsRoot);

        Assert.True(result.Success, result.Message);
        Assert.True(Directory.Exists(Path.Combine(PluginsRoot, "demo")));
        Assert.False(Directory.Exists(Path.Combine(PluginsRoot, "随便叫什么都行")));
    }

    [Fact]
    public void 从_zip_安装即使里面多套了一层目录也能装上()
    {
        var source = MakePackageFolder("打包前", "demo", extraFile: "NOTES.md");
        var zip = Path.Combine(_root, "demo.zip");
        ZipFile.CreateFromDirectory(source, zip);

        var result = PackageInstaller.Install(zip, PluginsRoot);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(PluginsRoot, "demo", "manifest.yaml")));
        Assert.True(File.Exists(Path.Combine(PluginsRoot, "demo", "NOTES.md")));
    }

    [Fact]
    public void 从单个清单文件安装会建出对应目录()
    {
        var single = Path.Combine(_root, "manifest.yaml");
        File.WriteAllText(single, ManifestYaml("demo"));

        var result = PackageInstaller.Install(single, PluginsRoot);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(PluginsRoot, "demo", "manifest.yaml")));
    }

    [Fact]
    public void 清单不合规范时拒绝安装并说明原因()
    {
        var directory = Path.Combine(_root, "坏包");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.yaml"), "spec: 1\nid: broken\n");   // 缺一堆必填

        var result = PackageInstaller.Install(directory, PluginsRoot);

        Assert.False(result.Success);
        Assert.False(Directory.Exists(Path.Combine(PluginsRoot, "broken")));
        Assert.Contains("未安装", result.Message);
    }

    [Fact]
    public void 目录里没有清单时拒绝安装()
    {
        var directory = Path.Combine(_root, "空的");
        Directory.CreateDirectory(directory);

        var result = PackageInstaller.Install(directory, PluginsRoot);

        Assert.False(result.Success);
        Assert.Contains("没有清单文件", result.Message);
    }

    [Fact]
    public void 装同名包等于升级_旧版本被备份而不是丢掉()
    {
        var first = MakePackageFolder("v1", "demo");
        Assert.True(PackageInstaller.Install(first, PluginsRoot).Success);

        var second = MakePackageFolder("v2", "demo", extraFile: "新版本.md");
        var result = PackageInstaller.Install(second, PluginsRoot);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.ReplacedBackup);
        Assert.True(Directory.Exists(result.ReplacedBackup!));
        Assert.True(File.Exists(Path.Combine(PluginsRoot, "demo", "新版本.md")));
        Assert.False(File.Exists(Path.Combine(PluginsRoot, "demo", "NOTES.md")));
    }

    [Fact]
    public void 卸载是移到回收站而不是删除()
    {
        var source = MakePackageFolder("来源", "demo", extraFile: "NOTES.md");
        Assert.True(PackageInstaller.Install(source, PluginsRoot).Success);
        var installed = Path.Combine(PluginsRoot, "demo");

        var result = PackageInstaller.Uninstall(installed, PluginsRoot);

        Assert.True(result.Success, result.Message);
        Assert.False(Directory.Exists(installed));
        Assert.NotNull(result.MovedTo);
        Assert.True(File.Exists(Path.Combine(result.MovedTo!, "manifest.yaml")));
        Assert.True(File.Exists(Path.Combine(result.MovedTo!, "NOTES.md")));
    }

    [Fact]
    public void 拒绝卸载_plugins_之外的目录()
    {
        var outside = Path.Combine(_root, "别处的目录");
        Directory.CreateDirectory(outside);

        var result = PackageInstaller.Uninstall(outside, PluginsRoot);

        Assert.False(result.Success);
        Assert.True(Directory.Exists(outside), "被拒绝的操作不应改动目录");
    }

    [Fact]
    public void 卸载不存在的目录时如实报告()
    {
        var result = PackageInstaller.Uninstall(Path.Combine(PluginsRoot, "没有这个"), PluginsRoot);

        Assert.False(result.Success);
        Assert.Contains("目录不存在", result.Message);
    }

    [Fact]
    public void 安装后再装载会跟原来的清单一致()
    {
        // 端到端：装进来 → 用宿主的真实加载器读一遍
        var source = MakePackageFolder("来源", "demo");
        Assert.True(PackageInstaller.Install(source, PluginsRoot).Success);

        var loaded = AllTool.Core.Manifest.ManifestLoader.LoadAll(PluginsRoot);

        var (_, manifest) = Assert.Single(loaded);
        Assert.Equal("demo", manifest.Id);
        Assert.Equal("hello", Assert.Single(manifest.Actions!).Id);
    }
}
