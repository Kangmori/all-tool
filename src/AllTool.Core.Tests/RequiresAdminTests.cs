using AllTool.Core.Manifest;

namespace AllTool.Core.Tests;

/// <summary>
/// 管理员权限需求的判定。
///
/// 这条规则来自实际需要：Windows 自带命令里，同一个软件往往只有**部分**动作需要提权
/// （`chkdsk` 只读检查不需要、`chkdsk /f` 需要；`sfc /verifyonly` 不需要、`sfc /scannow` 需要），
/// 所以动作级要能覆盖工具包级的默认值。
/// </summary>
public class RequiresAdminTests
{
    private static ManifestAction Make(bool? actionLevel) => new()
    {
        Id = "x",
        Title = "x",
        Command = "x",
        RequiresAdmin = actionLevel,
        Sources = [new ManifestSource { Title = "t", Retrieved = "2026-09-27" }],
    };

    [Fact]
    public void 动作没写时跟随工具包级的默认值()
    {
        Assert.True(Make(null).RequiresAdminEffective(packageDefault: true));
        Assert.False(Make(null).RequiresAdminEffective(packageDefault: false));
    }

    [Fact]
    public void 动作写了就以动作为准()
    {
        // 工具包整体不需要管理员，但这个动作需要
        Assert.True(Make(true).RequiresAdminEffective(packageDefault: false));

        // 工具包整体需要，但这个动作不需要（例如只读检查）
        Assert.False(Make(false).RequiresAdminEffective(packageDefault: true));
    }
}
