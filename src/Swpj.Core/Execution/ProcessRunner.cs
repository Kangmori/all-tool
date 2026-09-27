using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Swpj.Core.Execution;

/// <summary>一次执行请求。</summary>
public sealed record ProcessRunRequest
{
    /// <summary>可执行文件的完整路径。</summary>
    public required string Executable { get; init; }

    /// <summary>参数列表。**以数组传递，绝不拼成字符串**——这是防注入与防转义错误的根本。</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public Encoding OutputEncoding { get; init; } = Encoding.UTF8;

    /// <summary>为 null 表示不限时。</summary>
    public TimeSpan? Timeout { get; init; }

    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>最多保留多少行输出（防止超长输出把内存吃掉）。</summary>
    public int MaxCapturedLines { get; init; } = 5000;

    /// <summary>
    /// 是否给子进程分配一个伪控制台（ConPTY）。
    ///
    /// 默认 false。需要它的场景很具体：**只在真控制台里画进度的程序**（7z 就是），
    /// 一旦被重定向就什么进度都不输出。代价是输出里会出现 ANSI 转义序列（用 AnsiText.Strip 清理），
    /// 且子进程会认为自己在一台真终端里（有的程序会因此改变行为，例如强制彩色输出）。
    /// 所以这是**按工具包选择**的开关，不是默认行为。
    /// </summary>
    public bool UsePseudoConsole { get; init; }
}

/// <summary>输出的一行，标明来源流。</summary>
public sealed record ProcessOutputLine(OutputStream Stream, string Text);

public enum OutputStream
{
    StandardOutput,
    StandardError,
}

/// <summary>一次执行的结果。</summary>
public sealed record ProcessRunResult
{
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public bool Canceled { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<ProcessOutputLine> Lines { get; init; } = [];
    public int TotalLineCount { get; init; }

    /// <summary>输出是否因超过上限而被截断。</summary>
    public bool Truncated => TotalLineCount > Lines.Count;
}

/// <summary>执行引擎的统一入口。有了接口，将来换成 ConPTY 或别的实现时调用方不用改。</summary>
public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 进程执行引擎（默认走管道；需要在真控制台里跑的程序走 ConPTY）。
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly ConPtyProcessRunner _conPtyRunner = new();

    public Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 两条路径的能力不同，所以由工具包（而非宿主）决定用哪条：
        //  - 管道：输出干净、可直接当数据解析；但只在真控制台里画进度的程序不会有进度
        //  - ConPTY：有进度、有颜色；但输出里混着转义序列，且程序会察觉到"有终端"
        return request.UsePseudoConsole
            ? _conPtyRunner.RunAsync(request, progress, cancellationToken)
            : RunWithPipesAsync(request, progress, cancellationToken);
    }

    private static async Task<ProcessRunResult> RunWithPipesAsync(
        ProcessRunRequest request,
        IProgress<ProcessOutputLine>? progress,
        CancellationToken cancellationToken)
    {

        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = request.OutputEncoding,
            StandardErrorEncoding = request.OutputEncoding,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(request.WorkingDirectory))
        {
            // 刻意自动创建：像「解压到还不存在的目录」是很常见的合理意图
            // （7z 自己也会用 -o 建目录），而 CreateProcess 在目录不存在时会直接报
            // "目录名称无效" 而启动失败。若目录名打错，用户会在输出与文件系统里立刻看到。
            if (!Directory.Exists(request.WorkingDirectory))
            {
                Directory.CreateDirectory(request.WorkingDirectory);
            }

            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        if (request.Environment is not null)
        {
            foreach (var (key, value) in request.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        var lines = new List<ProcessOutputLine>();
        var totalLines = 0;
        var gate = new object();

        void OnLine(ProcessOutputLine line)
        {
            lock (gate)
            {
                totalLines++;

                // 超出上限后仍然计数（好让界面知道"被截断了"），但不再保留内容。
                if (lines.Count < request.MaxCapturedLines)
                {
                    lines.Add(line);
                }
            }

            progress?.Report(line);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stopwatch = Stopwatch.StartNew();

        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动进程：{request.Executable}");
        }

        using var timeoutSource = request.Timeout is { } timeout
            ? new CancellationTokenSource(timeout)
            : new CancellationTokenSource();

        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutSource.Token);

        var stdoutTask = PumpAsync(process.StandardOutput, OutputStream.StandardOutput, OnLine, CancellationToken.None);
        var stderrTask = PumpAsync(process.StandardError, OutputStream.StandardError, OnLine, CancellationToken.None);

        var canceled = false;
        var timedOut = false;

        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            canceled = cancellationToken.IsCancellationRequested;
            timedOut = timeoutSource.IsCancellationRequested;

            TryKillTree(process);

            // 等它真的死掉，否则读流的任务可能读不到 EOF。
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        // 输出流读干净（子进程退出后管道会关闭，这两个任务随后自然结束）。
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        stopwatch.Stop();

        return new ProcessRunResult
        {
            ExitCode = process.HasExited ? process.ExitCode : -1,
            Canceled = canceled,
            TimedOut = timedOut,
            Duration = stopwatch.Elapsed,
            Lines = lines,
            TotalLineCount = totalLines,
        };
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // 进程可能刚好自己退出了；这里不掩盖调用方真正关心的取消/超时语义。
        }
    }

    /// <summary>
    /// 按 <c>\r</c> 与 <c>\n</c> 切行地读取一个输出流。
    /// 把 <c>\r</c> 也当行结束符是刻意的：7z 之类的进度就是用 <c>\r</c> 原地刷新的。
    /// </summary>
    private static async Task PumpAsync(
        StreamReader reader,
        OutputStream stream,
        Action<ProcessOutputLine> onLine,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var pending = new StringBuilder();

        while (true)
        {
            int read;
            try
            {
                read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                var ch = buffer[i];

                if (ch is '\r' or '\n')
                {
                    if (pending.Length > 0)
                    {
                        onLine(new ProcessOutputLine(stream, pending.ToString()));
                        pending.Clear();
                    }

                    continue;
                }

                pending.Append(ch);
            }
        }

        if (pending.Length > 0)
        {
            onLine(new ProcessOutputLine(stream, pending.ToString()));
        }
    }
}

/// <summary>把清单里的 encoding 字符串解析成 .NET 的 Encoding。</summary>
public static class EncodingResolver
{
    private static bool _providerRegistered;

    /// <summary>解析编码名。<paramref name="name"/> 为 null 或 auto 时按 <paramref name="fallback"/> 处理。</summary>
    public static Encoding Resolve(string? name, Encoding? fallback = null)
    {
        EnsureCodePagesRegistered();

        return (name ?? "auto").ToLowerInvariant() switch
        {
            "utf-8" or "utf8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            "utf-16le" or "utf16le" or "unicode" => Encoding.Unicode,
            "gbk" => Encoding.GetEncoding(936),
            "oem" => Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            _ => fallback ?? DefaultForConsoleApps(),
        };
    }

    /// <summary>
    /// auto 的默认选择：**控制台程序的输出码页**。
    ///
    /// 理由：Windows 上的传统命令行程序在被重定向时，通常仍按控制台输出码页（中文系统上是 936）写字节，
    /// 而不是 UTF-8。把它作为 auto 的默认值，比默认 UTF-8 更不容易出现乱码。
    /// 现代工具（uv、node 等）多写 UTF-8，这类工具包应显式写 encoding: utf-8。
    /// 这是一处**已知的取舍**，不是完美方案——见 docs/ai/development.md。
    /// </summary>
    public static Encoding DefaultForConsoleApps()
    {
        EnsureCodePagesRegistered();
        return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    }

    private static void EnsureCodePagesRegistered()
    {
        if (_providerRegistered)
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _providerRegistered = true;
    }
}
