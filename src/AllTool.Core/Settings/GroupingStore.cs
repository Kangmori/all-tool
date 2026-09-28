using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllTool.Core.Settings;

/// <summary>
/// 用户对界面分组的调整。
///
/// 两层来源，**清单优先，用户可覆盖**：
///   1. 工具包清单里的 <c>category</c> 是默认分组（作者最懂该怎么归类）；
///   2. 用户把某个条目**拖到别的分组**时，覆盖值记在这里。
///
/// 另外还记"用户自己新建的分组名"——空分组也需要存在，否则新建完一刷新就没了，
/// 用户没有地方可以把条目拖进去。
///
/// 文件格式（<c>%APPDATA%\All Tool\grouping.json</c>）：
/// <code>
/// {
///   "items":  { "pkg/scoop": "包管理", "act/scoop/install": "常用" },
///   "groups": { "pkg": ["我的分组"], "act": ["常用", "诊断"] }
/// }
/// </code>
/// 也兼容早期版本那种"根对象直接就是 items"的扁平格式。
/// </summary>
public sealed class GroupingStore
{
    private const string PackageScope = "pkg";
    private const string ActionScope = "act";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly Dictionary<string, string> _items;
    private readonly Dictionary<string, List<string>> _groups;

    public GroupingStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        (_items, _groups) = Load(path);
    }

    public static GroupingStore OpenDefault()
    {
        return new GroupingStore(SettingsDirectory.Resolve("grouping.json"));
    }

    // ---------------------------------------------------------------- 条目的分组覆盖

    public string? GetPackageGroup(string packageId) =>
        _items.GetValueOrDefault($"pkg/{packageId}");

    public string? GetActionGroup(string packageId, string actionId) =>
        _items.GetValueOrDefault($"act/{packageId}/{actionId}");

    public void SetPackageGroup(string packageId, string? group) =>
        SetItem($"pkg/{packageId}", group);

    public void SetActionGroup(string packageId, string actionId, string? group) =>
        SetItem($"act/{packageId}/{actionId}", group);

    public int Count => _items.Count;

    // ---------------------------------------------------------------- 用户新建的分组

    public IReadOnlyList<string> GetCustomGroups(bool forPackages) =>
        _groups.TryGetValue(forPackages ? PackageScope : ActionScope, out var list)
            ? list
            : [];

    /// <summary>新建一个空分组。名字重复或为空则什么都不做。</summary>
    public void AddCustomGroup(bool forPackages, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var scope = forPackages ? PackageScope : ActionScope;

        if (!_groups.TryGetValue(scope, out var list))
        {
            list = [];
            _groups[scope] = list;
        }

        var trimmed = name.Trim();

        if (!list.Contains(trimmed, StringComparer.CurrentCulture))
        {
            list.Add(trimmed);
        }
    }

    /// <summary>删掉一个用户分组：里面条目的覆盖值一并清掉（它们会回到清单里的默认分组）。</summary>
    public void RemoveCustomGroup(bool forPackages, string name)
    {
        var scope = forPackages ? PackageScope : ActionScope;

        if (_groups.TryGetValue(scope, out var list))
        {
            list.RemoveAll(g => string.Equals(g, name, StringComparison.CurrentCulture));

            if (list.Count == 0)
            {
                _groups.Remove(scope);
            }
        }

        foreach (var key in _items
                     .Where(pair => string.Equals(pair.Value, name, StringComparison.CurrentCulture))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            // 只清掉属于这个作用域的条目
            if (key.StartsWith(scope + "/", StringComparison.Ordinal))
            {
                _items.Remove(key);
            }
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            var payload = new PersistedGroups
            {
                Items = _items.Count == 0 ? null : _items,
                Groups = _groups.Count == 0 ? null : _groups,
            };

            File.WriteAllText(_path, JsonSerializer.Serialize(payload, WriteOptions), new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // 记不住分组只是少了点便利，不该让操作失败。
        }
    }

    private void SetItem(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _items.Remove(key);
            return;
        }

        _items[key] = value.Trim();
    }

    private static (Dictionary<string, string> Items, Dictionary<string, List<string>> Groups) Load(string path)
    {
        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        try
        {
            if (!File.Exists(path))
            {
                return (items, groups);
            }

            var text = File.ReadAllText(path, Encoding.UTF8);

            // 新格式
            var parsed = JsonSerializer.Deserialize<PersistedGroups>(text);

            if (parsed?.Items is not null || parsed?.Groups is not null)
            {
                foreach (var (key, value) in parsed.Items ?? [])
                {
                    items[key] = value;
                }

                foreach (var (key, value) in parsed.Groups ?? [])
                {
                    groups[key] = value;
                }

                return (items, groups);
            }

            // 旧格式：根对象直接就是 键→分组名
            var flat = JsonSerializer.Deserialize<Dictionary<string, string>>(text);

            if (flat is not null)
            {
                foreach (var (key, value) in flat)
                {
                    items[key] = value;
                }
            }
        }
        catch (Exception)
        {
            // 文件坏了就当作没有自定义分组，绝不让宿主起不来。
        }

        return (items, groups);
    }

    private sealed class PersistedGroups
    {
        [JsonPropertyName("items")]
        public Dictionary<string, string>? Items { get; init; }

        [JsonPropertyName("groups")]
        public Dictionary<string, List<string>>? Groups { get; init; }
    }
}
