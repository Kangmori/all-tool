using Swpj.Core.Execution;

namespace Swpj.Core.Tests;

/// <summary>
/// Windows 命令行引号规则的单测。
///
/// 这是安全敏感的代码：拼错一个反斜杠，参数的含义就可能完全变了。
/// 用例覆盖了微软文档里点名的三种边界：含空格、含引号、结尾是反斜杠。
/// </summary>
public class WindowsCommandLineTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("-oD:\\out", "-oD:\\out")]           // 无反斜杠结尾、无空格：原样
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\tb", "\"a\tb\"")]
    [InlineData("", "\"\"")]
    public void 按需要加引号(string input, string expected)
    {
        Assert.Equal(expected, WindowsCommandLine.Quote(input));
    }

    [Fact]
    public void 参数内部的双引号被反斜杠转义()
    {
        // 输入：a"b   →  输出："a\"b"
        Assert.Equal("\"a\\\"b\"", WindowsCommandLine.Quote("a\"b"));
    }

    [Fact]
    public void 引号前的反斜杠要翻倍()
    {
        // 输入：a\"b  →  输出："a\\\"b"
        Assert.Equal("\"a\\\\\\\"b\"", WindowsCommandLine.Quote("a\\\"b"));
    }

    [Fact]
    public void 需要加引号时_结尾的反斜杠必须翻倍_否则会把收尾引号转义掉()
    {
        // 含空格 → 必须加引号 → 结尾反斜杠翻倍，否则解析回来会丢掉收尾引号
        Assert.Equal("\"C:\\my path\\\\\"", WindowsCommandLine.Quote("C:\\my path\\"));

        // 两个结尾反斜杠 → 翻成四个
        Assert.Equal("\"C:\\my path\\\\\\\\\"", WindowsCommandLine.Quote("C:\\my path\\\\"));
    }

    [Fact]
    public void 不需要加引号时_结尾反斜杠原样保留()
    {
        // 没有空格就不加引号，也就没有"收尾引号被转义"的问题，反斜杠不必翻倍
        Assert.Equal("C:\\path\\", WindowsCommandLine.Quote("C:\\path\\"));
        Assert.Equal("C:\\path\\\\", WindowsCommandLine.Quote("C:\\path\\\\"));
    }

    [Fact]
    public void 拼出的命令行第一个_token_是可执行文件()
    {
        var line = WindowsCommandLine.Build(
            @"C:\Program Files\7-Zip\7z.exe",
            ["x", @"D:\my docs\a.zip", @"-oD:\out"]);

        Assert.Equal("\"C:\\Program Files\\7-Zip\\7z.exe\" x \"D:\\my docs\\a.zip\" -oD:\\out", line);
    }

    [Fact]
    public void 没有参数时只有可执行文件()
    {
        Assert.Equal("7z", WindowsCommandLine.Build("7z", []));
    }

    [Fact]
    public void 含中文的参数原样保留()
    {
        var line = WindowsCommandLine.Build("7z", ["x", @"D:\我的 文档\包.7z"]);

        Assert.Equal("7z x \"D:\\我的 文档\\包.7z\"", line);
    }
}

public class AnsiTextTests
{
    [Fact]
    public void 去掉颜色与光标控制序列()
    {
        Assert.Equal("45%", AnsiText.Strip("\x1B[32m45%\x1B[0m"));
        Assert.Equal("hello", AnsiText.Strip("\x1B[1mhel\x1B[0mlo"));
    }

    [Fact]
    public void 去掉回车与退格这类光标残留()
    {
        Assert.Equal("abc", AnsiText.Strip("a\rb\bc"));
    }

    [Fact]
    public void 纯文本原样返回()
    {
        Assert.Equal("Everything is Ok", AnsiText.Strip("Everything is Ok"));
        Assert.Equal(string.Empty, AnsiText.Strip(null));
    }

    [Fact]
    public void 能判断是否含转义序列()
    {
        Assert.True(AnsiText.ContainsAnsi("\x1B[31mred"));
        Assert.False(AnsiText.ContainsAnsi("plain"));
    }
}
