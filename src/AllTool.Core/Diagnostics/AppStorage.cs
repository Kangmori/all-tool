using System.Text;

namespace AllTool.Core.Diagnostics;

/// <summary><c>%APPDATA%\AllTool</c> 下的一项。</summary>
public sealed record StorageItem(string Title, string Path, long Bytes, bool IsCache)
{
    public string SizeText => Bytes < 1024
        ? $"{Bytes} B"
        : Bytes < 1024 * 1024
            ? $"{Bytes / 1024.0:F1} KB"
            : $"{Bytes / 1024.0 / 1024.0:F1} MB";
}

/// <summary>
/// 本软件自己产生的东西放哪、有多大、怎么清。
///
/// 为什么要单独抽出来：用户会问"这软件占了我什么、怎么清干净"。
/// 把它做成可测的纯文件操作（根目录可注入），比在界面里散着写 <c>Directory.Delete</c> 靠谱得多。
///
/// 两类要分清楚：
///   - **缓存**（<see cref="IsCache"/> = true）：日志。删了只损失排查材料。
///   - **用户数据**：记住的上次输入、自定义分组。删了会丢用户的设置，所以界面上默认**不勾**，
///     而且要单独确认。把这两类混成一个"清理"按钮是很容易让人误点丢东西的。
/// </summary>
public sealed class AppStorage
{
    public const string DirectoryName = "AllTool";

    private readonly string _root;

    public AppStorage(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _root = root;
    }

    public static AppStorage OpenDefault() =>
        new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            DirectoryName));

    public string Root => _root;

    public string LogsDirectory => Path.Combine(_root, "logs");

    public string LastValuesPath => Path.Combine(_root, "last-values.json");

    public string GroupingPath => Path.Combine(_root, "grouping.json");

    /// <summary>列出所有占用项（不存在的跳过），日志算缓存、设置算用户数据。</summary>
    public IReadOnlyList<StorageItem> Inspect()
    {
        var items = new List<StorageItem>();

        if (Directory.Exists(LogsDirectory))
        {
            var files = Directory.GetFiles(LogsDirectory, "*.log");
            var bytes = files.Sum(Size);

            if (files.Length > 0)
            {
                items.Add(new StorageItem($"日志文件（{files.Length} 个）", LogsDirectory, bytes, IsCache: true));
            }
        }

        Add(LastValuesPath, "记住的上次输入");
        Add(GroupingPath, "自定义分组");

        return items;

        void Add(string path, string title)
        {
            if (File.Exists(path))
            {
                items.Add(new StorageItem(title, path, Size(path), IsCache: false));
            }
        }
    }

    /// <summary>清掉日志，返回释放的字节数。删不掉的个别文件不影响整体。</summary>
    public long DeleteLogs()
    {
        if (!Directory.Exists(LogsDirectory))
        {
            return 0;
        }

        long freed = 0;

        foreach (var file in Directory.GetFiles(LogsDirectory, "*.log"))
        {
            var size = Size(file);

            try
            {
                File.Delete(file);
                freed += size;
            }
            catch (Exception)
            {
                // 可能正被另一个实例占着，跳过
            }
        }

        return freed;
    }

    /// <summary>清掉用户数据（记住的输入与自定义分组），返回释放的字节数。</summary>
    public long DeleteUserData()
    {
        long freed = 0;

        foreach (var path in new[] { LastValuesPath, GroupingPath })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var size = Size(path);

            try
            {
                File.Delete(path);
                freed += size;
            }
            catch (Exception)
            {
                // 同上
            }
        }

        return freed;
    }

    /// <summary>人类可读的大小，用于状态栏。</summary>
    public static string Describe(long bytes) =>
        bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:F1} KB";

    private static long Size(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
