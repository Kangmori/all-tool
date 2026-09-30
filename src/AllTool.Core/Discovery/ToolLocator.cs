using System.Text.RegularExpressions;
using AllTool.Core.Execution;
using AllTool.Core.Manifest;

namespace AllTool.Core.Discovery;

/// <summary>定位结果。</summary>
public sealed record ToolLocation(
    string ExecutablePath,
    string? Version,
    bool MeetsMinimumVersion,
    string? VersionOutput,
    string? Problem);

/// <summary>
/// 找到工具包描述的那个可执行文件，并取出它的版本。
///
/// 刻意**不返回 .ps1**：PowerShell 脚本不能直接被 CreateProcess 启动
/// （需要 <c>pwsh -File x.ps1</c>）。工具包若要调用 .ps1，应当在 locate 里写清
/// 真正的可执行文件（例如 scoop 的 scoop.cmd），或把启动器写进 commandArgs。
/// </summary>
public static class ToolLocator
{
    /// <summary>Windows 上可以直接由 CreateProcess 启动的扩展名。</summary>
    private static readonly string[] DirectlyRunnableExtensions = [".exe", ".cmd", ".bat", ".com"];

    /// <summary>在 PATH 与 locate.searchPaths 里查找可执行文件。找不到返回 null。</summary>
    public static string? FindExecutable(LocateSpec locate)
    {
        ArgumentNullException.ThrowIfNull(locate);

        if (string.IsNullOrWhiteSpace(locate.Executable))
        {
            return null;
        }

        var directories = CandidateDirectories(locate);
        var names = new List<string>();

        foreach (var name in new[] { locate.Executable }.Concat(locate.AlternativeNames ?? []))
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            // 已经带扩展名的就原样试；否则依次试可执行的扩展名。
            if (Path.HasExtension(name))
            {
                names.Add(name);
                continue;
            }

            names.AddRange(DirectlyRunnableExtensions.Select(ext => name + ext));
        }

        foreach (var directory in directories)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>查找 + 取版本 + 与 minVersion 比对。</summary>
    public static async Task<ToolLocation?> LocateAsync(
        LocateSpec locate,
        ProcessRunner runner,
        string? executableOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runner);

        // 动作级可执行文件覆盖（见 ManifestAction.Executable）：只换名字，搜索路径与版本检查沿用包级。
        var effective = string.IsNullOrWhiteSpace(executableOverride)
            ? locate
            : locate with { Executable = executableOverride.Trim(), AlternativeNames = null };

        var path = FindExecutable(effective);
        if (path is null)
        {
            return null;
        }

        var encoding = EncodingResolver.DefaultForConsoleApps();
        ProcessRunResult run;

        try
        {
            run = await runner.RunAsync(
                new ProcessRunRequest
                {
                    Executable = path,
                    Arguments = locate.VersionArgs ?? [],
                    OutputEncoding = encoding,
                    Timeout = TimeSpan.FromSeconds(15),
                },
                progress: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ToolLocation(path, null, false, null, $"取版本时无法启动：{ex.Message}");
        }

        var output = string.Join('\n', run.Lines.Select(l => l.Text));
        var version = ExtractVersion(output, locate.VersionPattern);
        var meets = version is null || locate.MinVersion is null
            ? version is not null || locate.MinVersion is null
            : VersionComparison.IsAtLeast(version, locate.MinVersion);

        string? problem = null;
        if (version is null)
        {
            problem = locate.VersionPattern is { Length: > 0 }
                ? $"取到了版本输出，但 versionPattern 没匹配上：{Truncate(output, 200)}"
                : "未能解析出版本号";
        }
        else if (!meets)
        {
            problem = $"版本过低：需要 {locate.MinVersion} 以上，实际 {version}";
        }

        return new ToolLocation(path, version, meets, output, problem);
    }

    /// <summary>用清单里的正则从版本输出里取版本号（按多行语义，第一个捕获组）。</summary>
    public static string? ExtractVersion(string? output, string? pattern)
    {
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        if (string.IsNullOrEmpty(pattern))
        {
            // 没有给正则：取第一段看起来像版本号的数字。
            var guess = Regex.Match(output, "[0-9]+(?:\\.[0-9]+)+", RegexOptions.Multiline);
            return guess.Success ? guess.Value : null;
        }

        var match = Regex.Match(output, pattern, RegexOptions.Multiline);
        return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value : null;
    }

    private static IEnumerable<string> CandidateDirectories(LocateSpec locate)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (AddIfUsable(directory))
            {
                yield return directory;
            }
        }

        foreach (var raw in locate.SearchPaths ?? [])
        {
            var expanded = Environment.ExpandEnvironmentVariables(raw);
            if (AddIfUsable(expanded))
            {
                yield return expanded;
            }
        }

        bool AddIfUsable(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !seen.Add(directory))
            {
                return false;
            }

            return Directory.Exists(directory);
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}

/// <summary>点分版本的逐段数值比较（"1.10" &gt; "1.9"，字符串比较会搞反）。</summary>
public static class VersionComparison
{
    public static int Compare(string? left, string? right)
    {
        var a = Segments(left);
        var b = Segments(right);
        var length = Math.Max(a.Count, b.Count);

        for (var i = 0; i < length; i++)
        {
            var x = i < a.Count ? a[i] : 0;
            var y = i < b.Count ? b[i] : 0;
            var comparison = x.CompareTo(y);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    public static bool IsAtLeast(string? actual, string? minimum) =>
        Compare(actual, minimum) >= 0;

    private static List<int> Segments(string? version)
    {
        var segments = new List<int>();
        if (string.IsNullOrWhiteSpace(version))
        {
            return segments;
        }

        foreach (Match match in Regex.Matches(version, "[0-9]+"))
        {
            segments.Add(int.TryParse(match.Value, out var value) ? value : 0);
        }

        return segments;
    }
}
