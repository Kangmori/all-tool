using System.Text;
using System.Text.Json;

namespace Swpj.Core.Settings;

/// <summary>
/// 用户自定义的界面分组（工具包级与动作级）。
///
/// 为什么要有它：工具包清单里声明的 <c>category</c> 只是**默认分组**，
/// 但"我平时怎么归类"是用户自己的事——有人按用途分，有人按使用频率分，
/// 而一个软件往往同时属于好几类。所以清单给默认值、用户可以覆盖，覆盖存在这里。
///
/// 键的形式：<c>pkg/&lt;工具包 id&gt;</c> 与 <c>act/&lt;工具包 id&gt;/&lt;动作 id&gt;</c>。
/// 值为分组名；置为空表示"恢复清单里的默认分组"。
///
/// 与 <see cref="LastValuesStore"/> 一样：文件损坏或格式不认识时按空处理，绝不让宿主起不来。
/// </summary>
public sealed class GroupingStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Dictionary<string, string> _groups;

    public GroupingStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _groups = Load(path);
    }

    public static GroupingStore OpenDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "swpj");

        return new GroupingStore(Path.Combine(directory, "grouping.json"));
    }

    public string? GetPackageGroup(string packageId) =>
        _groups.GetValueOrDefault(PackageKey(packageId));

    public string? GetActionGroup(string packageId, string actionId) =>
        _groups.GetValueOrDefault(ActionKey(packageId, actionId));

    public void SetPackageGroup(string packageId, string? group) =>
        Set(PackageKey(packageId), group);

    public void SetActionGroup(string packageId, string actionId, string? group) =>
        Set(ActionKey(packageId, actionId), group);

    public int Count => _groups.Count;

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_groups, WriteOptions), new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // 记不住分组只是少了点便利，不该让操作失败。
        }
    }

    private void Set(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _groups.Remove(key);
            return;
        }

        _groups[key] = value.Trim();
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(path, Encoding.UTF8));

            return parsed is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (Exception)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string PackageKey(string packageId) => $"pkg/{packageId}";

    private static string ActionKey(string packageId, string actionId) => $"act/{packageId}/{actionId}";
}
