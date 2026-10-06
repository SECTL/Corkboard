using Corkboard.Core.Controls;

namespace Corkboard.Core.Tests;

/// <summary>
///     弹层卡片拖动范围的夹取：卡片必须留在宿主里，卡片比宿主大时反过来盖住宿主。
///     用例里的数字都取「宿主 800×600、卡片 400×300、居中」这组好算的值。
/// </summary>
public class SheetDragLimitsTests
{
    private const double BaseX = 200;
    private const double BaseY = 150;
    private const double CardWidth = 400;
    private const double CardHeight = 300;
    private const double AreaWidth = 800;
    private const double AreaHeight = 600;

    [Fact]
    public void ClampOffset_KeepsOffsetInsideHost()
    {
        Assert.Equal(50, SheetDragLimits.ClampOffset(50, BaseX, CardWidth, AreaWidth));
        Assert.Equal(-50, SheetDragLimits.ClampOffset(-50, BaseX, CardWidth, AreaWidth));
    }

    [Fact]
    public void ClampOffset_StopsAtHostEdges()
    {
        // 居中时左右各余 200：往右最多 200（贴右边缘），往左最多 -200（贴左边缘）。
        Assert.Equal(200, SheetDragLimits.ClampOffset(500, BaseX, CardWidth, AreaWidth));
        Assert.Equal(-200, SheetDragLimits.ClampOffset(-500, BaseX, CardWidth, AreaWidth));

        Assert.Equal(150, SheetDragLimits.ClampOffset(500, BaseY, CardHeight, AreaHeight));
        Assert.Equal(-150, SheetDragLimits.ClampOffset(-500, BaseY, CardHeight, AreaHeight));
    }

    [Fact]
    public void ClampOffset_LetsBigCardCoverHost()
    {
        // 卡片（1000）比宿主（800）大：这时的合法区间是 [宿主宽 − 卡片宽 − 基准, −基准] = [-400, -200]，
        // 也就是卡片左边缘从「盖到宿主左边缘」滑到「右边缘对齐」的那一小段。
        const double cardWidth = 1000;
        var leftLimit = AreaWidth - cardWidth - BaseX;
        var rightLimit = -BaseX;

        Assert.Equal(leftLimit, SheetDragLimits.ClampOffset(-9999, BaseX, cardWidth, AreaWidth));
        Assert.Equal(rightLimit, SheetDragLimits.ClampOffset(9999, BaseX, cardWidth, AreaWidth));

        // 区间方向由 min/max 兜住：端点前后颠倒时也只会夹，不会抛 ArgumentException。
        Assert.Equal(-300, SheetDragLimits.ClampOffset(-300, BaseX, cardWidth, AreaWidth));
    }

    [Fact]
    public void ClampOffset_ExactFitHostAllowsNoMovement()
    {
        // 卡片与宿主一样大：一点都不能挪。
        Assert.Equal(0, SheetDragLimits.ClampOffset(120, 0, AreaWidth, AreaWidth));
        Assert.Equal(0, SheetDragLimits.ClampOffset(-120, 0, AreaWidth, AreaWidth));
    }

    [Fact]
    public void ClampOffset_UnmeasuredHostBlocksMovement()
    {
        // 宿主还没量出来（ClientSize 为 0）时不许动，而不是让卡片飞出去。
        Assert.Equal(0, SheetDragLimits.ClampOffset(80, 0, 0, 0));
        Assert.Equal(0, SheetDragLimits.ClampOffset(80, double.NaN, CardWidth, AreaWidth));
    }
}
