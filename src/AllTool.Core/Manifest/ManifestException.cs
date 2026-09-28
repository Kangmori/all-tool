namespace AllTool.Core.Manifest;

/// <summary>清单本身有问题（读不了、结构不合法、缺必填项）。属于"工具包作者的错"。</summary>
public sealed class ManifestException : Exception
{
    public ManifestException(string message) : base(message)
    {
    }

    public ManifestException(string message, Exception inner) : base(message, inner)
    {
    }

    /// <summary>校验发现的问题清单（多条）。</summary>
    public ManifestException(string origin, IReadOnlyList<string> errors)
        : base($"清单校验失败 {origin}：{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", errors)}")
    {
        Errors = errors;
    }

    public IReadOnlyList<string>? Errors { get; }
}

/// <summary>
/// 清单合法，但这次使用的方式不对（例如给 literal 字段传了选项之外的值）。
/// 属于"宿主或界面传值有 bug"，必须显式失败，不能静默生成一条行为不同的命令。
/// </summary>
public sealed class ManifestUsageException : Exception
{
    public ManifestUsageException(string message) : base(message)
    {
    }
}
