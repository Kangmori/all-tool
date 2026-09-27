using Swpj.Core.Discovery;
using Swpj.Core.Execution;
using Swpj.Core.Manifest;

namespace Swpj.Core.Tests;

/// <summary>
/// 端到端：用仓库里真实的 7zip 清单 + 本机真实安装的 7-Zip，
/// 走完「定位 → 生成 argv → 执行 → 解析输出与退出码」这条链路。
///
/// 这正是界面点「执行」时走的路径，只是把 WinUI 控件换成了直接调用。
/// 若本机没装 7-Zip，用例会直接返回（视为通过），以便在没有该软件的环境里也能跑测试。
/// </summary>
public class EndToEndTests
{
    /// <summary>
    /// 按清单的声明构造执行请求，而不是自己另定一套参数——
    /// 否则端到端测试验证的就不是"清单描述的行为"了（例如清单开了伪控制台，这里必须跟着开）。
    /// </summary>
    private static ProcessRunRequest Request(
        string executable,
        IReadOnlyList<string> arguments,
        ToolManifest manifest,
        string? workingDirectory = null) => new()
    {
        Executable = executable,
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        OutputEncoding = EncodingResolver.Resolve(manifest.Runtime?.Encoding),
        UsePseudoConsole = manifest.Runtime?.UsePseudoConsole ?? false,
    };

    [Fact]
    public async Task 用真实_7zip_跑通压缩_校验_解压全流程()
    {
        var manifest = ManifestLoader.LoadFromFile(
            Path.Combine(TestRepo.Root, "plugins", "7zip", "manifest.yaml"));

        var runner = new ProcessRunner();
        var location = await ToolLocator.LocateAsync(manifest.Locate!, runner);

        if (location is null)
        {
            // 本机没装 7-Zip：不把环境依赖变成测试失败。
            return;
        }

        Assert.True(location.MeetsMinimumVersion, location.Problem);
        Assert.NotNull(location.Version);

        var work = Path.Combine(Path.GetTempPath(), "swpj-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(work, "sub"));

        try
        {
            File.WriteAllText(Path.Combine(work, "hello.txt"), "hello swpj");
            File.WriteAllText(Path.Combine(work, "sub", "nested.txt"), "nested");
            var archive = Path.Combine(work, "test.7z");
            var encoding = EncodingResolver.Resolve(manifest.Runtime?.Encoding);

            // ---------------- 1) 添加：7z a ----------------
            var add = manifest.Actions!.Single(a => a.Id == "add");
            var addArgv = ArgvBuilder.Build(add, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["archive"] = archive,
                ["inputs"] = new List<string> { Path.Combine(work, "hello.txt"), Path.Combine(work, "sub") },
                ["type"] = "7z",
                ["level"] = "5",
                ["encryptNames"] = "off",
                ["recurse"] = "off",
                ["logLevel"] = "0",
            });

            var addRun = await runner.RunAsync(Request(location.ExecutablePath, addArgv, manifest));

            Assert.Equal(0, addRun.ExitCode);
            Assert.True(File.Exists(archive), "压缩包没有生成");
            Assert.True(ExitCodeInterpreter.Interpret(addRun.ExitCode, manifest.ExitCodes).IsSuccess);

            // ---------------- 2) 测试完整性：7z t ----------------
            var test = manifest.Actions!.Single(a => a.Id == "test");
            var testRun = await runner.RunAsync(Request(location.ExecutablePath, ArgvBuilder.Build(test, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["archive"] = archive,
                ["logLevel"] = "1",
            }), manifest));

            Assert.Equal(0, testRun.ExitCode);

            // ---------------- 3) 解压：7z x ----------------
            var extract = manifest.Actions!.Single(a => a.Id == "extract");
            var outputDirectory = Path.Combine(work, "out");
            var progress = new SyncProgress<ProcessOutputLine>();

            var extractRun = await runner.RunAsync(
                Request(
                    location.ExecutablePath,
                    ArgvBuilder.Build(extract, new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["archive"] = archive,
                        ["outputDir"] = outputDirectory,
                        ["overwrite"] = "a",
                        ["recurse"] = "off",
                        ["logLevel"] = "0",
                    }),
                    manifest,
                    outputDirectory),
                progress);

            Assert.Equal(0, extractRun.ExitCode);

            // x 保留目录结构——这是它与 e 的关键差别，必须真的在文件系统上成立
            Assert.True(File.Exists(Path.Combine(outputDirectory, "hello.txt")), "hello.txt 没有被解压出来");
            Assert.True(File.Exists(Path.Combine(outputDirectory, "sub", "nested.txt")), "子目录结构没有保留");

            // 输出确实被捕获到了（不是空数组）
            Assert.NotEmpty(progress.Items);

            // 已知事实：进度百分比**不会**出现在这个小压缩包上（7z 对秒级任务来不及画进度）。
            // 这个限制本身不再断言成"永远解析不到"——因为清单现在开了伪控制台，
            // 真正验证「7z 在伪控制台下会报百分比」的用例是
            // ConPtyTests.伪控制台下_7z_能报出百分比进度（那里刻意用 32 MB 输入）。
            // 这里只确认输出确实被捕获到了，且退出去解读为成功。
            Assert.NotEmpty(progress.Items);
            Assert.True(ExitCodeInterpreter.Interpret(extractRun.ExitCode, manifest.ExitCodes).IsSuccess);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清理失败不该让测试失败。
            }
        }
    }

    [Fact]
    public async Task 清单里的_minVersion_与真实版本一致()
    {
        var manifest = ManifestLoader.LoadFromFile(
            Path.Combine(TestRepo.Root, "plugins", "7zip", "manifest.yaml"));

        var location = await ToolLocator.LocateAsync(manifest.Locate!, new ProcessRunner());
        if (location is null)
        {
            return;
        }

        // 本机装的是 26.03，清单 minVersion 是 23.00
        Assert.True(location.MeetsMinimumVersion);
        Assert.True(VersionComparison.IsAtLeast(location.Version, manifest.Locate!.MinVersion));
    }
}
