using System.Text;

namespace AllTool.Core.Diagnostics;

/// <summary>日志级别。按重要性递增排序。</summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
}

/// <summary>
/// 极简的滚动文件日志。
///
/// 为什么要它：宿主是"跑别人的程序"的工具，出问题时**现场信息全在输出里**
/// （命令、退出码、程序吐了什么）。界面上为了不卡顿只保留最后若干行（见宿主的输出批处理），
/// 所以必须有一份**完整**的落盘记录，否则用户报"某某命令失败了"时无从查起。
///
/// 设计取舍：
///   - **同步写、立即 flush**：崩溃/被杀时也要留下最后几行，这是日志的价值所在。
///     输出量已经由宿主批处理限制了写入频率，不需要异步缓冲。
///   - **单文件 + 轮转**：超过 <see cref="MaxBytesPerFile"/> 就换一个 `<名字>.1.log`，
///     最多保留 <see cref="MaxFiles"/> 个。日志是诊断材料，不是数据资产，够用即可。
///   - **写不进去不能让程序崩**：所有 IO 异常都被吞掉（日志失败不该变成第二个故障）。
/// </summary>
public sealed class FileLogger : IDisposable
{
    /// <summary>单个日志文件的上限（8 MB：足够装下几次超长输出）。</summary>
    public const long MaxBytesPerFile = 8 * 1024 * 1024;

    /// <summary>最多保留几个历史文件（含当前）。</summary>
    public const int MaxFiles = 5;

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _baseName;
    private readonly LogLevel _minimum;
    private readonly bool _echoToDebug;

    private string? _currentPath;
    private long _currentBytes;

    public FileLogger(string directory, string baseName = "alltool", LogLevel minimum = LogLevel.Info, bool echoToDebug = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = directory;
        _baseName = baseName;
        _minimum = minimum;
        _echoToDebug = echoToDebug;
    }

    /// <summary>默认位置：<c>%APPDATA%\AllTool\logs</c>。</summary>
    public static FileLogger OpenDefault(LogLevel minimum = LogLevel.Info) =>
        new(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AllTool",
                "logs"),
            minimum: minimum,
            echoToDebug: true);

    /// <summary>当前正在写的文件（用于在界面上告诉用户"日志在哪"）。</summary>
    public string? CurrentPath
    {
        get
        {
            lock (_gate)
            {
                return _currentPath;
            }
        }
    }

    public string Directory => _directory;

    public void Debug(string message) => Write(LogLevel.Debug, message);

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warn(string message) => Write(LogLevel.Warn, message);

    public void Error(string message) => Write(LogLevel.Error, message);

    /// <summary>记录异常：把类型、消息与堆栈都写进去（堆栈是排查的关键）。</summary>
    public void Exception(string context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Write(LogLevel.Error, $"{context}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception}");
    }

    /// <summary>记录一段多行文本（例如命令的完整输出）。</summary>
    public void Block(string title, string? content)
    {
        Write(LogLevel.Info, $"--- {title} ---");

        if (!string.IsNullOrEmpty(content))
        {
            foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                Write(LogLevel.Info, line);
            }
        }

        Write(LogLevel.Info, $"--- {title} 结束 ---");
    }

    public void Write(LogLevel level, string message)
    {
        if (level < _minimum)
        {
            return;
        }

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Abbreviation(level)}] {message}";

        if (_echoToDebug)
        {
            System.Diagnostics.Debug.WriteLine(line);
        }

        lock (_gate)
        {
            try
            {
                EnsureFile();
                File.AppendAllText(_currentPath!, line + Environment.NewLine, new UTF8Encoding(false));
                _currentBytes += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
            }
            catch (Exception)
            {
                // 日志写不进去不该让程序出问题
            }
        }
    }

    /// <summary>
    /// 批量写多行（一次文件操作）。
    ///
    /// 为什么需要它：命令输出可能有几万行，逐行 <c>AppendAllText</c> 等于每个行都开关一次文件，
    /// 光是 IO 就能把界面拖慢。宿主把输出攒起来按批调用这个方法。
    /// </summary>
    public void WriteLines(LogLevel level, IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (level < _minimum)
        {
            return;
        }

        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var builder = new StringBuilder();
        var count = 0;

        foreach (var line in lines)
        {
            builder.Append(stamp).Append(" [").Append(Abbreviation(level)).Append("] ").Append(line).Append('\n');
            count++;
        }

        if (count == 0)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                EnsureFile();
                File.AppendAllText(_currentPath!, builder.ToString(), new UTF8Encoding(false));
                _currentBytes += Encoding.UTF8.GetByteCount(builder.ToString());
            }
            catch (Exception)
            {
                // 日志写不进去不该让程序出问题
            }
        }
    }

    private static string Abbreviation(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        _ => "ERR",
    };

    private void EnsureFile()
    {
        System.IO.Directory.CreateDirectory(_directory);

        var path = Path.Combine(_directory, $"{_baseName}.log");

        if (_currentPath is null || _currentBytes >= MaxBytesPerFile)
        {
            if (File.Exists(path))
            {
                Rotate(path);
            }

            _currentPath = path;
            _currentBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        }
    }

    /// <summary>把 <c>&lt;名字&gt;.log</c> → <c>&lt;名字&gt;.1.log</c>，依次后移，超出的删掉。</summary>
    private void Rotate(string path)
    {
        var oldest = Path.Combine(_directory, $"{_baseName}.{MaxFiles - 1}.log");

        try
        {
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }
        }
        catch (Exception)
        {
            // 删不掉就让它继续占着；下面的改名会失败并被吞掉
        }

        for (var index = MaxFiles - 2; index >= 1; index--)
        {
            var from = Path.Combine(_directory, $"{_baseName}.{index}.log");

            if (!File.Exists(from))
            {
                continue;
            }

            var to = Path.Combine(_directory, $"{_baseName}.{index + 1}.log");

            try
            {
                File.Move(from, to, overwrite: true);
            }
            catch (Exception)
            {
                // 同上
            }
        }

        try
        {
            File.Move(path, Path.Combine(_directory, $"{_baseName}.1.log"), overwrite: true);
        }
        catch (Exception)
        {
            // 同上
        }
    }

    public void Dispose()
    {
        // 同步写、立即 flush，没有需要收尾的缓冲。
        // 保留 Dispose 是为了让调用方能用 using 表达生命周期，将来换成异步写入时不用改调用点。
    }
}
