using System.Diagnostics;
using Swpj.Core.Execution;

namespace Swpj.Core.Tests;

/// <summary>
/// 进程执行引擎的测试。会真的启动子进程（cmd / powershell），因此每个用例都自带超时上限。
/// </summary>
public class ProcessRunnerTests
{
    private static ProcessRunRequest Request(params string[] arguments) => new()
    {
        Executable = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        Arguments = arguments,
        OutputEncoding = EncodingResolver.DefaultForConsoleApps(),
    };

    private static string PowerShellPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        @"WindowsPowerShell\v1.0\powershell.exe");

    [Fact]
    public async Task 能捕获标准输出与退出码()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(Request("/c", "echo hello & exit /b 3"));

        Assert.False(result.Canceled);
        Assert.False(result.TimedOut);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Lines, l =>
            l.Stream == OutputStream.StandardOutput && l.Text.Contains("hello", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 退出码为_0_时正常返回()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(Request("/c", "ver"));

        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(result.Lines);
    }

    [Fact]
    public async Task 换行与回车都当作行结束符_这样才抓得住原地刷新的进度()
    {
        var runner = new ProcessRunner();

        // 用 Write 而不是 WriteLine，并只用 \r 分隔，模拟 7z 的进度刷新方式。
        var result = await runner.RunAsync(new ProcessRunRequest
        {
            Executable = PowerShellPath,
            Arguments =
            [
                "-NoProfile", "-NonInteractive", "-Command",
                "[Console]::Out.Write('10%' + [char]13 + '20%' + [char]13 + '100%')",
            ],
            OutputEncoding = EncodingResolver.DefaultForConsoleApps(),
        });

        var texts = result.Lines.Select(l => l.Text).ToList();

        Assert.Contains("10%", texts);
        Assert.Contains("20%", texts);
        Assert.Contains("100%", texts);

        // 顺带验证进度解析能接上：
        var parser = new ProgressParser(new Manifest.ProgressSpec { Pattern = "(\\d+)%", Unit = "percent", Group = 1 });
        Assert.True(parser.TryParse(texts.Last(), out var value));
        Assert.Equal(100, value);
    }

    [Fact]
    public async Task 取消时要杀掉子进程并迅速返回()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource();

        var run = runner.RunAsync(
            new ProcessRunRequest
            {
                Executable = PowerShellPath,
                Arguments = ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],
                OutputEncoding = EncodingResolver.DefaultForConsoleApps(),
            },
            progress: null,
            cts.Token);

        // 等它真正起来再取消，避免"还没启动就被取消"的竞态导致测不到杀进程逻辑。
        await Task.Delay(400);
        cts.Cancel();

        var result = await run;

        Assert.True(result.Canceled);
        Assert.False(result.TimedOut);
        Assert.True(result.Duration < TimeSpan.FromSeconds(15), $"取消后应迅速返回，实际 {result.Duration}");
    }

    [Fact]
    public async Task 超时会被标记并终止子进程()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(new ProcessRunRequest
        {
            Executable = PowerShellPath,
            Arguments = ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],
            OutputEncoding = EncodingResolver.DefaultForConsoleApps(),
            Timeout = TimeSpan.FromSeconds(1),
        });

        Assert.True(result.TimedOut);
        Assert.False(result.Canceled);
        Assert.True(result.Duration < TimeSpan.FromSeconds(15), $"超时后应迅速返回，实际 {result.Duration}");
    }

    [Fact]
    public async Task 输出行数超过上限时截断内容但仍如实计数()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(new ProcessRunRequest
        {
            Executable = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = ["/c", "(for /l %i in (1,1,50) do @echo line%i)"],
            OutputEncoding = EncodingResolver.DefaultForConsoleApps(),
            MaxCapturedLines = 10,
        });

        Assert.Equal(50, result.TotalLineCount);
        Assert.Equal(10, result.Lines.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task 工作目录会被传给子进程()
    {
        var runner = new ProcessRunner();
        var temp = Path.GetTempPath().TrimEnd('\\');

        var result = await runner.RunAsync(new ProcessRunRequest
        {
            Executable = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = ["/c", "cd"],
            OutputEncoding = EncodingResolver.DefaultForConsoleApps(),
            WorkingDirectory = temp,
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(temp, string.Join('\n', result.Lines.Select(l => l.Text)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 启动不存在的程序会抛异常而不是静默失败()
    {
        var runner = new ProcessRunner();

        await Assert.ThrowsAnyAsync<Exception>(() => runner.RunAsync(new ProcessRunRequest
        {
            Executable = @"C:\swpj-does-not-exist\nothing.exe",
            Arguments = [],
        }));
    }

    [Fact]
    public async Task 进度回调能实时收到输出行()
    {
        var runner = new ProcessRunner();
        var received = new List<string>();
        var progress = new Progress<ProcessOutputLine>(line => received.Add(line.Text));

        var result = await runner.RunAsync(Request("/c", "echo one & echo two"), progress);

        Assert.Equal(0, result.ExitCode);

        // Progress<T> 是投递到同步上下文的，给它一点时间落地。
        for (var i = 0; i < 20 && received.Count < 2; i++)
        {
            await Task.Delay(50);
        }

        // 注意用 Trim：cmd 的 `echo one & echo two` 会把 `&` 前的空格也算进输出（"one "）。
        Assert.Contains(received, text => text.Trim() == "one");
        Assert.Contains(received, text => text.Trim() == "two");
    }
}
