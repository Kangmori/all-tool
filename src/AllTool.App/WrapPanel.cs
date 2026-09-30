using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace AllTool.App;

/// <summary>
/// 会换行的横向排列面板。
///
/// 为什么需要它：WinUI **没有内置 WrapPanel**，而横向 <c>StackPanel</c> 永远不会换行——
/// 首页按类型列工具包按钮时，按钮会一路往右溢出、超出窗口被裁掉（产品负责人实测反馈）。
/// 这里按可用宽度分行排列，窗口变窄时自动折行。
/// </summary>
public sealed class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 6;

    public double VerticalSpacing { get; set; } = 6;

    protected override Size MeasureOverride(Size availableSize)
    {
        var maxWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;

        var lineWidth = 0.0;
        var lineHeight = 0.0;
        var totalWidth = 0.0;
        var totalHeight = 0.0;

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var size = child.DesiredSize;

            if (lineWidth > 0 && lineWidth + HorizontalSpacing + size.Width > maxWidth)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineHeight + VerticalSpacing;
                lineWidth = size.Width;
                lineHeight = size.Height;
            }
            else
            {
                lineWidth += (lineWidth > 0 ? HorizontalSpacing : 0) + size.Width;
                lineHeight = Math.Max(lineHeight, size.Height);
            }
        }

        totalWidth = Math.Max(totalWidth, lineWidth);
        totalHeight += lineHeight;

        return new Size(
            double.IsInfinity(maxWidth) ? totalWidth : Math.Min(totalWidth, maxWidth),
            totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var y = 0.0;
        var lineHeight = 0.0;

        foreach (var child in Children)
        {
            var size = child.DesiredSize;

            if (x > 0 && x + HorizontalSpacing + size.Width > finalSize.Width)
            {
                x = 0;
                y += lineHeight + VerticalSpacing;
                lineHeight = 0;
            }
            else if (x > 0)
            {
                x += HorizontalSpacing;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return finalSize;
    }
}
