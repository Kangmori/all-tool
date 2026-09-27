using Swpj.Core.Manifest;

namespace Swpj.Core.Execution;

/// <summary>
/// 把某个动作的字段值按 <c>docs/spec/manifest-v1.md</c> 的规则展开成 argv（不含可执行文件本身）。
///
/// 这个类是纯函数、无 IO、无副作用——它是全项目最容易出错的地方，所以必须被单测覆盖。
/// 规范里的三条硬规则在此实现：
///   1. 字段声明顺序 = argv 顺序；
///   2. 空值（null / "" / 空集合）不产生任何 token；
///   3. 只生成 token 序列，绝不拼成命令行字符串（拼接会带来注入与转义问题）。
/// </summary>
public static class ArgvBuilder
{
    /// <summary>生成 argv。返回值不含可执行文件，宿主负责把它放在最前面。</summary>
    /// <param name="action">动作定义。</param>
    /// <param name="values">字段值。键是字段 id，值可以是 string / bool / 数字 / 集合。</param>
    public static IReadOnlyList<string> Build(
        ManifestAction action,
        IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(values);

        var argv = new List<string>();

        if (!string.IsNullOrEmpty(action.Command))
        {
            argv.Add(action.Command);
        }

        if (action.CommandArgs is { Count: > 0 })
        {
            argv.AddRange(action.CommandArgs);
        }

        foreach (var field in action.Fields ?? [])
        {
            var fieldId = field.Id;
            if (string.IsNullOrEmpty(fieldId))
            {
                continue;
            }

            if (!values.TryGetValue(fieldId, out var raw))
            {
                continue;
            }

            Append(argv, field, raw);
        }

        if (action.FixedArgs is { Count: > 0 })
        {
            argv.AddRange(action.FixedArgs);
        }

        return argv;
    }

    /// <summary>
    /// 把完整命令行拼成给人看的字符串，仅用于界面展示（<c>showCommandLine</c>）。
    /// 注意：**绝不用它来执行**——执行必须传 argv 数组。
    /// </summary>
    public static string FormatForDisplay(string executable, IEnumerable<string> argv)
    {
        var parts = new List<string> { Quote(executable) };
        parts.AddRange(argv.Select(Quote));
        return string.Join(' ', parts);
    }

    private static string Quote(string token)
    {
        if (token.Length == 0)
        {
            return "\"\"";
        }

        return token.Any(char.IsWhiteSpace) || token.Contains('"')
            ? $"\"{token.Replace("\"", "\"\"")}\""
            : token;
    }

    private static void Append(List<string> argv, ManifestField field, object? raw)
    {
        switch (field.Style)
        {
            case FieldStyle.Positional:
                AppendPositional(argv, field, raw);
                break;
            case FieldStyle.Attached:
            case FieldStyle.Repeated:
                AppendWithPrefix(argv, field, raw, separate: false);
                break;
            case FieldStyle.Separate:
                AppendWithPrefix(argv, field, raw, separate: true);
                break;
            case FieldStyle.Flag:
                AppendFlag(argv, field, raw);
                break;
            case FieldStyle.Literal:
                AppendLiteral(argv, field, raw);
                break;
            default:
                throw new ManifestUsageException(
                    $"字段 {field.Id}：未知的 style '{field.Style}'");
        }
    }

    private static void AppendPositional(List<string> argv, ManifestField field, object? raw)
    {
        if (IsEmpty(raw))
        {
            return;
        }

        // perLine：按行拆成 token（用于 rn 这类成对参数）。
        // 每行内部再按空白拆分，支持用双引号包住含空格的整段。
        if (field.PositionalMode == PositionalMode.PerLine && raw is string text)
        {
            foreach (var line in text.Split('\n'))
            {
                foreach (var token in SplitTokens(line))
                {
                    argv.Add(token);
                }
            }

            return;
        }

        foreach (var item in AsItems(raw))
        {
            argv.Add(Stringify(item));
        }
    }

    private static void AppendWithPrefix(
        List<string> argv,
        ManifestField field,
        object? raw,
        bool separate)
    {
        if (IsEmpty(raw))
        {
            return;
        }

        var prefix = field.Prefix ?? string.Empty;
        var separator = field.Separator ?? string.Empty;

        foreach (var item in AsItems(raw))
        {
            if (IsEmpty(item))
            {
                continue;
            }

            var text = Stringify(item);

            if (separate && prefix.Length > 0)
            {
                argv.Add(prefix);
                argv.Add(text);
            }
            else
            {
                argv.Add(prefix + separator + text);
            }
        }
    }

    private static void AppendFlag(List<string> argv, ManifestField field, object? raw)
    {
        var on = raw switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var parsed) && parsed,
            _ => false,
        };

        if (on && !string.IsNullOrEmpty(field.Prefix))
        {
            argv.Add(field.Prefix);
        }
    }

    private static void AppendLiteral(List<string> argv, ManifestField field, object? raw)
    {
        if (IsEmpty(raw))
        {
            return;
        }

        var value = Stringify(raw);
        var option = (field.Values ?? [])
            .FirstOrDefault(v => string.Equals(v.Value, value, StringComparison.Ordinal));

        if (option is null)
        {
            // 这是工具包作者的笔误或界面传了非法值，必须显式报错而不是静默忽略——
            // 静默会生成一条"看起来对"但行为不同的命令，那比报错危险得多。
            throw new ManifestUsageException(
                $"字段 {field.Id}：取值 '{value}' 不在该字段的 literal 选项里");
        }

        // args 为空数组表示"这个选项不产生任何参数"（例如"使用程序默认行为"）。
        if (option.Args is { Count: > 0 })
        {
            argv.AddRange(option.Args);
        }
    }

    internal static IEnumerable<string> SplitTokens(string line)
    {
        var tokens = new List<string>();
        var buffer = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (buffer.Length > 0)
                {
                    tokens.Add(buffer.ToString());
                    buffer.Clear();
                }

                continue;
            }

            buffer.Append(ch);
        }

        if (buffer.Length > 0)
        {
            tokens.Add(buffer.ToString());
        }

        return tokens;
    }

    internal static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => s.Length == 0,
        bool => false,
        System.Collections.IEnumerable e => !e.GetEnumerator().MoveNext(),
        _ => false,
    };

    private static IEnumerable<object?> AsItems(object? raw)
    {
        if (raw is null)
        {
            yield break;
        }

        if (raw is string || raw is bool)
        {
            yield return raw;
            yield break;
        }

        if (raw is System.Collections.IEnumerable items)
        {
            foreach (var item in items)
            {
                yield return item;
            }

            yield break;
        }

        yield return raw;
    }

    private static string Stringify(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
