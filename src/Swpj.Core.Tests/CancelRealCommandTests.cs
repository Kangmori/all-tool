using Swpj.Core.Execution;
using Swpj.Core.Manifest;

namespace Swpj.Core.Tests;

/// <summary>
/// 复刻界面上的"取消"场景：用 scoop shim 的 7z 压一个 48 MB 的**不可压缩**文件，2 秒后取消。
///
/// 存在的理由：界面上点取消曾出现过"状态栏显示正在取消，但任务照样跑完"的现象，
/// 而当时的 ConPTY 取消单测（cmd + powershell）是通过的。差异只可能在"真实的被包装程序"
/// 与"调用方式"上，所以这里把界面那条链路原样搬进测试，好让它能快速复现与回归。
/// </summary>
[Collection(RealProcessCollection.Name)]
public class CancelRealCommandTests
{
    private static string? FindSevenZip()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims", "7z.exe"),
            @"C:\Program Files\7-Zip\7z.exe",
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    [Fact]
    public async Task 压缩中途取消应当真的杀掉_7z()
    {
        var sevenZip = FindSevenZip();

        if (sevenZip is null)
        {
            return;   // 本机没有 7z 就跳过（本项目其余真实清单测试也依赖本机软件）
        }

        var work = Path.Combine(Path.GetTempPath(), "swpj-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        try
        {
            // 不可压缩的数据，压缩要跑好几秒，才来得及取消
            var input = Path.Combine(work, "big.bin");
            var payload = new byte[48 * 1024 * 1024];
            new Random(42).NextBytes(payload);
            await File.WriteAllBytesAsync(input, payload);

            var archive = Path.Combine(work, "out.7z");

            var runner = new ProcessRunner();
            using var cts = new CancellationTokenSource();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var run = runner.RunAsync(
                new ProcessRunRequest
                {
                    Executable = sevenZip!,
                    Arguments = ["a", archive, input, "-t7z", "-mx5", "-r-", "-bb0"],
                    WorkingDirectory = work,
                    OutputEncoding = EncodingResolver.Resolve("gbk"),
                    UsePseudoConsole = true,
                },
                progress: null,
                cts.Token);

            await Task.Delay(2000);
            cts.Cancel();

            var result = await run;

            Assert.True(result.Canceled, $"取消请求已发出，但执行器报告 Canceled=false（退出码 {result.ExitCode}）");
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(15),
                $"取消后过了 {stopwatch.Elapsed.TotalSeconds:F1} 秒才返回，说明没被及时杀掉");
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
    }
}
