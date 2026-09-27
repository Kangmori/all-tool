using System.Text;
using Swpj.Core.Execution;

// ConPTY 诊断程序（**无控制台的 GUI 子系统**）。
//
// 为什么必须是 WinExe：实验已经证明"子进程的标准句柄是从父进程继承的，ConPTY 不会覆盖它们"。
//   - 父进程是控制台程序 → 子进程写到父进程的控制台（绕过 ConPTY）
//   - 父进程是测试宿主、stdout 被重定向 → 子进程写到那个看不见的管道
//   - 父进程是无控制台的 GUI 程序（就是本程序，也是真实的 WinUI 宿主形态）→ 没有可继承的句柄
// 所以只有在 WinExe 下测，才能得到与真实宿主一致的结论。
//
// 用法：conpty-probe.exe <报告文件路径>
var reportPath = args.Length > 0
    ? args[0]
    : Path.Combine(Path.GetTempPath(), "swpj-conpty-probe.txt");

var log = new StringBuilder();
log.AppendLine($"父进程 pid={Environment.ProcessId}，是否有控制台={HasConsole()}");

var runner = new ProcessRunner();
var result = await runner.RunAsync(new ProcessRunRequest
{
    Executable = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
    Arguments = ["/c", "echo from-conpty & echo second-line"],
    OutputEncoding = EncodingResolver.DefaultForConsoleApps(),
    UsePseudoConsole = true,
});

log.AppendLine($"退出码={result.ExitCode} 捕获行数={result.Lines.Count} 耗时={result.Duration.TotalMilliseconds:F0}ms");
log.AppendLine("--- 捕获到的内容（已去掉转义序列） ---");
foreach (var line in result.Lines)
{
    log.AppendLine($"[{AnsiText.Strip(line.Text)}]");
}

File.WriteAllText(reportPath, log.ToString(), new UTF8Encoding(false));

static string HasConsole()
{
    try
    {
        return (Console.Title.Length >= 0).ToString();
    }
    catch (Exception ex)
    {
        return $"否（{ex.GetType().Name}）";
    }
}
