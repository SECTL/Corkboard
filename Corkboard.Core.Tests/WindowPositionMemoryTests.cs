using Avalonia;
using Corkboard.Core.Helpers;

namespace Corkboard.Core.Tests;

/// <summary>
///     主窗口位置记忆的换算：屏幕没变就原样恢复，屏幕尺寸变了按工作区比例等比缩，
///     最后都要夹回可见区（标题栏那角得抓得到）。
/// </summary>
public class WindowPositionMemoryTests
{
    [Fact]
    public void Resolve_KeepsOffsetWhenWorkingAreaUnchanged()
    {
        var area = new PixelRect(0, 0, 2560, 1400);

        var position = WindowPositionMemory.Resolve(300, 180, 2560, 1400, area);

        Assert.Equal(new PixelPoint(300, 180), position);
    }

    [Fact]
    public void Resolve_ScalesOffsetWithWorkingArea()
    {
        // 记下时工作区 1000x500，现在 2000x1000：偏移各放大一倍，再叠上工作区自己的原点。
        var area = new PixelRect(100, 50, 2000, 1000);

        var position = WindowPositionMemory.Resolve(200, 100, 1000, 500, area);

        Assert.Equal(new PixelPoint(500, 250), position);
    }

    [Fact]
    public void Resolve_DoesNotScaleWhenSavedScreenSizeIsMissing()
    {
        var area = new PixelRect(0, 0, 1920, 1080);

        // 老配置里没记屏幕大小（0）：不缩放，按原偏移摆。
        Assert.Equal(new PixelPoint(120, 60), WindowPositionMemory.Resolve(120, 60, 0, 0, area));
    }

    [Fact]
    public void Resolve_ReturnsNullWithoutUsableSourceOrTarget()
    {
        var area = new PixelRect(0, 0, 1920, 1080);

        Assert.Null(WindowPositionMemory.Resolve(WindowPositionMemory.Unset, 0, 1920, 1080, area));
        Assert.Null(WindowPositionMemory.Resolve(0, WindowPositionMemory.Unset, 1920, 1080, area));
        Assert.Null(WindowPositionMemory.Resolve(0, 0, 1920, 1080, default));
    }

    [Fact]
    public void Resolve_RejectsMinimizedPlaceholderOffset()
    {
        // 最小化时 Windows 把窗口挪到 (-32000,-32000)，这个值实测会先经 PositionChanged 写进配置。
        var area = new PixelRect(0, 0, 2560, 1400);

        Assert.Null(WindowPositionMemory.Resolve(-32000, -32000, 2560, 1400, area));
        Assert.Null(WindowPositionMemory.Resolve(-32000, 300, 2560, 1400, area));
        Assert.Null(WindowPositionMemory.Resolve(300, -32000, 2560, 1400, area));

        // 副屏在主屏左侧是合法位置（负偏移），不能被当成不可信数据。
        Assert.Equal(
            new PixelPoint(-1280, 0),
            WindowPositionMemory.Resolve(-1280, 0, 2560, 1400, area));
    }

    [Fact]
    public void IsPlausible_RejectsMinimizedPlaceholder()
    {
        Assert.True(WindowPositionMemory.IsPlausible(new PixelPoint(0, 0)));
        Assert.True(WindowPositionMemory.IsPlausible(new PixelPoint(-2560, 120)));
        Assert.False(WindowPositionMemory.IsPlausible(new PixelPoint(-32000, -32000)));
        Assert.False(WindowPositionMemory.IsPlausible(new PixelPoint(600, -32000)));
        Assert.False(WindowPositionMemory.IsPlausible(new PixelPoint(WindowPositionMemory.Unset, 0)));
    }

    [Fact]
    public void Clamp_KeepsWindowInsideItsScreen()
    {
        var area = new PixelRect(0, 0, 1920, 1080);
        var visible = WindowPositionMemory.VisibleEdge;

        // 跑到右下角外面：夹回「至少留 VisibleEdge 可见」的位置。
        Assert.Equal(
            new PixelPoint(1920 - visible, 1080 - visible),
            WindowPositionMemory.Clamp(new PixelPoint(5000, 5000), area));

        // 跑到左上角外面：夹回工作区原点。
        Assert.Equal(new PixelPoint(0, 0), WindowPositionMemory.Clamp(new PixelPoint(-800, -600), area));

        // 本来就在工作区里：原样不动。
        Assert.Equal(new PixelPoint(640, 360), WindowPositionMemory.Clamp(new PixelPoint(640, 360), area));
    }

    [Fact]
    public void Clamp_FallsBackToOriginOnTinyWorkingArea()
    {
        // 工作区比 VisibleEdge 还小：夹取的下限不能大于上限（Math.Clamp 会抛 ArgumentException）。
        var area = new PixelRect(20, 30, 100, 80);

        Assert.Equal(new PixelPoint(20, 30), WindowPositionMemory.Clamp(new PixelPoint(900, 900), area));
    }

    [Fact]
    public void ToOffset_SubtractsWorkingAreaOrigin()
    {
        var (x, y) = WindowPositionMemory.ToOffset(new PixelPoint(420, 300), new PixelRect(100, 50, 1920, 1080));

        Assert.Equal(320, x);
        Assert.Equal(250, y);
    }
}
