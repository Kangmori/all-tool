using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Swpj.Core.Manifest;

/// <summary>
/// 读取并校验工具包清单。
///
/// 有意**不**调用 <c>IgnoreUnmatchedProperties()</c>：这样 YAML 里拼错的键会直接报错，
/// 相当于在运行时也享受 schema 里 <c>additionalProperties: false</c> 的保护。
/// 静默忽略未知键是很危险的——作者以为写了某个参数，实际上什么都没发生。
/// </summary>
public static class ManifestLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>清单文件名，按优先级。</summary>
    public static readonly string[] ManifestFileNames = ["manifest.yaml", "manifest.yml"];

    /// <summary>从工具包目录加载（目录里必须有 manifest.yaml 或 manifest.yml）。</summary>
    public static ToolManifest LoadFromPluginDirectory(string pluginDirectory)
    {
        foreach (var fileName in ManifestFileNames)
        {
            var candidate = Path.Combine(pluginDirectory, fileName);
            if (File.Exists(candidate))
            {
                return LoadFromFile(candidate);
            }
        }

        throw new ManifestException($"目录里找不到清单文件（{string.Join(" / ", ManifestFileNames)}）：{pluginDirectory}");
    }

    /// <summary>从文件加载并校验。</summary>
    public static ToolManifest LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new ManifestException($"清单文件不存在：{path}");
        }

        return Load(File.ReadAllText(path), path);
    }

    /// <summary>从 YAML 文本加载并校验。</summary>
    public static ToolManifest Load(string yaml, string? origin = null)
    {
        var where = origin is null ? string.Empty : $"（{origin}）";

        ToolManifest manifest;
        try
        {
            manifest = Deserializer.Deserialize<ToolManifest>(yaml)
                       ?? throw new ManifestException($"清单内容为空{where}");
        }
        catch (YamlException ex)
        {
            // YamlException 的默认消息不告诉你是哪个键写错了，这里补上行列位置。
            throw new ManifestException(
                $"YAML 解析失败{where} 第 {ex.Start.Line} 行第 {ex.Start.Column} 列：{ex.Message}", ex);
        }

        var errors = ManifestValidation.Validate(manifest);
        if (errors.Count > 0)
        {
            throw new ManifestException(origin ?? "<内存中的 YAML>", errors);
        }

        return manifest;
    }

    /// <summary>扫描一个 plugins 根目录下的所有工具包。</summary>
    public static IReadOnlyList<(string Directory, ToolManifest Manifest)> LoadAll(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot))
        {
            return [];
        }

        var results = new List<(string, ToolManifest)>();
        foreach (var directory in Directory.EnumerateDirectories(pluginsRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (ManifestFileNames.Any(name => File.Exists(Path.Combine(directory, name))))
            {
                results.Add((directory, LoadFromPluginDirectory(directory)));
            }
        }

        return results;
    }
}
