using Corkboard.Core.Controls;

namespace Corkboard.Core.Tests;

/// <summary>
///     弹层卡片的尺寸上限：高度按「可用高度 × 比例」并托住下限，宽度在设计上限与
///     「可用宽度 − 留边」之间取小。这里用的比例与下限与 `BoardAssignmentForm` 里的策略常量同值，
///     改那边记得改这里。
/// </summary>
public class SheetSizeLimitsTests
{
    private const double Ratio = 0.7;
    private const double MinHeight = 320;
    private const double Margin = 32;
    private const double PreferredWidth = 720;
    private const double MinWidth = 380;

    [Fact]
    public void ResolveMaxHeight_UsesRatioWhenHostIsTall()
    {
        // 宿主 1000 高：最多占七成（700），不是一路长到 968 贴边。
        Assert.Equal(700, SheetSizeLimits.ResolveMaxHeight(1000, Ratio, MinHeight), 9);
        Assert.Equal(560, SheetSizeLimits.ResolveMaxHeight(800, Ratio, MinHeight), 9);
    }

    [Fact]
    public void ResolveMaxHeight_FallsBackToFloorWhenHostIsShort()
    {
        // 宿主矮到「七成」比下限还小时托到下限，别把卡片缩成一条。
        Assert.Equal(MinHeight, SheetSizeLimits.ResolveMaxHeight(400, Ratio, MinHeight));
    }

    [Fact]
    public void ResolveMaxHeight_UnmeasuredHostStillReturnsFloor()
    {
        // 量不到宿主（离屏测试、ClientSize 为 0）时给下限，不给 NaN / 负数。
        Assert.Equal(MinHeight, SheetSizeLimits.ResolveMaxHeight(0, Ratio, MinHeight));
        Assert.Equal(MinHeight, SheetSizeLimits.ResolveMaxHeight(double.NaN, Ratio, MinHeight));
        Assert.Equal(MinHeight, SheetSizeLimits.ResolveMaxHeight(double.PositiveInfinity, Ratio, MinHeight));
    }

    [Fact]
    public void ResolveMaxWidth_KeepsPreferredWidthWhenHostIsWide()
    {
        // 窗口够宽：卡片长到设计上限就停，不会跟着窗口一直变宽。
        Assert.Equal(PreferredWidth, SheetSizeLimits.ResolveMaxWidth(1600, PreferredWidth, MinWidth, Margin));
        Assert.Equal(PreferredWidth, SheetSizeLimits.ResolveMaxWidth(1000, PreferredWidth, MinWidth, Margin));
    }

    [Fact]
    public void ResolveMaxWidth_ShrinksIntoNarrowHost()
    {
        // 窗口 600 宽：可用宽度减掉左右余量，卡片跟着收。
        Assert.Equal(568, SheetSizeLimits.ResolveMaxWidth(600, PreferredWidth, MinWidth, Margin));
    }

    [Fact]
    public void ResolveMaxWidth_FallsBackToFloorAndPreferred()
    {
        // 窗口比下限还窄：保下限（卡片稍微溢出，但不缩到没法用）。
        Assert.Equal(MinWidth, SheetSizeLimits.ResolveMaxWidth(300, PreferredWidth, MinWidth, Margin));

        // 量不到可用宽度：只用设计上限。
        Assert.Equal(PreferredWidth, SheetSizeLimits.ResolveMaxWidth(0, PreferredWidth, MinWidth, Margin));
    }
}
