using AllTool.Core.Execution;
using AllTool.Core.Manifest;

namespace AllTool.Core.Tests;

/// <summary>
/// 会话型工具包的核心逻辑测试（脚本重放 + 状态门控）。
///
/// 这些测试**不启动任何真实程序**：会话的全部判断逻辑（需要什么前置条件、脚本怎么拼、
/// 输出里的哪一句代表成功）都被做成了纯函数，所以能在这里钉死。
/// 真机部分只剩"把脚本交给程序"这一句，那条路径由既有的执行器测试覆盖。
/// </summary>
public class SessionTests
{
    private static SessionSpec DiskPartSession() => new()
    {
        Executable = "diskpart.exe",
        ScriptArgs = ["/s", "{script}"],
        ExitCommand = "exit",
        ErrorPattern = @"(?im)^(Virtual Disk Service error|DiskPart encountered an error|.*\bnot found\b)",
        State =
        [
            new SessionStateSpec { Key = "disk", Title = "已选中磁盘", Capture = "disk", SuccessPattern = @"(?im)^Disk (\d+) is now the selected disk" },
            new SessionStateSpec { Key = "volume", Title = "已选中卷", Capture = "volume", SuccessPattern = @"(?im)^Volume (\d+) is now the selected volume" },
        ],
    };

    // ---------------------------------------------------------------- 前置条件

    [Fact]
    public void 没有前置条件时任何状态都满足()
    {
        var state = new SessionState();

        Assert.True(state.Satisfies(null));
        Assert.True(state.Satisfies([]));
        Assert.False(state.Satisfies(["disk"]));
    }

    [Fact]
    public void 确立了状态之后前置条件才满足()
    {
        var state = new SessionState();

        state.Establish("disk", "0", "select-disk", "select disk 0");

        Assert.True(state.Satisfies(["disk"]));
        Assert.False(state.Satisfies(["volume"]));
        Assert.False(state.Satisfies(["disk", "volume"]));
    }

    // ---------------------------------------------------------------- 脚本重放

    [Fact]
    public void 脚本会把选择类命令重放到前面()
    {
        var state = new SessionState();
        state.Establish("disk", "0", "select-disk", "select disk 0");

        var script = SessionScriptBuilder.Build(state, "create partition primary", "exit");

        Assert.Equal(
            ["select disk 0", "create partition primary", "exit"],
            SessionScriptBuilder.Describe(script));
    }

    [Fact]
    public void 同一个状态只重放最后一次()
    {
        var state = new SessionState();
        state.Establish("disk", "0", "select-disk", "select disk 0");
        state.Establish("disk", "1", "select-disk", "select disk 1");

        var script = SessionScriptBuilder.Build(state, "clean", "exit");

        Assert.Equal(["select disk 1", "clean", "exit"], SessionScriptBuilder.Describe(script));
        Assert.DoesNotContain("select disk 0", script);
    }

    [Fact]
    public void 多个状态按确立顺序重放()
    {
        var state = new SessionState();
        state.Establish("disk", "0", "select-disk", "select disk 0");
        state.Establish("volume", "3", "select-volume", "select volume 3");

        var script = SessionScriptBuilder.Build(state, "format fs=ntfs quick", null);

        Assert.Equal(
            ["select disk 0", "select volume 3", "format fs=ntfs quick"],
            SessionScriptBuilder.Describe(script));
    }

    [Fact]
    public void 清空状态后脚本里不再有重放命令()
    {
        var state = new SessionState();
        state.Establish("disk", "0", "select-disk", "select disk 0");
        state.Clear();

        Assert.True(state.IsEmpty);
        Assert.Equal(
            ["list disk", "exit"],
            SessionScriptBuilder.Describe(SessionScriptBuilder.Build(state, "list disk", "exit")));
    }

    // ---------------------------------------------------------------- 从输出推导状态

    [Fact]
    public void 输出里的成功语句会确立状态并捕获值()
    {
        var session = DiskPartSession();
        var state = new SessionState();

        var ok = SessionStateUpdater.Apply(
            session, "select-disk", "select disk 2",
            "Microsoft DiskPart 版本 10.0.26100\r\n\r\nDisk 2 is now the selected disk.\r\n",
            state, out var key);

        Assert.True(ok);
        Assert.Equal("disk", key);
        Assert.Equal("2", state.Captures["disk"]);
        Assert.True(state.Satisfies(["disk"]));
    }

    [Fact]
    public void 命中错误模式时不确立任何状态()
    {
        var session = DiskPartSession();
        var state = new SessionState();

        var ok = SessionStateUpdater.Apply(
            session, "select-disk", "select disk 99",
            "Virtual Disk Service error:\r\nThe specified disk is not valid.\r\n",
            state, out _);

        Assert.False(ok);
        Assert.True(state.IsEmpty);
        Assert.False(state.Satisfies(["disk"]));
    }

    [Fact]
    public void 输出里没有成功语句时什么都不变()
    {
        var session = DiskPartSession();
        var state = new SessionState();

        var ok = SessionStateUpdater.Apply(session, "select-disk", "select disk 0", "随便什么输出", state, out _);

        Assert.False(ok);
        Assert.True(state.IsEmpty);
    }

    [Fact]
    public void 正则写错时当作没命中而不是崩掉()
    {
        var broken = new SessionSpec
        {
            State = [new SessionStateSpec { Key = "x", SuccessPattern = "([未闭合" }],
        };

        var state = new SessionState();
        var ok = SessionStateUpdater.Apply(broken, "a", "b", "任意输出", state, out _);

        Assert.False(ok);
    }

    [Fact]
    public void 状态描述能带上捕获到的值()
    {
        var session = DiskPartSession();
        var state = new SessionState();
        state.Establish("disk", "0", "select-disk", "select disk 0");
        state.Establish("volume", "3", "select-volume", "select volume 3");

        Assert.Equal("已选中磁盘 0、已选中卷 3", state.Describe(session.State));
        Assert.Equal("未开始（没有前置条件被满足）", new SessionState().Describe(session.State));
    }
}
