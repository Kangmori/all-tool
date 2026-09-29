using System.Text;
using AllTool.Core.Diagnostics;

namespace AllTool.Core.Tests;

/// <summary>
/// "清理缓存"的测试。
///
/// 重点守两条：**缓存与用户数据要分开**（别让人误点一下就丢了记住的输入与分组），
/// 以及"列出来的大小 / 删掉的字节数"要对得上（不然界面上的提示就是骗人的）。
/// </summary>
public class AppStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "alltool-storage-" + Guid.NewGuid().ToString("N"));

    public AppStorageTests() => Directory.CreateDirectory(Path.Combine(_root, "logs"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响结论
        }
    }

    private AppStorage Storage => new(_root);

    private void WriteLog(string name, int bytes) =>
        File.WriteAllText(Path.Combine(_root, "logs", name), new string('x', bytes), Encoding.UTF8);

    private void WriteSettings()
    {
        File.WriteAllText(Path.Combine(_root, "last-values.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "grouping.json"), "{}");
    }

    [Fact]
    public void 日志被标成缓存_设置被标成用户数据()
    {
        WriteLog("alltool.log", 100);
        WriteSettings();

        var items = Storage.Inspect();

        var log = Assert.Single(items.Where(i => i.IsCache));
        Assert.Contains("日志", log.Title);
        Assert.True(log.Bytes > 0);

        var settings = items.Where(i => !i.IsCache).ToList();
        Assert.Equal(2, settings.Count);
        Assert.Contains(settings, i => i.Title.Contains("上次输入"));
        Assert.Contains(settings, i => i.Title.Contains("分组"));
    }

    [Fact]
    public void 清日志不会碰设置()
    {
        WriteLog("alltool.log", 100);
        WriteLog("alltool.1.log", 200);
        WriteSettings();

        // 按删除前的真实大小算（WriteAllText 会写 BOM，写死 300 是不对的）
        var logsDirectory = Path.Combine(_root, "logs");
        var expected = Directory.GetFiles(logsDirectory, "*.log").Sum(f => new FileInfo(f).Length);

        var freed = Storage.DeleteLogs();

        Assert.Equal(expected, freed);
        Assert.Empty(Directory.GetFiles(logsDirectory, "*.log"));
        Assert.True(File.Exists(Storage.LastValuesPath), "清缓存不该动用户数据");
        Assert.True(File.Exists(Storage.GroupingPath));
    }

    [Fact]
    public void 清用户数据会删掉设置文件()
    {
        WriteSettings();

        var freed = Storage.DeleteUserData();

        Assert.True(freed > 0);
        Assert.False(File.Exists(Storage.LastValuesPath));
        Assert.False(File.Exists(Storage.GroupingPath));
    }

    [Fact]
    public void 没有任何东西时列出空表_删除返回零()
    {
        Assert.Empty(Storage.Inspect());
        Assert.Equal(0, Storage.DeleteLogs());
        Assert.Equal(0, Storage.DeleteUserData());
    }

    [Fact]
    public void 大小文本可读()
    {
        WriteLog("alltool.log", 2048);

        var item = Assert.Single(Storage.Inspect());

        Assert.Contains("KB", item.SizeText);
        Assert.Equal("2.0 KB", item.SizeText);
    }
}
