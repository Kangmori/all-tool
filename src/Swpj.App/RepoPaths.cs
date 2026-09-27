namespace Swpj.App;

/// <summary>
/// 定位工具包目录。
///
/// 查找顺序（前者优先）：
///   1. **可执行文件所在目录旁的 <c>plugins\</c>** —— 将来做成可分发形态时工具包就在这里；
///      这样"把程序目录拷走就能用"成立，不需要仓库结构。
///   2. 向上找到仓库根（以 <c>AGENTS.md</c> 为标志）后的 <c>plugins\</c> —— 开发期用，
///      因为开发时 exe 在 <c>src\Swpj.App\bin\...</c> 里，旁边没有工具包。
///
/// 待决事项 D7（<c>docs/ai/project-state.json</c>）问的是"分发形态下放哪"。
/// 这里先按"程序目录旁优先"落地，不排除以后加 %APPDATA% 或可配置路径——
/// 那只是在这个方法里多插一层查找。
/// </summary>
internal static class RepoPaths
{
    public static string FindPluginsDirectory()
    {
        var besideExecutable = Path.Combine(AppContext.BaseDirectory, "plugins");

        if (Directory.Exists(besideExecutable))
        {
            return besideExecutable;
        }

        return Path.Combine(FindRepositoryRoot(), "plugins");
    }

    /// <summary>向上找仓库根（以 AGENTS.md 为标志）。只在开发期用得到。</summary>
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "既没有在程序目录旁找到 plugins 目录，也向上找不到仓库根目录（以 AGENTS.md 为标志）。" +
            "开发期请从仓库内运行；分发形态请把 plugins 目录放在程序旁边。");
    }
}
