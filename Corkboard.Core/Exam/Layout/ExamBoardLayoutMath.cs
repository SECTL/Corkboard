using Corkboard.Core.Models.SubConfigs.Exam;

namespace Corkboard.Core.Exam.Layout;

/// <summary>
///     百分比 ↔ DIP 换算、12 列水平吸附、1% 垂直吸附、矩形夹取与八手柄缩放。
///     纯函数，不依赖控件，单测直接测数字。
/// </summary>
public static class ExamBoardLayoutMath
{
    /// <summary>任何组件的全局最小尺寸（百分比）。</summary>
    public const double MinSizePercent = 5;

    public static double PercentToDip(double percent, double total) => percent / 100d * total;

    public static double DipToPercent(double dip, double total) =>
        total <= 0 || !double.IsFinite(total) ? 0 : dip / total * 100d;

    /// <summary>水平吸附到 1/12 宽网格。</summary>
    public static double SnapX(double percent) => Math.Round(percent * 12d) / 12d;

    /// <summary>垂直吸附到 1%。</summary>
    public static double SnapY(double percent) => Math.Round(percent);

    /// <summary>把矩形夹进 0–100，并保证最小宽高、X+W / Y+H 不越界。</summary>
    public static ExamBoardLayoutRect ClampRect(
        ExamBoardLayoutRect rect,
        double minWidth = MinSizePercent,
        double minHeight = MinSizePercent)
    {
        var width = double.IsFinite(rect.Width) ? Math.Clamp(rect.Width, minWidth, 100) : 100;
        var height = double.IsFinite(rect.Height) ? Math.Clamp(rect.Height, minHeight, 100) : 100;
        var x = double.IsFinite(rect.X) ? Math.Clamp(rect.X, 0, 100 - width) : 0;
        var y = double.IsFinite(rect.Y) ? Math.Clamp(rect.Y, 0, 100 - height) : 0;
        return new ExamBoardLayoutRect(x, y, width, height);
    }

    /// <summary>
    ///     按当前视口把组件的 DIP 下限换算成百分比下限，再夹取矩形。
    ///     编辑时用；落盘仍是百分比，所以只影响编辑手感，不影响跨分辨率。
    /// </summary>
    public static ExamBoardLayoutRect ClampToView(
        ExamBoardLayoutRect rect,
        double viewWidth,
        double viewHeight,
        ExamBoardLayoutComponentId component)
    {
        var (minWidthDip, minHeightDip) = ExamBoardLayoutDefaults.GetMinimumDip(component);
        var minWidth = Math.Max(MinSizePercent, DipToPercent(minWidthDip, viewWidth));
        var minHeight = Math.Max(MinSizePercent, DipToPercent(minHeightDip, viewHeight));
        return ClampRect(rect, minWidth, minHeight);
    }

    /// <summary>拖 8 个手柄：<paramref name="deltaX" /> / <paramref name="deltaY" /> 为百分比增量。</summary>
    public static ExamBoardLayoutRect Resize(
        ExamBoardLayoutRect start,
        ExamBoardResizeHandle handle,
        double deltaX,
        double deltaY,
        double minWidth = MinSizePercent,
        double minHeight = MinSizePercent)
    {
        var left = start.X;
        var top = start.Y;
        var right = start.Right;
        var bottom = start.Bottom;

        var movingLeft = handle is ExamBoardResizeHandle.TopLeft or ExamBoardResizeHandle.Left
            or ExamBoardResizeHandle.BottomLeft;
        var movingRight = handle is ExamBoardResizeHandle.TopRight or ExamBoardResizeHandle.Right
            or ExamBoardResizeHandle.BottomRight;
        var movingTop = handle is ExamBoardResizeHandle.TopLeft or ExamBoardResizeHandle.Top
            or ExamBoardResizeHandle.TopRight;
        var movingBottom = handle is ExamBoardResizeHandle.BottomLeft or ExamBoardResizeHandle.Bottom
            or ExamBoardResizeHandle.BottomRight;

        if (movingLeft)
            left = Math.Clamp(left + deltaX, 0, right - minWidth);
        if (movingRight)
            right = Math.Clamp(right + deltaX, left + minWidth, 100);
        if (movingTop)
            top = Math.Clamp(top + deltaY, 0, bottom - minHeight);
        if (movingBottom)
            bottom = Math.Clamp(bottom + deltaY, top + minHeight, 100);

        return ClampRect(new ExamBoardLayoutRect(left, top, right - left, bottom - top), minWidth, minHeight);
    }

    /// <summary>按吸附规则吸附 X 与宽度右边界，再夹取。</summary>
    public static ExamBoardLayoutRect SnapMove(double x, double y, double width, double height) =>
        ClampRect(new ExamBoardLayoutRect(SnapX(x), SnapY(y), width, height));

    public static void Apply(ExamBoardLayoutRect rect, ExamBoardComponentLayout layout)
    {
        layout.X = Math.Round(rect.X, 2);
        layout.Y = Math.Round(rect.Y, 2);
        layout.Width = Math.Round(rect.Width, 2);
        layout.Height = Math.Round(rect.Height, 2);
    }
}