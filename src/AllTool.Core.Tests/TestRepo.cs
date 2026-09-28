namespace AllTool.Core.Tests;

/// <summary>测试用的仓库定位helper。</summary>
internal static class TestRepo
{
    public static string Root { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException(
            "向上找不到仓库根目录（以 AGENTS.md 为标志）。测试必须从仓库内运行。");
    }
}

/// <summary>
/// 同步的进度接收器。
/// 测试里**不用** <c>System.Progress&lt;T&gt;</c>：它把回调投递到同步上下文/线程池，
/// 断言时可能还没落地，会造成偶发失败。这里同步收集，行为确定。
/// </summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Lock _gate = new();
    private readonly List<T> _items = [];

    public IReadOnlyList<T> Items
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    public void Report(T value)
    {
        lock (_gate)
        {
            _items.Add(value);
        }
    }
}
