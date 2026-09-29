using System.Text.RegularExpressions;

namespace AllTool.Core.Manifest;

/// <summary>
/// 清单的结构校验（运行时）。
///
/// 与 <c>docs/spec/manifest-v1.schema.json</c> 的分工：
///   - JSON Schema 是**编写工具包时的权威门禁**（跑在 CI 与本地脚本里，规则最全）；
///   - 这里只覆盖"加载器必须自己确认"的结构性规则，好让宿主在运行时也能给出清晰的报错，
///     而不是等到 ArgvBuilder 抛空引用。
/// 两者若出现分歧，以 JSON Schema 为准，并回来同步这里。
/// </summary>
public static class ManifestValidation
{
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex ActionIdPattern = new("^[a-z][a-z0-9-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex VersionPattern = new("^[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex DatePattern = new("^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.Compiled);

    private static readonly string[] KnownStyles =
    [
        FieldStyle.Positional, FieldStyle.Attached, FieldStyle.Separate,
        FieldStyle.Flag, FieldStyle.Literal, FieldStyle.Repeated,
    ];

    /// <summary>返回问题清单；空列表表示通过。</summary>
    public static IReadOnlyList<string> Validate(ToolManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = new List<string>();

        if (manifest.Spec != 1)
        {
            errors.Add($"spec 必须是 1，实际是 {manifest.Spec}");
        }

        RequireMatch(errors, "id", manifest.Id, IdPattern);
        RequireMatch(errors, "manifestVersion", manifest.ManifestVersion, VersionPattern);

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            errors.Add("缺少 name");
        }

        if (manifest.Locate is null)
        {
            errors.Add("缺少 locate");
        }
        else if (string.IsNullOrWhiteSpace(manifest.Locate.Executable))
        {
            errors.Add("locate.executable 不能为空");
        }

        if (manifest.Actions is null || manifest.Actions.Count == 0)
        {
            errors.Add("actions 不能为空");
            return errors;
        }

        var actionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in manifest.Actions)
        {
            var where = $"动作 {action.Id ?? "<无 id>"}";

            RequireMatch(errors, $"{where}.id", action.Id, ActionIdPattern);

            if (!string.IsNullOrWhiteSpace(action.Id) && !actionIds.Add(action.Id))
            {
                errors.Add($"{where}：动作 id 重复");
            }

            if (string.IsNullOrWhiteSpace(action.Title))
            {
                errors.Add($"{where}：缺少 title");
            }

            // 只要求"写了这个键"，**允许空串**。
            //
            // 为什么：有一类程序根本没有子命令——Windows 自带命令就是典型
            // （`ping 8.8.8.8`、`ipconfig`、`netstat -an` 里没有"命令"这一段）。
            // 这种情况下 `command: ""` 是唯一诚实的写法；强迫作者填个非空值
            // 就等于编造参数（违反 R1）。
            //
            // 执行层本来就是这么支持的（ArgvBuilder 用 IsNullOrEmpty 判断，空则跳过），
            // 这里原先用 IsNullOrWhiteSpace 拦了一道，属于**校验与执行自相矛盾**——
            // 12 个 Windows 工具包一进来就把这个矛盾撞出来了（子智能体报告 §0）。
            // schema 里仍然把 command 列为 required：强制作者显式决定"有没有子命令"，
            // 免得 7z 那种必须写 `command: a` 的地方漏写而产生一条静默错误的命令。
            if (action.Command is null)
            {
                errors.Add($"{where}：缺少 command（没有子命令的工具请显式写 command: \"\"）");
            }

            if (action.ExecutionOrDefault is not ("run" or "info" or "terminal"))
            {
                errors.Add($"{where}：execution '{action.Execution}' 不是合法值（只能是 run / info / terminal）");
            }

            if (action.Sources is null || action.Sources.Count == 0)
            {
                errors.Add($"{where}：至少要有一条 sources（参数依据）");
            }

            ValidateSources(errors, where, action.Sources);

            var fieldIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in action.Fields ?? [])
            {
                var fieldWhere = $"{where} 字段 {field.Id ?? "<无 id>"}";

                if (string.IsNullOrWhiteSpace(field.Id))
                {
                    errors.Add($"{fieldWhere}：缺少 id");
                }
                else if (!fieldIds.Add(field.Id))
                {
                    errors.Add($"{fieldWhere}：字段 id 在动作内重复");
                }

                if (string.IsNullOrWhiteSpace(field.Label))
                {
                    errors.Add($"{fieldWhere}：缺少 label");
                }

                if (string.IsNullOrWhiteSpace(field.Type))
                {
                    errors.Add($"{fieldWhere}：缺少 type");
                }

                if (string.IsNullOrWhiteSpace(field.Style) || !KnownStyles.Contains(field.Style))
                {
                    errors.Add($"{fieldWhere}：style '{field.Style}' 不是合法值");
                    continue;
                }

                if (field.Style == FieldStyle.Literal && (field.Values is null || field.Values.Count == 0))
                {
                    errors.Add($"{fieldWhere}：literal 风格必须给出 values");
                }

                if (field.Style is FieldStyle.Attached or FieldStyle.Separate
                    or FieldStyle.Flag or FieldStyle.Repeated
                    && string.IsNullOrEmpty(field.Prefix))
                {
                    errors.Add($"{fieldWhere}：style 为 {field.Style} 时必须给出 prefix");
                }

                if (field.Values is not null)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var value in field.Values)
                    {
                        // 注意：value 允许为空字符串，它表示「不指定 / 不输出参数」这种选项
                        // （空值规则会让它不产生任何 token）。这里只查重复。
                        if (value.Value is not null && !seen.Add(value.Value))
                        {
                            errors.Add($"{fieldWhere}：values 里 value '{value.Value}' 重复");
                        }
                    }
                }
            }

            if (action.Output is { ShowCommandLine: false })
            {
                errors.Add($"{where}：output.showCommandLine 不能为 false（硬规则 R6：不隐藏命令）");
            }
        }

        return errors;
    }

    private static void ValidateSources(List<string> errors, string where, List<ManifestSource>? sources)
    {
        foreach (var source in sources ?? [])
        {
            if (string.IsNullOrWhiteSpace(source.Title))
            {
                errors.Add($"{where}：sources 里有一条缺少 title");
            }

            if (string.IsNullOrWhiteSpace(source.Retrieved))
            {
                errors.Add($"{where}：sources 里 '{source.Title}' 缺少 retrieved 日期");
            }
            else if (!DatePattern.IsMatch(source.Retrieved))
            {
                errors.Add($"{where}：sources 里 '{source.Title}' 的 retrieved 必须是 YYYY-MM-DD");
            }
        }
    }

    private static void RequireMatch(List<string> errors, string name, string? value, Regex pattern)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"缺少 {name}");
        }
        else if (!pattern.IsMatch(value))
        {
            errors.Add($"{name} 的格式不合法：'{value}'（应匹配 {pattern}）");
        }
    }
}
