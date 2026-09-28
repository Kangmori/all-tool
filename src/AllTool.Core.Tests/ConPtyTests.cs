using AllTool.Core.Discovery;
using AllTool.Core.Execution;
using AllTool.Core.Manifest;

namespace AllTool.Core.Tests;

/// <summary>
/// 伪控制台（ConPTY）执行路径的测试。
///
/// 核心那条用例是全项目最"贵"的测试：它要真的造一个 32 MB 的输入、真跑 7z、
/// 并断言进度百分比确实出现了。因为"7z 被重定向时不报进度"正是引入 ConPTY 的唯一理由，
/// 这条断言就是那个理由的证明。
/// </summary>
[Collection(RealProcessCollection.Name)]
public class ConPtyTests
{
    private static string CmdExe => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task 伪控制台下能捕获输出与退出码()
    {
        var runner = new ProcessRunner();

        // 中间插一段 ping 当"等一下"：用来区分两种失败原因——
        //   (a) 管道根本没通（那么加了等待也没有输出）
        //   (b) 子进程退出太快、伪控制台还没渲染完就把会话拆了（那么加了等待就有输出）
        var result = await runner.RunAsync(new ProcessRunRequest
        {
            Executable = CmdExe,
            Arguments = ["/c", "echo conpty-hello & ping -n 3 127.0.0.1 > nul & exit /b 5"],
            OutputEncoding = EncodingResolver.DefaultForConsoleApps(),
            UsePseudoConsole = true,
        });

        var dump = string.Join(" | ", result.Lines.Select(l => AnsiText.Strip(l.Text)));

        Assert.Equal(5, result.ExitCode);
        Assert.False(result.Canceled);
        Assert.True(
            result.Lines.Any(line => AnsiText.Strip(line.Text).Contains("conpty-hello", StringComparison.Ordinal)),
            $"没捕获到 echo 的输出。退出码={result.ExitCode}，共 {result.Lines.Count} 行：{dump}");
    }

    [Fact]
    public async Task 伪控制台下取消会连孙进程一起杀掉()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource();
        var marker = Path.Combine(Path.GetTempPath(), "alltool-tree-" + Guid.NewGuid().ToString("N") + ".txt");

        // cmd 起 powershell；powershell 睡 3 秒后写标记文件，再继续睡。
        // 取消发生在第 1.5 秒左右：如果只杀了 cmd 而放走了 powershell，
        // 那个标记文件就会在第 3 秒出现，测试随即失败。
        // 这正是 Job Object（KILL_ON_JOB_CLOSE）要覆盖的场景。
        var script = $"Start-Sleep -Seconds 3; Set-Content -Path '{marker}' -Value alive; Start-Sleep -Seconds 30";

        var run = runner.RunAsync(
            new ProcessRunRequest
            {
                Executable = CmdExe,
                Arguments = ["/c", "powershell", "-NoProfile", "-NonInteractive", "-Command", script],
                OutputEncoding = EncodingResolver.DefaultForConsoleApps(),
                UsePseudoConsole = true,
            },
            progress: null,
            cts.Token);

        await Task.Delay(1500);
        cts.Cancel();

        var result = await run;
        Assert.True(result.Canceled);

        // 等到"孙进程本该写标记文件"的时刻之后，确认它确实没活下来
        await Task.Delay(3000);
        Assert.False(File.Exists(marker), $"取消后孙进程仍然活着并写出了 {marker}");
    }

    [Fact]
    public async Task 伪控制台下超时也会杀进程()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(new ProcessRunRequest
        {
            Executable = CmdExe,
            Arguments = ["/c", "ping -n 30 127.0.0.1 > nul"],
            OutputEncoding = EncodingResolver.DefaultForConsoleApps(),
            UsePseudoConsole = true,
            Timeout = TimeSpan.FromSeconds(1),
        });

        Assert.True(result.TimedOut);
        Assert.False(result.Canceled);
        Assert.True(result.Duration < TimeSpan.FromSeconds(15), $"超时后应迅速返回，实际 {result.Duration}");
    }

    [Fact]
    public async Task 伪控制台下_7z_能报出百分比进度()
    {
        var manifest = ManifestLoader.LoadFromFile(
            Path.Combine(TestRepo.Root, "plugins", "7zip", "manifest.yaml"));

        var runner = new ProcessRunner();
        var location = await ToolLocator.LocateAsync(manifest.Locate!, runner);

        if (location is null)
        {
            return; // 本机没装 7-Zip
        }

        var work = Path.Combine(Path.GetTempPath(), "alltool-conpty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        try
        {
            // 数据要足够大，7z 才有时间画进度
            var payload = new byte[32 * 1024 * 1024];
            new Random(7).NextBytes(payload);
            File.WriteAllBytes(Path.Combine(work, "big.bin"), payload);

            var add = manifest.Actions!.Single(a => a.Id == "add");
            var progress = new SyncProgress<ProcessOutputLine>();

            var result = await runner.RunAsync(
                new ProcessRunRequest
                {
                    Executable = location.ExecutablePath,
                    Arguments = ArgvBuilder.Build(add, new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["archive"] = Path.Combine(work, "big.7z"),
                        ["inputs"] = new List<string> { Path.Combine(work, "big.bin") },
                        ["type"] = "7z",
                        ["level"] = "1",
                        ["encryptNames"] = "off",
                        ["recurse"] = "off",
                        ["logLevel"] = "0",
                    }),
                    OutputEncoding = EncodingResolver.Resolve(manifest.Runtime?.Encoding),
                    UsePseudoConsole = true,
                },
                progress);

            Assert.Equal(0, result.ExitCode);

            var parser = new ProgressParser(add.Output?.Progress);
            var percentLines = progress.Items
                .Select(item => item.Text)
                .Where(text => parser.TryParse(text, out _))
                .ToList();

            Assert.True(
                percentLines.Count > 0,
                "接了伪控制台之后 7z 应当报出百分比进度。这里失败说明 ConPTY 没真正生效" +
                "（这正是 N5 存在的全部理由）。捕获到的输出：" +
                string.Join(" | ", progress.Items.Take(15).Select(i => AnsiText.Strip(i.Text))));
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task 取消_不存在的程序会抛异常而不是静默失败()
    {
        var runner = new ProcessRunner();

        await Assert.ThrowsAnyAsync<Exception>(() => runner.RunAsync(new ProcessRunRequest
        {
            Executable = @"C:\All Tool-does-not-exist\nothing.exe",
            Arguments = [],
            UsePseudoConsole = true,
        }));
    }

    [Fact]
    public async Task 伪控制台下工作目录不存在时也会被创建()
    {
        var runner = new ProcessRunner();
        var target = Path.Combine(Path.GetTempPath(), "alltool-conpty-wd-" + Guid.NewGuid().ToString("N"));

        try
        {
            var result = await runner.RunAsync(new ProcessRunRequest
            {
                Executable = CmdExe,
                Arguments = ["/c", "cd"],
                OutputEncoding = EncodingResolver.DefaultForConsoleApps(),
                WorkingDirectory = target,
                UsePseudoConsole = true,
            });

            Assert.Equal(0, result.ExitCode);
            Assert.True(Directory.Exists(target));
        }
        finally
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
    }
}
