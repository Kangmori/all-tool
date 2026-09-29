using AllTool.Core.Diagnostics;

namespace AllTool.Core.Tests;

/// <summary>
/// 日志组件的测试。
///
/// 这套测试守的是"出问题时能不能查到现场"：日志必须真的落盘、必须能轮转、
/// **而且写日志失败绝不能把程序搞崩**（日志是辅助手段，不是关键路径）。
/// </summary>
public class FileLoggerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "alltool-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响结论
        }
    }

    [Fact]
    public void 写入后能立刻在磁盘上读到()
    {
        var logger = new FileLogger(_root);

        logger.Info("程序启动");
        logger.Warn("工具包载入有问题");
        logger.Error("执行失败");

        var text = File.ReadAllText(logger.CurrentPath!);

        Assert.Contains("程序启动", text);
        Assert.Contains("[INF]", text);
        Assert.Contains("[WRN]", text);
        Assert.Contains("[ERR]", text);
    }

    [Fact]
    public void Debug_级别默认不写()
    {
        var logger = new FileLogger(_root);

        logger.Debug("这是细节");
        logger.Info("这是要点");

        var text = File.ReadAllText(logger.CurrentPath!);

        Assert.DoesNotContain("这是细节", text);
        Assert.Contains("这是要点", text);
    }

    [Fact]
    public void 记录异常时会带上类型与堆栈()
    {
        var logger = new FileLogger(_root);

        try
        {
            throw new InvalidOperationException("模拟故障");
        }
        catch (Exception ex)
        {
            logger.Exception("执行命令时", ex);
        }

        var text = File.ReadAllText(logger.CurrentPath!);

        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("模拟故障", text);
        Assert.Contains("FileLoggerTests", text);   // 堆栈里有调用点
    }

    [Fact]
    public void 目录不存在时会自动创建()
    {
        var nested = Path.Combine(_root, "a", "b", "logs");
        var logger = new FileLogger(nested);

        logger.Info("hello");

        Assert.True(File.Exists(logger.CurrentPath));
        Assert.StartsWith(nested, logger.CurrentPath);
    }

    [Fact]
    public void 超过大小上限会轮转并保留历史()
    {
        var logger = new FileLogger(_root);

        // 写超过一个文件上限的内容（用小上限不方便，所以直接写够 8 MB 不现实——
        // 改为验证轮转逻辑本身：先写一个大块触发轮转）
        var chunk = new string('x', 64 * 1024);
        var writes = (int)(FileLogger.MaxBytesPerFile / chunk.Length) + 2;

        for (var i = 0; i < writes; i++)
        {
            logger.Info($"第 {i} 块 {chunk}");
        }

        var files = Directory.GetFiles(_root, "*.log").Select(Path.GetFileName).ToList();

        Assert.Contains("alltool.log", files);
        Assert.Contains("alltool.1.log", files);
    }

    [Fact]
    public void 写不进去时不抛异常()
    {
        // 用一个非法目录让写入必然失败（Windows 上 : 之后的字符非法）
        var logger = new FileLogger(Path.Combine(_root, "非法:目录"));

        var exception = Record.Exception(() =>
        {
            logger.Info("这条写不进去");
            logger.Error("这条也写不进去");
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Block_会把多行内容逐行记下来()
    {
        var logger = new FileLogger(_root);

        logger.Block("命令输出", "第一行\r\n第二行\n第三行");

        var text = File.ReadAllText(logger.CurrentPath!);

        Assert.Contains("--- 命令输出 ---", text);
        Assert.Contains("第二行", text);
        Assert.Contains("第三行", text);
    }
}
