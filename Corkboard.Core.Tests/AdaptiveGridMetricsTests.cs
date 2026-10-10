using Corkboard.Core.Controls;

namespace Corkboard.Core.Tests;

/// <summary>
///     自适应区块排布的计算：分几列、每块多宽，以及错落排布时每块落在哪。
///     这里用的最小区块宽 / 最大区块宽与页面上的属性默认值同值（见 AdaptiveWrapPanel），改那边记得改这里。
/// </summary>
public class AdaptiveGridMetricsTests
{
    private const double MinItemWidth = 220;
    private const double MaxItemWidth = 420;
    private const double Spacing = 8;

    [Fact]
    public void Resolve_SplitsRowWidthBetweenBlocks()
    {
        // 1000 宽：按最小 220 + 间距 8 算得下 4 块，于是平分可用宽度（(1000 - 3*8) / 4 = 244）。
        var (columns, itemWidth) = AdaptiveGridMetrics.Resolve(1000, MinItemWidth, MaxItemWidth, 5, Spacing);

        Assert.Equal(4, columns);
        Assert.Equal(244, itemWidth, 9);

        // 456 宽：只放得下 2 块，平分后是 224（比最小宽度略宽一点）。
        (columns, itemWidth) = AdaptiveGridMetrics.Resolve(456, MinItemWidth, MaxItemWidth, 3, Spacing);

        Assert.Equal(2, columns);
        Assert.Equal(224, itemWidth, 9);
    }

    [Fact]
    public void Resolve_ReflowsIntoOneColumnWhenHostIsNarrow()
    {
        // 窗口比一块的下限还窄：收成一列，宽度跟可用宽度走，不横向溢出。
        var (columns, itemWidth) = AdaptiveGridMetrics.Resolve(200, MinItemWidth, MaxItemWidth, 3, Spacing);

        Assert.Equal(1, columns);
        Assert.Equal(200, itemWidth, 9);
    }

    [Fact]
    public void Resolve_CapsBlockWidthWhenThereAreFewBlocks()
    {
        // 1600 宽、只有 2 块：平分下来 796 太宽，用上限 420 兜住（宁可右边空着）。
        var (columns, itemWidth) = AdaptiveGridMetrics.Resolve(1600, MinItemWidth, MaxItemWidth, 2, Spacing);

        Assert.Equal(2, columns);
        Assert.Equal(MaxItemWidth, itemWidth);

        // 只有 1 块时也一样：不把整个窗口宽度灌给一块。
        (columns, itemWidth) = AdaptiveGridMetrics.Resolve(1600, MinItemWidth, MaxItemWidth, 1, Spacing);

        Assert.Equal(1, columns);
        Assert.Equal(MaxItemWidth, itemWidth);
    }

    [Fact]
    public void Resolve_UnmeasuredHostFallsBackToMinWidth()
    {
        // 还没量到宽度 / 宿主不给约束：先按最小宽度报一块，别报 0 或 NaN。
        var (columns, itemWidth) = AdaptiveGridMetrics.Resolve(0, MinItemWidth, MaxItemWidth, 3, Spacing);
        Assert.Equal(1, columns);
        Assert.Equal(MinItemWidth, itemWidth);

        (columns, itemWidth) = AdaptiveGridMetrics.Resolve(double.NaN, MinItemWidth, MaxItemWidth, 3, Spacing);
        Assert.Equal(1, columns);
        Assert.Equal(MinItemWidth, itemWidth);
    }

    [Fact]
    public void Resolve_InfiniteWidthPutsEverythingInOneRow()
    {
        // 横向无限（外层不收宽度）：一行排完，宽度取下限。
        var (columns, itemWidth) = AdaptiveGridMetrics.Resolve(
            double.PositiveInfinity, MinItemWidth, MaxItemWidth, 3, Spacing);

        Assert.Equal(3, columns);
        Assert.Equal(MinItemWidth, itemWidth);
    }

    [Fact]
    public void Resolve_ReportsNoColumnsWithoutItems()
    {
        var (columns, itemWidth) = AdaptiveGridMetrics.Resolve(1000, MinItemWidth, MaxItemWidth, 0, Spacing);

        Assert.Equal(0, columns);
        Assert.Equal(MinItemWidth, itemWidth);
    }

    [Fact]
    public void Resolve_ToleratesDirtyLimits()
    {
        // 下限写成 0、上限比下限还小、间距是负数：都不该算出 0 宽或负宽。
        var (columns, itemWidth) = AdaptiveGridMetrics.Resolve(1000, 0, 100, 3, -5);

        Assert.Equal(3, columns);
        Assert.Equal(100, itemWidth, 9);
    }

    [Fact]
    public void ResolveMasonry_PutsTheFirstBlocksLeftToRight()
    {
        // 三列、四块：前三块都贴顶、按左到右落，第四块回到（此时最矮的）第一列、接在它下面。
        var placements = AdaptiveGridMetrics.ResolveMasonry([100, 100, 100, 50], 3, 8);

        Assert.Equal((0, 0d), placements[0]);
        Assert.Equal((1, 0d), placements[1]);
        Assert.Equal((2, 0d), placements[2]);
        Assert.Equal((0, 108d), placements[3]);
    }

    [Fact]
    public void ResolveMasonry_FillsTheGapLeftByAShorterBlock()
    {
        double[] heights = [100, 40, 100, 60];

        // 第二块只有 40 高：第四块补到它下面（40 + 8 = 48），不去等第一行最高的那块。
        var placements = AdaptiveGridMetrics.ResolveMasonry(heights, 3, 8);

        Assert.Equal((1, 48d), placements[3]);

        // 内容总高度＝最深那列的底部（108），而不是按行对齐的 100 + 8 + 60。
        var bottom = 0d;
        for (var index = 0; index < placements.Length; index++)
            bottom = Math.Max(bottom, placements[index].Y + heights[index]);

        Assert.Equal(108d, bottom);
    }

    [Fact]
    public void ResolveMasonry_KeepsLeftColumnOnEqualBottoms()
    {
        // 三列都空：先落满一排，再放第四块时三列一样高，靠左优先。
        var placements = AdaptiveGridMetrics.ResolveMasonry([80, 80, 80, 80], 3, 10);

        Assert.Equal((0, 90d), placements[3]);
    }

    [Fact]
    public void ResolveMasonry_HandlesDirtyInput()
    {
        // 一块都没有：不落位。
        Assert.Empty(AdaptiveGridMetrics.ResolveMasonry([], 3, 8));
        Assert.Empty(AdaptiveGridMetrics.ResolveMasonry(null, 3, 8));

        // 列数 0、行距是负数 / NaN、高度是 NaN：都当干净输入处理，不能算出 NaN 坐标。
        var placements = AdaptiveGridMetrics.ResolveMasonry([double.NaN, -5, 30], 0, double.NaN);

        Assert.Equal((0, 0d), placements[0]);
        Assert.Equal((0, 0d), placements[1]);
        Assert.Equal((0, 0d), placements[2]);
    }

    [Fact]
    public void ResolveMasonry_CapsColumnsAtBlockCount()
    {
        // 两块却给了五列：只在两列里落位，不把块排到空气里。
        var placements = AdaptiveGridMetrics.ResolveMasonry([60, 40], 5, 8);

        Assert.Equal(2, placements.Length);
        Assert.Equal((0, 0d), placements[0]);
        Assert.Equal((1, 0d), placements[1]);
    }
}
