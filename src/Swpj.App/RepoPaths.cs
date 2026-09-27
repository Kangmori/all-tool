namespace Swpj.App;

/// <summary>
/// 定位仓库根目录。
///
/// 注意：这是**开发期**的做法（以 AGENTS.md 为标志向上找）。
/// 将来做成可分发形态时，plugins 目录应当可配置（程序目录旁 / %APPDATA%），
/// 这一点已记进 docs/ai/project-state.json 的未决事项。
/// </summary>
internal static class RepoPaths
{
    public static string FindRoot()
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

        throw new InvalidOperationException("向上找不到仓库根目录（以 AGENTS.md 为标志）。");
    }

    public static string PluginsDirectory => Path.Combine(FindRoot(), "plugins");
}
