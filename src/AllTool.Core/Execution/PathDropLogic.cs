using System.Text;

namespace AllTool.Core.Execution;

/// <summary>
/// 把"拖进来的文件/文件夹"变成字段里的文本。
///
/// 抽成纯函数是为了能单测：拖放本身没法自动化验证，但"拖进来之后文本该变成什么样"可以。
/// 规则：
///   - 多值字段（repeatable / 多行）：每个拖入的路径一行，**追加**到已有内容后面，重复的不再加；
///   - 单值字段：用第一个路径**替换**现有内容（拖多个也只有第一个有意义）。
/// </summary>
public static class PathDropLogic
{
    public static string Apply(string? currentText, IReadOnlyList<string> droppedPaths, bool multiValue)
    {
        ArgumentNullException.ThrowIfNull(droppedPaths);

        var usable = droppedPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();

        if (usable.Count == 0)
        {
            return currentText ?? string.Empty;
        }

        if (!multiValue)
        {
            return usable[0];
        }

        var lines = SplitLines(currentText);

        foreach (var path in usable)
        {
            if (!lines.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(path);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>按行拆分，同时认 \r 与 \n（WinUI 的 TextBox 用 \r 换行）。</summary>
    public static List<string> SplitLines(string? text) =>
        (text ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

    /// <summary>给界面用的一句提示：拖进来的东西该怎么理解。</summary>
    public static string DescribeDrop(IReadOnlyList<string> droppedPaths)
    {
        if (droppedPaths.Count == 0)
        {
            return "没有识别到文件或文件夹";
        }

        var builder = new StringBuilder($"已填入 {droppedPaths.Count} 项：");
        builder.Append(droppedPaths.Count <= 3
            ? string.Join("、", droppedPaths)
            : string.Join("、", droppedPaths.Take(3)) + " …");

        return builder.ToString();
    }
}
