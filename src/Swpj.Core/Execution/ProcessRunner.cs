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

/// <summary>
/// 进程执行引擎。
///
/// 两个刻意的设计：
///   1. **不用 <c>BeginOutputReadLine</c>**。它按行事件推送，对 7z 这种用 <c>\r</c> 原地刷进度的程序
///      行为不可控。这里自己按 <c>\r</c> / <c>\n</c> 切行，进度才抓得住。
///   2. **取消时杀整棵进程树**（<c>Kill(entireProcessTree: true)</c>）。scoop 会调 aria2 再调 7z，
///      只杀直接子进程会留下孤儿。
///      已知局限：这是"杀的那一刻"递归杀，被杀之后又新建的子进程管不到。彻底解决要用 Job Object，
///      见 docs/ai/development.md 的组件说明。
/// </summary>
public sealed class ProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

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
