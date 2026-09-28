namespace Swpj.Core.Manifest;

/// <summary>
/// 工具包清单的对象模型。字段名与 <c>docs/spec/manifest-v1.schema.json</c> 一一对应
/// （由 YamlDotNet 的 CamelCaseNamingConvention 做映射，例如 <c>manifestVersion</c> → <see cref="ManifestVersion"/>）。
///
/// 所有可省略的属性都是可空的：YAML 里没写就是 null，由 <see cref="ManifestValidation"/> 判断是否合法。
/// </summary>
public sealed class ToolManifest
{
    public int Spec { get; init; }
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? ManifestVersion { get; init; }
    public string? AppVersion { get; init; }
    public string? Description { get; init; }
    public string? Homepage { get; init; }
    public string? License { get; init; }

    /// <summary>
    /// 工具包在界面上的默认分组（例如"包管理""压缩归档"）。可被用户在宿主里自定义覆盖。
    /// 注意与字段级的 <c>group</c> 区分：那个是"表单内的小节"，这个是"左侧列表的分组"。
    /// </summary>
    public string? Category { get; init; }

    public List<string>? Tags { get; init; }
    public List<ManifestSource>? Sources { get; init; }
    public LocateSpec? Locate { get; init; }
    public RuntimeSpec? Runtime { get; init; }
    public List<ExitCodeSpec>? ExitCodes { get; init; }
    public List<ManifestAction>? Actions { get; init; }
}

/// <summary>参数知识的出处。每个动作都必须带至少一条。</summary>
public sealed class ManifestSource
{
    public string? Title { get; init; }
    public string? Url { get; init; }
    public string? Retrieved { get; init; }
    public string? AppliesTo { get; init; }
    public string? Note { get; init; }
}

/// <summary>如何找到这个程序、如何取版本。</summary>
public sealed class LocateSpec
{
    public string? Executable { get; init; }
    public List<string>? AlternativeNames { get; init; }
    public List<string>? SearchPaths { get; init; }
    public List<string>? VersionArgs { get; init; }
    public string? VersionPattern { get; init; }
    public string? MinVersion { get; init; }
    public string? NotFoundHint { get; init; }
}

/// <summary>执行模型。</summary>
public sealed class RuntimeSpec
{
    public string? Encoding { get; init; }
    public bool UseShell { get; init; }
    public bool RequiresAdmin { get; init; }
    public string? WorkingDirectory { get; init; }
    public int TimeoutSeconds { get; init; }
    public bool PreventConcurrentRuns { get; init; }
    public Dictionary<string, string>? Env { get; init; }

    /// <summary>
    /// 是否为子进程分配伪控制台（ConPTY）。默认 false，按工具包选择。
    ///
    /// 需要它的典型情形：**只在真控制台里画进度的程序**。7-Zip 实测就是这样——
    /// 被重定向时 40 MB 输入的完整输出只有 354 字节、一个 % 都没有；接上伪控制台才有百分比。
    /// 代价：输出里混入 ANSI 转义序列（用 AnsiText 清理），且程序会认为自己在一台真终端里。
    /// </summary>
    public bool UsePseudoConsole { get; init; }
}

/// <summary>退出码语义，用于把裸数字翻译成人能看懂的结果。</summary>
public sealed class ExitCodeSpec
{
    public int Code { get; init; }
    public string? Meaning { get; init; }
    public string? Severity { get; init; }
}

/// <summary>一个可点击的动作。</summary>
public sealed class ManifestAction
{
    public string? Id { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Command { get; init; }
    public List<string>? CommandArgs { get; init; }
    public List<ManifestSource>? Sources { get; init; }
    public string? Danger { get; init; }

    /// <summary>
    /// 规范化后的危险级别：**没写 danger 就是 none**。
    ///
    /// 别直接用 <see cref="Danger"/> 去比 —— 它是可空的，`null != "none"` 在 C# 里是 true，
    /// 于是每个没标 danger 的只读动作都会被当成"会覆盖数据"（实测踩过：scoop status 也挂着
    /// "⚠ 此动作会覆盖已有文件。"）。界面一律用这个属性判断。
    /// </summary>
    public string DangerOrDefault =>
        string.IsNullOrWhiteSpace(Danger) ? DangerLevel.None : Danger.Trim();

    public string? ConfirmText { get; init; }

    /// <summary>动作在左侧列表里的默认分组（例如"查询""安装""缓存"）。可被用户自定义覆盖。</summary>
    public string? Category { get; init; }

    /// <summary>执行完之后可以推荐的下一步（见 <see cref="NextStepSpec"/>）。</summary>
    public List<NextStepSpec>? NextSteps { get; init; }

    public string? WorkingDirectory { get; init; }
    public List<ManifestField>? Fields { get; init; }
    public List<string>? FixedArgs { get; init; }
    public OutputSpec? Output { get; init; }
    public List<ManifestExample>? Examples { get; init; }
}

/// <summary>界面上的一个输入项，以及它如何变成 argv。</summary>
public sealed class ManifestField
{
    public string? Id { get; init; }
    public string? Label { get; init; }
    public string? Help { get; init; }
    public string? Doc { get; init; }
    public string? Type { get; init; }
    public string? Style { get; init; }
    public string? Prefix { get; init; }
    public string? SwitchBase { get; init; }
    public string? Separator { get; init; }
    public string? PositionalMode { get; init; }
    public List<FieldValue>? Values { get; init; }

    /// <summary>YAML 里的 <c>default</c>。类型不确定（可能是 string / bool / 数字），由 ArgvBuilder 容错处理。</summary>
    public object? Default { get; init; }

    public bool Required { get; init; }
    public string? Placeholder { get; init; }
    public string? Group { get; init; }
    public bool Advanced { get; init; }
    public bool Repeatable { get; init; }
    public bool Save { get; init; }
    public List<string>? Accept { get; init; }
    public string? VisibleWhen { get; init; }
}

/// <summary>enum / multiselect 的一个选项；literal 风格下 <see cref="Args"/> 就是原样输出的 token。</summary>
public sealed class FieldValue
{
    public string? Value { get; init; }
    public string? Label { get; init; }
    public string? Help { get; init; }
    public List<string>? Args { get; init; }
    public bool IsDefault { get; init; }
}

/// <summary>输出与呈现方式。</summary>
public sealed class OutputSpec
{
    public string? Mode { get; init; }
    public bool ShowCommandLine { get; init; }
    public ProgressSpec? Progress { get; init; }
    public string? OpenOnFinish { get; init; }
    public string? ResultNote { get; init; }
}

/// <summary>从输出里提取进度的正则。</summary>
public sealed class ProgressSpec
{
    public string? Pattern { get; init; }
    public string? Unit { get; init; }
    public int Group { get; init; } = 1;
}

/// <summary>官方文档里的示例，兼作快速填充预设与冒烟测试夹具。</summary>
public sealed class ManifestExample
{
    public string? Title { get; init; }
    public List<string>? Args { get; init; }
    public int ExpectExitCode { get; init; }
}

/// <summary>
/// 一条"执行完之后可以推荐什么"的规则。
///
/// 它是**声明式**的：宿主只做正则匹配与按钮呈现，不需要理解任何具体软件的语义。
/// 这样推荐逻辑既可审计（有出处、可评审），又不必在宿主里为每个软件写特例——
/// 与本项目"清单里不写代码"的底线一致。
/// </summary>
public sealed class NextStepSpec
{
    /// <summary>按钮上的文字，例如"一键更新全部"。</summary>
    public string? Title { get; init; }

    /// <summary>要点哪个动作（同一工具包内的动作 id）。</summary>
    public string? Action { get; init; }

    /// <summary>可选的正则：在刚执行完的输出里命中才推荐（多行匹配）。不填则总是推荐。</summary>
    public string? When { get; init; }

    /// <summary>给用户看的理由，例如"检测到有可用更新"。</summary>
    public string? Reason { get; init; }

    /// <summary>可选：执行目标动作前预填的字段值（字段 id → 值）。</summary>
    public Dictionary<string, object?>? Values { get; init; }
}

/// <summary>字段风格的字符串常量（与 schema 的 enum 一致）。</summary>
public static class FieldStyle
{
    public const string Positional = "positional";
    public const string Attached = "attached";
    public const string Separate = "separate";
    public const string Flag = "flag";
    public const string Literal = "literal";
    public const string Repeated = "repeated";
}

/// <summary>positional 字段的展开方式。</summary>
public static class PositionalMode
{
    public const string Literal = "literal";
    public const string PerLine = "perLine";
}

/// <summary>动作的危险级别。</summary>
public static class DangerLevel
{
    public const string None = "none";
    public const string Overwrite = "overwrite";
    public const string Destructive = "destructive";
}
