using AllTool.Core.Settings;

namespace AllTool.Core.Tests;

/// <summary>
/// 「记住上次值」的测试。重点在两条：跨实例能读回来，以及文件坏了也不能把宿主搞崩。
/// </summary>
public class LastValuesStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "alltool-lastvalues-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void 存进去的值能被同一个实例读回来()
    {
        var path = TempPath();

        try
        {
            var store = new LastValuesStore(path);
            store.Set("7zip", "extract", "archive", @"D:\a.zip");
            store.Set("7zip", "extract", "outputDir", @"D:\out");

            Assert.Equal(@"D:\a.zip", store.Get("7zip", "extract", "archive"));
            Assert.Equal(@"D:\out", store.Get("7zip", "extract", "outputDir"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 保存后新实例能读回来()
    {
        var path = TempPath();

        try
        {
            var first = new LastValuesStore(path);
            first.Set("7zip", "extract", "archive", @"D:\包.7z");
            first.Save();

            var second = new LastValuesStore(path);

            Assert.Equal(@"D:\包.7z", second.Get("7zip", "extract", "archive"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 不同动作的同一个字段互不干扰()
    {
        var path = TempPath();

        try
        {
            var store = new LastValuesStore(path);
            store.Set("7zip", "extract", "archive", "for-extract");
            store.Set("7zip", "add", "archive", "for-add");
            store.Save();

            var reloaded = new LastValuesStore(path);

            Assert.Equal("for-extract", reloaded.Get("7zip", "extract", "archive"));
            Assert.Equal("for-add", reloaded.Get("7zip", "add", "archive"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 没存过的键返回_null()
    {
        var store = new LastValuesStore(TempPath());

        Assert.Null(store.Get("nope", "nope", "nope"));
    }

    [Fact]
    public void 设成空值等于删掉这一条()
    {
        var path = TempPath();

        try
        {
            var store = new LastValuesStore(path);
            store.Set("7zip", "extract", "archive", "x");
            store.Set("7zip", "extract", "archive", "");

            Assert.Null(store.Get("7zip", "extract", "archive"));
            Assert.Equal(0, store.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 文件损坏时当作空而不是抛异常()
    {
        var path = TempPath();

        try
        {
            File.WriteAllText(path, "{ 这不是合法 JSON");
            var store = new LastValuesStore(path);

            Assert.Null(store.Get("7zip", "extract", "archive"));

            // 而且仍然可用
            store.Set("7zip", "extract", "archive", "y");
            store.Save();
            Assert.Equal("y", new LastValuesStore(path).Get("7zip", "extract", "archive"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 目录不存在时会自动创建()
    {
        var root = Path.Combine(Path.GetTempPath(), "alltool-lastvalues-dir-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "nested", "last-values.json");

        try
        {
            var store = new LastValuesStore(path);
            store.Set("scoop", "list", "filter", "git");
            store.Save();

            Assert.True(File.Exists(path));
            Assert.Equal("git", new LastValuesStore(path).Get("scoop", "list", "filter"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
