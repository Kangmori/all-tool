using System.Text;
using System.Text.RegularExpressions;

namespace AllTool.Core.Execution;

/// <summary>
/// 去掉控制台程序输出的 ANSI 转义序列。
///
/// 为什么需要：走 ConPTY 之后，子进程认为自己在一个真控制台里，于是会输出颜色、光标移动、
/// 以及"清行"这类控制序列。这些字节直接显示在界面上就是乱码，也会打断进度正则的匹配。
/// </summary>
public static partial class AnsiText
{
    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)|\x1B[@-Z\\-_]")]
    private static partial Regex AnsiPattern();

    /// <summary>去掉转义序列，并把光标控制的残留字符（\r、\b）清掉。</summary>
    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var stripped = AnsiPattern().Replace(text, string.Empty);
        return stripped.Replace("\r", string.Empty).Replace("\b", string.Empty);
    }

    public static bool ContainsAnsi(string? text) =>
        !string.IsNullOrEmpty(text) && AnsiPattern().IsMatch(text);
}
