using System.IO.Compression;
using Swpj.Core.Manifest;

namespace Swpj.Core.Discovery;

/// <summary>安装一个工具包的结果。</summary>
public sealed record PackageInstallResult(
    bool Success,
    string? PackageId,
    string? TargetDirectory,
    string Message,
    string? ReplacedBackup);

/// <summary>卸载一个工具包的结果。</summary>
public sealed record PackageUninstallResult(
    bool Success,
    string Message,
    string? MovedTo);

/// <summary>
/// 工具包的安装与卸载——就是**文件操作**，所以刻意放在 Core 里并且有单测覆盖：
/// 界面上的拖放没法自动化验证，但"把一个目录/压缩包装进 plugins、把 plugins 里的某个包移走"
/// 这件事可以被钉死。
///
/// 三条设计取舍：
///   1. **卸载是"移到 .trash"而不是删掉**。用户的工具包可能是自己辛苦写的清单，
///      一次误点不该让它消失；移到 <c>plugins/.trash/&lt;名字&gt;-&lt;时间戳&gt;/</c> 可以随时拿回来。
///   2. **安装前先试着按清单规范加载一次**。清单本来就有校验器，装进来一个语法错的包
///      只会让界面上多一个报错条目；这里直接拒绝并说明原因更省事。
///   3. **同名包视为升级**：先把旧的移到 .trash，再放新的，并在结果里告诉用户备份在哪。
/// </summary>
public static class PackageInstaller
{
    public const string TrashFolderName = ".trash";

    /// <summary>
    /// 从一个来源安装：可以是**目录**（含 manifest.yaml）、**.zip**（含 manifest.yaml），
    /// 或者**单个清单文件**（.yaml/.yml）。
    /// </summary>
    public static PackageInstallResult Install(string sourcePath, string pluginsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsRoot);

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            return new PackageInstallResult(false, null, null, $"找不到要安装的东西：{sourcePath}", null);
        }

        // zip：先解到临时目录，再按目录安装
        if (File.Exists(sourcePath) && Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var staging = Path.Combine(Path.GetTempPath(), "swpj-install-" + Guid.NewGuid().ToString("N"));

            try
            {
                ZipFile.ExtractToDirectory(sourcePath, staging);
                return InstallDirectory(FindPackageRoot(staging), pluginsRoot);
            }
            catch (Exception ex)
            {
                return new PackageInstallResult(false, null, null, "解压失败：" + ex.Message, null);
            }
            finally
            {
                TryDeleteDirectory(staging);
            }
        }

        // 单个清单文件
        if (File.Exists(sourcePath))
        {
            return InstallManifestFile(sourcePath, pluginsRoot);
        }

        return InstallDirectory(FindPackageRoot(sourcePath), pluginsRoot);
    }

    public static PackageInstallResult InstallDirectory(string sourceDirectory, string pluginsRoot)
    {
        var manifestPath = FindManifest(sourceDirectory);

        if (manifestPath is null)
        {
            return new PackageInstallResult(
                false, null, null,
                $"这个目录里没有清单文件（{string.Join(" / ", ManifestLoader.ManifestFileNames)}）：{sourceDirectory}",
                null);
        }

        ToolManifest manifest;

        try
        {
            manifest = ManifestLoader.LoadFromFile(manifestPath);
        }
        catch (ManifestException ex)
        {
            return new PackageInstallResult(false, null, null, "清单没有通过校验，未安装：" + ex.Message, null);
        }

        var id = manifest.Id;

        if (string.IsNullOrWhiteSpace(id))
        {
            return new PackageInstallResult(false, null, null, "清单里没有 id，无法确定目录名", null);
        }

        Directory.CreateDirectory(pluginsRoot);
        var target = Path.Combine(pluginsRoot, id!);

        // 同名视为升级：旧的先挪到 .trash
        string? backup = null;

        if (Directory.Exists(target))
        {
            backup = MoveToTrash(target, pluginsRoot);
        }

        try
        {
            CopyDirectory(sourceDirectory, target);
        }
        catch (Exception ex)
        {
            return new PackageInstallResult(false, id, target, "复制失败：" + ex.Message, backup);
        }

        var message = backup is null
            ? $"已安装工具包「{manifest.Name}（{id}）」"
            : $"已替换工具包「{manifest.Name}（{id}）」，旧版本备份在 {backup}";

        return new PackageInstallResult(true, id, target, message, backup);
    }

    /// <summary>卸载：把整个工具包目录移到 plugins/.trash/ 下（可恢复，不是删除）。</summary>
    public static PackageUninstallResult Uninstall(string packageDirectory, string pluginsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        if (!Directory.Exists(packageDirectory))
        {
            return new PackageUninstallResult(false, $"目录不存在：{packageDirectory}", null);
        }

        // 只允许卸载 plugins 下面的目录，避免误删别处的东西
        var full = Path.GetFullPath(packageDirectory);
        var root = Path.GetFullPath(pluginsRoot);

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return new PackageUninstallResult(false, $"出于安全，只允许卸载 {root} 下的目录", null);
        }

        try
        {
            var movedTo = MoveToTrash(full, root);

            return new PackageUninstallResult(
                true,
                $"已卸载（移到 {movedTo}，需要时可以拿回来）",
                movedTo);
        }
        catch (Exception ex)
        {
            return new PackageUninstallResult(false, "卸载失败：" + ex.Message, null);
        }
    }

    // ------------------------------------------------------------------ 内部

    private static string MoveToTrash(string directory, string pluginsRoot)
    {
        var trash = Path.Combine(pluginsRoot, TrashFolderName);
        Directory.CreateDirectory(trash);

        var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var destination = Path.Combine(trash, $"{name}-{stamp}");

        // 同一秒内重复操作也不冲突
        var suffix = 1;
        while (Directory.Exists(destination))
        {
            destination = Path.Combine(trash, $"{name}-{stamp}-{++suffix}");
        }

        Directory.Move(directory, destination);

        return destination;
    }

    /// <summary>压缩包里可能多套了一层目录（例如 zip 里是 pkg/manifest.yaml），往下找一层。</summary>
    private static string FindPackageRoot(string directory)
    {
        if (FindManifest(directory) is not null)
        {
            return directory;
        }

        var subdirectories = Directory.GetDirectories(directory);
        var withManifest = subdirectories.Where(d => FindManifest(d) is not null).ToList();

        return withManifest.Count == 1 ? withManifest[0] : directory;
    }

    private static string? FindManifest(string directory) =>
        !Directory.Exists(directory)
            ? null
            : ManifestLoader.ManifestFileNames
                .Select(name => Path.Combine(directory, name))
                .FirstOrDefault(File.Exists);

    private static PackageInstallResult InstallManifestFile(string manifestPath, string pluginsRoot)
    {
        string id;

        try
        {
            id = ManifestLoader.LoadFromFile(manifestPath).Id ?? string.Empty;
        }
        catch (ManifestException ex)
        {
            return new PackageInstallResult(false, null, null, "清单没有通过校验，未安装：" + ex.Message, null);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return new PackageInstallResult(false, null, null, "清单里没有 id，无法确定目录名", null);
        }

        Directory.CreateDirectory(pluginsRoot);
        var targetDirectory = Path.Combine(pluginsRoot, id);
        Directory.CreateDirectory(targetDirectory);

        var target = Path.Combine(targetDirectory, Path.GetFileName(manifestPath));
        File.Copy(manifestPath, target, overwrite: true);

        return new PackageInstallResult(true, id, targetDirectory, $"已安装工具包「{id}」（只含清单文件）", null);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(directory);

            // 不要把回收站或版本库塞进新包里
            if (name is TrashFolderName or ".git")
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(target, name));
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // 临时目录删不掉不影响结果
        }
    }
}
