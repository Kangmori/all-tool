namespace AllTool.Core.Manifest;

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

    /// <summary>
    /// 工具包类型，**只影响界面显示方式**（见 MainWindow 的 PackageDisplay）：
    /// user（默认，用户自己装的）/ system（系统自带）/ interactive（交互式、会弹窗）/
    /// dangerous（含不可逆的高危操作）。
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>规范化后的类型：没写就是 user。</summary>
    public string KindOrDefault =>
        string.IsNullOrWhiteSpace(Kind) ? "user" : Kind.Trim().ToLowerInvariant();

    /// <summary>
    /// 一句话说明这个软件是干什么的（鼠标悬停在工具包条目上时显示）。
    /// 写不出十个字以内就别写——界面会退回用 <see cref="Description"/> 的第一句。
    /// </summary>
    public string? Summary { get; init; }

    public string? Homepage { get; init; }
    public string? License { get; init; }

    /// <summary>
    /// 工具包在界面上的默认分组（例如"包管理""压缩归档"）。可被用户在宿主里自定义覆盖。
    /// 注意与字段级的 <c>group</c> 区分：那个是"表单内的小节"，这个是"左侧列表的分组"。
    /// </summary>
    public string? Category { get; init; }

    public List<string>? Tags { get; init; }
    public List<ManifestSource>? Sources { get; init; }

    /// <summary>
    /// 会话型程序（例如 diskpart）：一次动作 = 把"当前生效的选择类命令"和"这次要跑的命令"
    /// 拼成一个脚本喂给它。见 <see cref="Execution.SessionScriptBuilder"/> 里为什么用脚本重放
    /// 而不是长驻进程。
    /// </summary>
    public SessionSpec? Session { get; init; }

    /// <summary>
    /// 右键工具包时额外显示的"与这个软件相关"的操作（例如"检查可用更新"）。
    /// 这些是**软件专有**的，所以由清单声明；而"打开所在目录""打开官网"这类通用项由宿主固定提供。
    /// </summary>
    public List<QuickActionSpec>? QuickActions { get; init; }
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

    /// <summary>
    /// 这个动作是否需要管理员权限。不写则跟随工具包级的 <c>runtime.requiresAdmin</c>。
    ///
    /// 为什么要动作级：同一个软件往往只有部分动作需要提权
    /// （例如 `chkdsk` 只读检查不需要，`chkdsk /f` 修盘需要；`sfc /verifyonly` 与 `sfc /scannow` 也是）。
    /// </summary>
    public bool? RequiresAdmin { get; init; }

    /// <summary>把工具包级的默认值算进来，得到这个动作最终是否需要管理员。</summary>
    public bool RequiresAdminEffective(bool packageDefault) => RequiresAdmin ?? packageDefault;

    /// <summary>
    /// 这个动作怎么执行：<c>run</c>（默认，宿主直接跑并捕获输出）、
    /// <c>info</c>（宿主**不执行**，只显示命令行并提供复制/在终端打开）、
    /// <c>terminal</c>（需要交互或会弹窗，直接在真终端里打开）。
    ///
    /// 见规范 §2.6。判断标准是"这一步该不该由工具替你做决定"，
    /// 而不是"命令危不危险"——例如 ipconfig /release 会切断网络，
    /// 工具替用户按下回车并不比让他自己按更好。
    /// </summary>
    public string? Execution { get; init; }

    /// <summary>会话型程序里这条动作要发的命令（字段值会代入，例如 "select disk {index}"）。</summary>
    public string? SessionCommand { get; init; }

    /// <summary>这条命令成功后确立的状态键（对应 <see cref="SessionSpec.State"/> 里的 key）。</summary>
    public string? Establishes { get; init; }

    /// <summary>需要哪些状态键才允许执行；不满足就灰显。</summary>
    public List<string>? Requires { get; init; }

    /// <summary>灰显时显示的原因，例如"先执行「选择磁盘」"。</summary>
    public string? RequiresHint { get; init; }

    /// <summary>
    /// 极高风险动作的**逐字确认短语**（例如"清空磁盘 0"）。
    /// 点一下"确定"和"意识到自己在擦哪块盘"之间没有认知负担，打字才有。
    /// </summary>
    public string? ConfirmPhrase { get; init; }

    /// <summary>规范化后的执行方式：没写就是 <c>run</c>。</summary>
    public string ExecutionOrDefault =>
        string.IsNullOrWhiteSpace(Execution) ? "run" : Execution.Trim().ToLowerInvariant();

    /// <summary>宿主是否会直接执行它。</summary>
    public bool HostRunsIt => ExecutionOrDefault == "run";

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
/// 工具包右键菜单里的一条"软件专有"操作：点它会切到某个动作并执行。
/// 与 <see cref="NextStepSpec"/> 的区别：这个不看输出，是用户主动发起的入口。
/// </summary>
public sealed class QuickActionSpec
{
    /// <summary>菜单项文字，例如"检查可用更新"。</summary>
    public string? Title { get; init; }

    /// <summary>要点哪个动作（同一工具包内的动作 id）。写错则菜单里不显示。</summary>
    public string? Action { get; init; }
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

/// <summary>会话型程序的声明（见 <see cref="Execution.SessionScriptBuilder"/> 的说明）。</summary>
public sealed class SessionSpec
{
    /// <summary>覆盖工具包级 locate.executable（可选）。</summary>
    public string? Executable { get; init; }

    /// <summary>怎么把脚本交给它，例如 ["/s", "{script}"]；{script} 会替换成临时脚本路径。</summary>
    public List<string>? ScriptArgs { get; init; }

    /// <summary>临时脚本的扩展名（默认 .txt）。</summary>
    public string? ScriptExtension { get; init; }

    /// <summary>追加到脚本末尾的命令（例如 exit）。</summary>
    public string? ExitCommand { get; init; }

    /// <summary>命中它就认为这条命令失败了（不确立任何状态）。</summary>
    public string? ErrorPattern { get; init; }

    /// <summary>可以确立哪些状态。</summary>
    public List<SessionStateSpec>? State { get; init; }
}

/// <summary>会话里的一个状态：怎么从输出看出它成立了、捕获什么值。</summary>
public sealed class SessionStateSpec
{
    /// <summary>状态键，动作的 establishes / requires 引用它。</summary>
    public string? Key { get; init; }

    /// <summary>给用户看的名字，例如"已选中磁盘"。</summary>
    public string? Title { get; init; }

    /// <summary>命中它就认为这个状态成立（多行匹配）。</summary>
    public string? SuccessPattern { get; init; }

    /// <summary>把哪个捕获组存成变量（例如 disk），供后续命令与提示引用。</summary>
    public string? Capture { get; init; }
}
