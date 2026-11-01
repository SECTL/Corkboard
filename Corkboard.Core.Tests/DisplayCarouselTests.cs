using Corkboard.Core.Display;

namespace Corkboard.Core.Tests;

/// <summary>
///     看板模式的自动翻页算式（<see cref="DisplayCarousel.NextOffset" />）。
///     <para>
///     这里钉的是三件事：①一屏放得下时一动不动；②翻页要尽量对齐区块卡片的顶边（别把卡片切一半）；
///     ③每翻一页都有进展、且不会越过「当前偏移 + 一屏」——越过去就意味着两页之间漏掉一条内容带。
///     以前 code-behind 里写死「偏移 + 视口 × 0.9」，落点随机，用户反馈的就是这个。
///     </para>
/// </summary>
public class DisplayCarouselTests
{
    // 一屏 1000，内容 3600，所以最多能滚到 2600。
    private const double Viewport = 1000;
    private const double Extent = 3600;
    private const double MaxOffset = Extent - Viewport;

    [Fact]
    public void NoPageTurn_WhenEverythingFitsOnOneScreen()
    {
        // extent 比 viewport 只大一点点：那是亚像素舍入，不算「超一屏」。
        Assert.Equal(0, DisplayCarousel.NextOffset(0, Viewport, Viewport + 4));
        Assert.Equal(0, DisplayCarousel.NextOffset(0, Viewport, Viewport));
    }

    [Fact]
    public void NoPageTurn_ForADegenerateViewport()
    {
        // 窗口还没量出来 / 内容区被收起来时不该除出个 Infinity 写进 Offset。
        Assert.Equal(0, DisplayCarousel.NextOffset(0, 0, Extent));
        Assert.Equal(0, DisplayCarousel.NextOffset(double.NaN, Viewport, Extent));
    }

    [Fact]
    public void FallsBackToAProportionalPage_WithoutBoundaries()
    {
        Assert.Equal(900, DisplayCarousel.NextOffset(0, Viewport, Extent), 3);
        Assert.Equal(1800, DisplayCarousel.NextOffset(900, Viewport, Extent), 3);
    }

    [Fact]
    public void SnapsToTheLowestBoundaryThatStillFitsThePage()
    {
        // 900 是「按比例该走到的位置」，750 / 880 都在这一屏里，取最靠下的 880：
        // 页面顶端正好对齐一张卡片的顶边，卡片不会被切开。
        double[] boundaries = [0, 240, 750, 880, 1200, 1700];
        Assert.Equal(880, DisplayCarousel.NextOffset(0, Viewport, Extent, boundaries), 3);

        // 第二页：窗口是 880..1780，这一屏里有 1200 与 1700，取 1700。
        Assert.Equal(1700, DisplayCarousel.NextOffset(880, Viewport, Extent, boundaries), 3);
    }

    [Fact]
    public void IgnoresBoundariesThatBarelyMoveThePage()
    {
        // 边界 310 离当前位置太近（不到一屏的 35%），照着它对齐就是「原地挪一下」——
        // 忽略它，按比例走。
        double[] boundaries = [0, 120, 310, 5000];
        Assert.Equal(900, DisplayCarousel.NextOffset(0, Viewport, Extent, boundaries), 3);
    }

    [Fact]
    public void NeverLeavesAGapBetweenTwoPages()
    {
        // 落点必须落在「当前偏移 + 一屏」以内，否则两页之间的那条带子永远看不到。
        double[] boundaries = [0, 300, 700, 940, 1500, 2300];
        var offset = 0d;
        for (var page = 0; page < 20; page++)
        {
            var next = DisplayCarousel.NextOffset(offset, Viewport, Extent, boundaries);
            if (next == 0)
                break;

            Assert.True(next > offset, $"第 {page} 页没有往前走：{offset} → {next}");
            Assert.True(next <= offset + Viewport, $"第 {page} 页跳过了内容：{offset} → {next}");
            offset = next;
        }

        // 最后一定停在「贴底」那一页，不会永远差一截。
        Assert.Equal(MaxOffset, offset, 3);
    }

    [Fact]
    public void LastPageSticksToTheBottom_ThenWrapsToTheTop()
    {
        // 再往前就到头了：直接贴底，让最后那截内容露出来。
        Assert.Equal(MaxOffset, DisplayCarousel.NextOffset(1900, Viewport, Extent), 3);

        // 已经在底上：下一拍回顶部，从头再轮一遍。
        Assert.Equal(0, DisplayCarousel.NextOffset(MaxOffset, Viewport, Extent), 3);
    }

    [Fact]
    public void BoundariesOutsideThePageAreIgnored()
    {
        // 传进来的边界可能来自另一套排布（隐藏的那块 ItemsControl），位置很远——不该被它们带跑。
        double[] boundaries = [0, 2600, 2900, 3400];
        Assert.Equal(900, DisplayCarousel.NextOffset(0, Viewport, Extent, boundaries), 3);
    }
}
