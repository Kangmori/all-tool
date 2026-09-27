using System.Globalization;
using System.Text.RegularExpressions;
using Swpj.Core.Manifest;

namespace Swpj.Core.Execution;

/// <summary>从输出行里提取进度。规则来自清单的 <c>output.progress</c>。</summary>
public sealed class ProgressParser
{
    private readonly Regex? _pattern;
    private readonly int _group;
    private readonly string _unit = "unknown";

    public ProgressParser(ProgressSpec? spec)
    {
        if (spec?.Pattern is { Length: > 0 } pattern)
        {
            _pattern = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            _group = spec.Group > 0 ? spec.Group : 1;
            _unit = spec.Unit ?? "unknown";
        }
    }

    public string Unit => _unit;

    /// <summary>尝试从一行输出里解析出进度值。</summary>
    public bool TryParse(string? line, out double value)
    {
        value = 0;

        if (_pattern is null || string.IsNullOrEmpty(line))
        {
            return false;
        }

        // 走 ConPTY 时输出里混着颜色与光标控制序列，先清掉再匹配，
        // 否则形如 "\x1b[32m 45%\x1b[0m" 的进度行会被转义序列打断。
        var match = _pattern.Match(AnsiText.Strip(line));
        if (!match.Success || match.Groups.Count <= _group)
        {
            return false;
        }

        return double.TryParse(
            match.Groups[_group].Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }
}

/// <summary>退出码的解释结果。</summary>
public sealed record ExitCodeVerdict(int Code, string Severity, string Meaning)
{
    public bool IsSuccess => Severity == "ok";
}

/// <summary>把裸退出码翻译成人能看懂的结果。</summary>
public static class ExitCodeInterpreter
{
    public const string Ok = "ok";
    public const string Warning = "warning";
    public const string Error = "error";

    public static ExitCodeVerdict Interpret(int exitCode, IReadOnlyList<ExitCodeSpec>? table)
    {
        var known = table?.FirstOrDefault(e => e.Code == exitCode);
        if (known is not null)
        {
            return new ExitCodeVerdict(
                exitCode,
                known.Severity ?? Error,
                known.Meaning ?? string.Empty);
        }

        // 表里没写：按"0 成功、其它失败"处理，并如实说明这是通用规则而非文档结论。
        return exitCode == 0
            ? new ExitCodeVerdict(0, Ok, "成功（该工具包的 exitCodes 表里没有这一条，按通用规则判定）")
            : new ExitCodeVerdict(
                exitCode,
                Error,
                $"失败（该工具包的 exitCodes 表里没有这一码，按通用规则判定；若文档确实定义过，请补进清单）");
    }

    /// <summary>被取消或超时时，退出码没有意义，用这个生成结论。</summary>
    public static ExitCodeVerdict FromInterruption(bool canceled, bool timedOut)
    {
        if (canceled)
        {
            return new ExitCodeVerdict(-1, Warning, "已被用户取消");
        }

        return timedOut
            ? new ExitCodeVerdict(-1, Warning, "已超时并被终止")
            : new ExitCodeVerdict(-1, Error, "未知的中断原因");
    }
}
