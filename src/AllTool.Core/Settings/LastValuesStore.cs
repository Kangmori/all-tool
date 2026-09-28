using System.Text;
using System.Text.Json;

namespace AllTool.Core.Settings;

/// <summary>
/// 记住字段上次用过的值（规范里 <c>save: true</c> 的落地）。
///
/// 键是「工具包 id / 动作 id / 字段 id」三元组，值是**界面上的文本形式**：
///   - 文本/路径/数字 → 原样字符串
///   - 布尔 → "true" / "false"
///   - 多值（每行一项）→ 用换行连接
///   - 枚举 → 该项的 value
/// 这样存的是"用户填进去的样子"，回填时不需要再猜类型。
///
/// **密码永不落盘**：调用方必须跳过 <c>type: password</c> 的字段（宿主里已这么做）。
/// 这里不做过滤，是因为该判断属于"界面语义"，放在宿主更合适；但这条约定必须两处都遵守。
/// </summary>
public sealed class LastValuesStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Dictionary<string, string> _values;

    public LastValuesStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _values = Load(path);
    }

    /// <summary>默认位置：<c>%APPDATA%\All Tool\last-values.json</c>。</summary>
    public static LastValuesStore OpenDefault()
    {
        return new LastValuesStore(SettingsDirectory.Resolve("last-values.json"));
    }

    public string? Get(string pluginId, string actionId, string fieldId) =>
        _values.GetValueOrDefault(Key(pluginId, actionId, fieldId));

    /// <summary>值为 null 或空字符串时视为"清掉这一条"，避免存一堆空值。</summary>
    public void Set(string pluginId, string actionId, string fieldId, string? value)
    {
        var key = Key(pluginId, actionId, fieldId);

        if (string.IsNullOrEmpty(value))
        {
            _values.Remove(key);
            return;
        }

        _values[key] = value;
    }

    public int Count => _values.Count;

    /// <summary>写盘。调用方在"一次执行之后"调用一次即可，不必每个字段调一次。</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_values, WriteOptions), new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // 记不住上次的值不该让整个操作失败——这只是便利功能。
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var text = File.ReadAllText(path, Encoding.UTF8);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(text);

            return parsed is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (Exception)
        {
            // 文件损坏/格式不认识时当作空的：宁可丢历史，也不要让宿主起不来。
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string Key(string pluginId, string actionId, string fieldId) =>
        $"{pluginId}/{actionId}/{fieldId}";
}
