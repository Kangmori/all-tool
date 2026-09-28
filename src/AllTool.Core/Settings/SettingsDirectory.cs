namespace AllTool.Core.Settings;

/// <summary>
/// 设置文件放哪、以及从旧名字迁移。
///
/// 历史上这个程序叫 swpj，设置存在 <c>%APPDATA%\swpj</c>。改名为 All Tool 之后
/// 如果直接换目录，用户"记住的上次输入"和"自定义分组"就凭空没了——所以这里做一次
/// **复制式迁移**（不删旧文件，旧版本仍可用）：新目录里没有就从旧目录拷一份过来。
/// </summary>
internal static class SettingsDirectory
{
    public const string Name = "AllTool";

    public const string LegacyName = "swpj";

    public static string Current => Path.Combine(Root, Name);

    private static string Root =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>给出某个设置文件的最终路径，必要时先从旧目录迁移。</summary>
    public static string Resolve(string fileName)
    {
        var current = Path.Combine(Current, fileName);

        if (File.Exists(current))
        {
            return current;
        }

        var legacy = Path.Combine(Root, LegacyName, fileName);

        if (File.Exists(legacy))
        {
            try
            {
                Directory.CreateDirectory(Current);
                File.Copy(legacy, current);
            }
            catch (Exception)
            {
                // 迁移失败就继续用旧文件，不能因为迁移把功能弄坏
                return legacy;
            }
        }

        return current;
    }
}
