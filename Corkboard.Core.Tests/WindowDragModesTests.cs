using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Helpers;
using Corkboard.Core.Models.SubConfigs.General;
using Xunit;

namespace Corkboard.Core.Tests;

/// <summary>
/// 窗口拖动三档（长按任意位置 / 只能拖标题栏 / 固定窗口）与两个落盘布尔值之间的折算。
/// 这里钉的是「设置页下拉框」和「主界面拖动判定」共用的那一份换算：
/// 两边各判一套就会出现「下拉框显示固定、窗口还能拖」。
/// </summary>
public class WindowDragModesTests
{
    [Theory]
    [InlineData(true, false, WindowDragMode.HoldAnywhere)]
    [InlineData(false, false, WindowDragMode.TitleBarOnly)]
    [InlineData(true, true, WindowDragMode.Fixed)]
    // 固定优先：固定开着时「长按拖动」写什么都不影响结果。
    [InlineData(false, true, WindowDragMode.Fixed)]
    public void FromConfigMapsTwoBooleansToThreeModes(bool hold, bool fixedWindow, WindowDragMode expected) =>
        Assert.Equal(expected, WindowDragModes.FromConfig(hold, fixedWindow));

    [Theory]
    [InlineData(WindowDragMode.HoldAnywhere, true, false)]
    [InlineData(WindowDragMode.TitleBarOnly, false, false)]
    [InlineData(WindowDragMode.Fixed, true, true)]
    public void ApplyToWritesBothBooleans(WindowDragMode mode, bool expectedHold, bool expectedFixed)
    {
        var basic = new BasicSettingsConfig();
        WindowDragModes.ApplyTo(basic, mode);

        Assert.Equal(expectedHold, basic.HoldToDragWindow);
        Assert.Equal(expectedFixed, basic.WindowFixed);
        Assert.Equal(mode, WindowDragModes.FromConfig(basic.HoldToDragWindow, basic.WindowFixed));
    }

    [Fact]
    public void ApplyToFixedKeepsTheHoldToDragPreference()
    {
        // 固定档压在长按拖动上面：切到固定再切回来，还是原来那一档。
        var basic = new BasicSettingsConfig { HoldToDragWindow = false };

        WindowDragModes.ApplyTo(basic, WindowDragMode.Fixed);
        Assert.True(basic.WindowFixed);
        Assert.False(basic.HoldToDragWindow);

        WindowDragModes.ApplyTo(basic, WindowDragMode.TitleBarOnly);
        Assert.False(basic.WindowFixed);
        Assert.False(basic.HoldToDragWindow);
        Assert.Equal(WindowDragMode.TitleBarOnly, WindowDragModes.FromConfig(basic.HoldToDragWindow, basic.WindowFixed));
    }

    [Fact]
    public void ApplyToOutOfRangeModeFallsBackToDefault()
    {
        var basic = new BasicSettingsConfig { WindowFixed = true, HoldToDragWindow = false };
        WindowDragModes.ApplyTo(basic, (WindowDragMode)99);

        Assert.False(basic.WindowFixed);
        Assert.True(basic.HoldToDragWindow);
    }

    [Fact]
    public void OutOfRangeModeFallsBackToDefault()
    {
        Assert.Equal(WindowDragModes.Default, WindowDragModes.Normalize((WindowDragMode)99));
        Assert.Equal(WindowDragMode.HoldAnywhere, WindowDragModes.Default);
    }

    [Fact]
    public void OnlyFixedBlocksDragAndResize()
    {
        Assert.True(WindowDragModes.AllowsDrag(WindowDragMode.HoldAnywhere));
        Assert.True(WindowDragModes.AllowsDrag(WindowDragMode.TitleBarOnly));
        Assert.False(WindowDragModes.AllowsDrag(WindowDragMode.Fixed));

        Assert.True(WindowDragModes.AllowsResize(WindowDragMode.HoldAnywhere));
        Assert.True(WindowDragModes.AllowsResize(WindowDragMode.TitleBarOnly));
        Assert.False(WindowDragModes.AllowsResize(WindowDragMode.Fixed));

        // 只有第一档能「按住任意位置」拖；只能拖标题栏时，空白处按住不该启动拖动。
        Assert.True(WindowDragModes.AllowsHoldAnywhere(WindowDragMode.HoldAnywhere));
        Assert.False(WindowDragModes.AllowsHoldAnywhere(WindowDragMode.TitleBarOnly));
        Assert.False(WindowDragModes.AllowsHoldAnywhere(WindowDragMode.Fixed));
    }

    [Fact]
    public void LockModeDefaultsToClickThrough()
    {
        // 默认值必须与加这个配置之前那颗锁的行为一致，否则老用户升上来会觉得按钮「变了」。
        Assert.Equal(WindowLockMode.ClickThrough, default(WindowLockMode));
        Assert.Equal(WindowLockMode.ClickThrough, new BasicSettingsConfig().WindowLockMode);
    }
}
