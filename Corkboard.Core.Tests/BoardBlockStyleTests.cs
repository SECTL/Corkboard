using Corkboard.Core.Controls;
using Corkboard.Core.Board;

namespace Corkboard.Core.Tests;

/// <summary>
///     区块宽度上限的换算：它决定「区块宽度」这个设置项往哪边走——
///     自适应时上限是基准宽度的 1.4 倍，关掉自适应时上限就是基准宽度（面板于是按固定宽度排）。
/// </summary>
public class BoardBlockStyleTests
{
    [Fact]
    public void ResolveMaxItemWidth_Adaptive_AllowsGrowingUpToTheRatio()
    {
        // 默认 300：自适应时上限 420，也就是加开关之前的旧行为。
        Assert.Equal(300 * BoardBlockStyle.MaxItemWidthRatio, BoardBlockStyle.ResolveMaxItemWidth(300, true), 9);
    }

    [Fact]
    public void ResolveMaxItemWidth_NotAdaptive_PinsEveryBlockToTheBaseWidth()
    {
        // 上限等于基准宽度：AdaptiveGridMetrics 平分出来的宽度会被夹回来，于是每块一样宽。
        Assert.Equal(300, BoardBlockStyle.ResolveMaxItemWidth(300, false), 9);

        var (columns, itemWidth) = AdaptiveGridMetrics.Resolve(1000, 300, 300, 5, 8);

        // 列数只由基准宽度决定（(1000 + 8) / (300 + 8) 向下取整 = 3），每块恰好 300，行尾留空。
        Assert.Equal(3, columns);
        Assert.Equal(300, itemWidth, 9);
    }

    [Fact]
    public void ResolveMaxItemWidth_AdaptiveIsTheDefault()
    {
        // 只传一个参数的调用（老调用点）等价于开着自适应。
        Assert.Equal(
            BoardBlockStyle.ResolveMaxItemWidth(420, true),
            BoardBlockStyle.ResolveMaxItemWidth(420),
            9);
    }

    [Fact]
    public void ResolveMaxItemWidth_ClampsBadValuesBeforeApplyingTheRatio()
    {
        // 手改脏了的 settings.json：NaN 退回默认基准宽度，再按开关算上限。
        Assert.Equal(300 * BoardBlockStyle.MaxItemWidthRatio, BoardBlockStyle.ResolveMaxItemWidth(double.NaN, true), 9);
        Assert.Equal(300, BoardBlockStyle.ResolveMaxItemWidth(double.NaN, false), 9);

        // 越界值先夹进上下限（1000 → 600）再乘倍数。
        Assert.Equal(600 * BoardBlockStyle.MaxItemWidthRatio, BoardBlockStyle.ResolveMaxItemWidth(1000, true), 9);
    }
}
