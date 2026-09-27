using System.Text;

namespace Swpj.Core.Execution;

/// <summary>
/// 把「可执行文件 + argv 数组」拼成 Windows 命令行字符串。
///
/// 为什么还需要拼字符串：走 ConPTY 时不能用 .NET 的 <c>Process</c>（它不支持伪控制台），
/// 必须直接调 <c>CreateProcess</c>，而 Win32 只接受一条命令行。所以这一处**必须**拼，
/// 但要用微软文档里那套确定的引号规则，而不是 <c>string.Join(" ")</c>——后者会在参数
/// 含空格/引号/结尾反斜杠时产生完全不同的参数（也是注入的温床）。
///
/// 规则来源：CreateProcess 与 C 运行库的参数解析约定（MSDN: Parsing C++ Command-Line Arguments）。
/// 要点：反斜杠只在「引号之前」和「字符串结尾（位于引号内）」需要翻倍。
/// </summary>
public static class WindowsCommandLine
{
    /// <summary>按需要给单个参数加引号。</summary>
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return argument;
        }

        var builder = new StringBuilder(argument.Length + 2);
        builder.Append('"');

        for (var i = 0; i < argument.Length; i++)
        {
            var backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i == argument.Length)
            {
                // 结尾的一串反斜杠：因为后面就要加收尾引号，所以要翻倍，否则引号会被转义掉。
                builder.Append('\\', backslashes * 2);
                break;
            }

            if (argument[i] == '"')
            {
                // 引号前的反斜杠翻倍，再补一个反斜杠转义引号本身。
                builder.Append('\\', (backslashes * 2) + 1);
                builder.Append('"');
            }
            else
            {
                builder.Append('\\', backslashes);
                builder.Append(argument[i]);
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>拼出完整的命令行（第一个 token 是可执行文件，按需加引号）。</summary>
    public static string Build(string executable, IEnumerable<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        var builder = new StringBuilder(Quote(executable));

        foreach (var argument in arguments)
        {
            builder.Append(' ').Append(Quote(argument));
        }

        return builder.ToString();
    }
}
