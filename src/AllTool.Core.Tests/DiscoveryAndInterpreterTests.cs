using System.Text;
using AllTool.Core.Discovery;
using AllTool.Core.Execution;
using AllTool.Core.Manifest;

namespace AllTool.Core.Tests;

[Collection(RealProcessCollection.Name)]
public class VersionComparisonTests
{
    [Theory]
    [InlineData("26.03", "23.00", true)]
    [InlineData("23.00", "23.00", true)]
    [InlineData("22.01", "23.00", false)]
    [InlineData("1.9", "1.10", false)]      // 字符串比较会在这里出错，所以必须逐段比数值
    [InlineData("1.10", "1.9", true)]
    [InlineData("10.0.401", "10.0", true)]
    [InlineData("2.55.0", "2.55", true)]
    public void 逐段数值比较(string actual, string minimum, bool expected)
    {
        Assert.Equal(expected, VersionComparison.IsAtLeast(actual, minimum));
    }

    [Fact]
    public void 版本号里混有非数字段时只取数字段()
    {
        Assert.True(VersionComparison.IsAtLeast("2.55.0.windows.5", "2.55.0"));
        Assert.False(VersionComparison.IsAtLeast("2.55.0.windows.5", "2.56"));
    }
}

[Collection(RealProcessCollection.Name)]
public class ProgressParserTests
{
    [Theory]
    [InlineData(" 45%", 45)]
    [InlineData(" 45% 12 - somefile.txt", 45)]
    [InlineData("100%", 100)]
    public void 能从行里解析出百分比(string line, double expected)
    {
        var parser = new ProgressParser(new ProgressSpec { Pattern = "(\\d+)%", Unit = "percent", Group = 1 });

        Assert.True(parser.TryParse(line, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no percent here")]
    [InlineData(null)]
    public void 不匹配时不报进度(string? line)
    {
        var parser = new ProgressParser(new ProgressSpec { Pattern = "(\\d+)%", Unit = "percent", Group = 1 });

        Assert.False(parser.TryParse(line, out _));
    }

    [Fact]
    public void 没有进度规则时永远不报进度()
    {
        var parser = new ProgressParser(null);

        Assert.False(parser.TryParse("50%", out _));
    }
}

[Collection(RealProcessCollection.Name)]
public class ExitCodeInterpreterTests
{
    private static readonly List<ExitCodeSpec> SevenZip =
    [
        new() { Code = 0, Meaning = "成功，无错误", Severity = "ok" },
        new() { Code = 1, Meaning = "警告（非致命错误）", Severity = "warning" },
        new() { Code = 2, Meaning = "致命错误", Severity = "error" },
        new() { Code = 7, Meaning = "命令行参数错误", Severity = "error" },
    ];

    [Theory]
    [InlineData(0, "ok")]
    [InlineData(1, "warning")]
    [InlineData(2, "error")]
    [InlineData(7, "error")]
    public void 用清单里的表翻译退出码(int code, string severity)
    {
        var verdict = ExitCodeInterpreter.Interpret(code, SevenZip);

        Assert.Equal(severity, verdict.Severity);
        Assert.Equal(code, verdict.Code);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Meaning));
    }

    [Fact]
    public void 表里没有的码按通用规则判定并如实说明()
    {
        var zero = ExitCodeInterpreter.Interpret(0, SevenZip);
        Assert.True(zero.IsSuccess);

        var unknown = ExitCodeInterpreter.Interpret(99, SevenZip);
        Assert.Equal("error", unknown.Severity);
        Assert.Contains("exitCodes", unknown.Meaning, StringComparison.Ordinal);
    }

    [Fact]
    public void 取消与超时给出各自的结论()
    {
        Assert.Contains("取消", ExitCodeInterpreter.FromInterruption(canceled: true, timedOut: false).Meaning, StringComparison.Ordinal);
        Assert.Contains("超时", ExitCodeInterpreter.FromInterruption(canceled: false, timedOut: true).Meaning, StringComparison.Ordinal);
    }
}

[Collection(RealProcessCollection.Name)]
public class EncodingResolverTests
{
    [Fact]
    public void gbk_解析为代码页_936()
    {
        Assert.Equal(936, EncodingResolver.Resolve("gbk").CodePage);
    }

    [Fact]
    public void utf8_不带_BOM()
    {
        var encoding = EncodingResolver.Resolve("utf-8");

        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
        Assert.Empty(encoding.GetPreamble());
    }

    [Fact]
    public void oem_解析为当前区域的控制台输出码页()
    {
        var expected = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;

        Assert.Equal(expected, EncodingResolver.Resolve("oem").CodePage);
    }

    [Fact]
    public void auto_走控制台码页而不是_utf8()
    {
        // 这是刻意的取舍：Windows 上的传统命令行程序在被重定向时通常仍按控制台码页写字节。
        Assert.Equal(EncodingResolver.DefaultForConsoleApps().CodePage, EncodingResolver.Resolve("auto").CodePage);
    }
}

[Collection(RealProcessCollection.Name)]
public class ToolLocatorTests
{
    [Fact]
    public void 能在_PATH_里找到_cmd_exe()
    {
        var locate = new LocateSpec { Executable = "cmd" };

        var path = ToolLocator.FindExecutable(locate);

        Assert.NotNull(path);
        Assert.EndsWith("cmd.exe", path!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 找不到时返回_null()
    {
        var locate = new LocateSpec { Executable = "alltool-definitely-not-installed-xyz" };

        Assert.Null(ToolLocator.FindExecutable(locate));
    }

    [Fact]
    public void 不返回_ps1_脚本因为无法被_CreateProcess_直接启动()
    {
        var locate = new LocateSpec { Executable = "scoop" };

        var path = ToolLocator.FindExecutable(locate);

        Assert.True(path is null || !path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase),
            $"不应返回 .ps1：{path}");
    }

    [Fact]
    public void searchPaths_支持环境变量展开()
    {
        var locate = new LocateSpec
        {
            Executable = "cmd",
            SearchPaths = ["%SystemRoot%\\System32"],
        };

        Assert.NotNull(ToolLocator.FindExecutable(locate));
    }

    [Fact]
    public void 版本正则可从多行输出里取值_且容忍首行空行()
    {
        const string output = "\n7-Zip 26.03 (x64) : Copyright\nLibs:\n";

        var version = ToolLocator.ExtractVersion(output, "(?m)^7-Zip\\s+([0-9.]+)");

        Assert.Equal("26.03", version);
    }

    [Fact]
    public void 没给正则时退化为取第一段像版本号的数字()
    {
        Assert.Equal("1.2.3", ToolLocator.ExtractVersion("my tool v1.2.3 build 9", null));
    }
}
