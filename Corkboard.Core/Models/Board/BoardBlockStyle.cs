namespace Corkboard.Core.Models.Board;

/// <summary>
///     区块排布里「单个区块多大」的约束：设置页的输入框、面板的宽度上下限、读盘时的兜底，
///     三处共用一份，免得三边各写一组数字。
///     <para>用户能改的是最小宽度（决定一行排几块），宽度上限由它推出来。</para>
/// </summary>
public static class BoardBlockStyle
{
    /// <summary>没设过最小宽度时用的宽度（DIP）：1100px 宽的窗口大约排 3 块。</summary>
    public const double DefaultMinItemWidth = 300;

    /// <summary>最小宽度下限：比这更窄就不像「区块」了，换行也会变多。</summary>
    public const double MinMinItemWidth = 220;

    /// <summary>最小宽度上限：再宽下去一屏只剩一块，和单列排布就没区别了。</summary>
    public const double MaxMinItemWidth = 600;

    /// <summary>
    ///     区块宽度上限相对最小宽度的倍数。有下限就必然要有上限：
    ///     窗口很宽、区块又少时若不给上限，余量会全灌给一块，反而更难读。
    /// </summary>
    public const double MaxItemWidthRatio = 1.4;

    /// <summary>把设置里填的最小宽度夹进合法区间。NaN / 无穷这类坏值退回默认值。</summary>
    public static double ClampMinItemWidth(double width)
    {
        return double.IsFinite(width)
            ? Math.Clamp(width, MinMinItemWidth, MaxMinItemWidth)
            : DefaultMinItemWidth;
    }

    /// <summary>由最小宽度推面板的宽度上限（默认 300 时上限是 420，和旧行为一致）。</summary>
    public static double ResolveMaxItemWidth(double minItemWidth)
    {
        return ClampMinItemWidth(minItemWidth) * MaxItemWidthRatio;
    }
}
