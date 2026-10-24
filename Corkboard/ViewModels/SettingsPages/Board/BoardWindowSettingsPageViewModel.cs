using Avalonia.Threading;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Services.Config;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>
///     「作业板 → 窗口与收纳」设置页的 ViewModel：主窗口那几项窗口能力（不透明度、置底到桌面、
///     窗口圆角、长按拖动、点击穿透）与收纳（<see cref="Dock" />）。
///     <para>
///         窗口能力这部分是从「基础设置」整块搬过来的（基础设置里不许再放作业板专属项）：
///         能力属性读 <see cref="IWindowFeatureService.SupportedFeatures" />，平台不支持就整张卡隐藏；
///         不透明度滑杆绑草稿 + 400ms 防抖，离开页面时 <see cref="FlushOpacityDraft" /> 落一次盘。
///     </para>
///     <para>
///         收纳那部分是 <see cref="DockSettingsPageViewModel" /> 原样搬过来的（页面绑定加 <c>Dock.</c> 前缀），
///         逻辑不在这里重写。
///     </para>
/// </summary>
public partial class BoardWindowSettingsPageViewModel(
    MainConfigHandler configHandler,
    IWindowFeatureService windowFeatures,
    DockSettingsPageViewModel dock) : ViewModelBase(configHandler)
{
    public BasicSettingsConfig Basic => Config.Basic;

    /// <summary>收纳（浮钮位置 / 偏移 / 大小）的子 VM。</summary>
    public DockSettingsPageViewModel Dock { get; } = dock;

    /// <summary>
    ///     当前平台是否真的能置底到桌面。骨架里只有 Windows 实现了这个能力，
    ///     其余平台整张卡隐藏，而不是让用户改一个不生效的配置。
    /// </summary>
    public bool IsPinToDesktopSupported =>
        (windowFeatures.SupportedFeatures & WindowFeatures.DesktopBottom) != 0;

    /// <summary>当前平台是否能把窗口设成点击穿透（没实现的平台整张卡隐藏）。</summary>
    public bool IsClickThroughSupported =>
        (windowFeatures.SupportedFeatures & WindowFeatures.ClickThrough) != 0;

    /// <summary>当前平台是否能设置窗口不透明度（没实现的平台整张卡隐藏）。</summary>
    public bool IsWindowOpacitySupported =>
        (windowFeatures.SupportedFeatures & WindowFeatures.WindowOpacity) != 0;

    /// <summary>
    ///     当前平台是否能让主窗口四角变圆。主窗口没有原生边框，系统不会替它画圆角，
    ///     只能由平台裁窗口区域实现（见 <c>WindowsWindowFeatureService</c>）；没实现的平台整张卡隐藏。
    /// </summary>
    public bool IsWindowCornersSupported =>
        (windowFeatures.SupportedFeatures & WindowFeatures.RoundedCorners) != 0;

    #region 不透明度滑杆

    /// <summary>
    ///     滑杆的下限：再低窗口就基本看不见了，用户会以为窗口没了（设置窗口还开着，但主窗口「消失」）。
    ///     平台那边还有一道更低的安全下限，兜的是手改配置文件的值。
    /// </summary>
    private const double MinOpacityPercent = 20;

    private const double MaxOpacityPercent = 100;

    /// <summary>拖滑杆时每帧都在变，写盘要攒一攒（<c>ConfigHandlerBase</c> 没有防抖）。</summary>
    private static readonly TimeSpan OpacityCommitDelay = TimeSpan.FromMilliseconds(400);

    private double? _opacityDraft;
    private DispatcherTimer? _opacityCommitTimer;

    public double OpacityPercentMinimum => MinOpacityPercent;

    public double OpacityPercentMaximum => MaxOpacityPercent;

    /// <summary>
    ///     滑杆的草稿值（百分数，20–100）。
    ///     <para>
    ///         与取色器同一个道理：<b>不直接双向绑配置</b>——拖滑杆会连发几十次值变化，
    ///         直接写配置等于拖一次写几十遍 <c>settings.json</c>。所以先进草稿，
    ///         停手 <see cref="OpacityCommitDelay" /> 之后再落到 <c>MainWindowOpacity</c> 上，
    ///         配置一变主窗口那边就重新应用（见 <c>MainWindow.OnBasicSettingsChanged</c>）。
    ///     </para>
    /// </summary>
    public double OpacityPercent
    {
        get => _opacityDraft ?? ToPercent(Basic.MainWindowOpacity);
        set
        {
            var clamped = ClampPercent(value);
            if (Math.Abs(OpacityPercent - clamped) < 0.01)
                return;

            _opacityDraft = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OpacityDisplayText));
            ScheduleOpacityCommit(clamped);
        }
    }

    /// <summary>滑杆旁边的百分比读数。</summary>
    public string OpacityDisplayText => $"{Math.Round(OpacityPercent)}%";

    private void ScheduleOpacityCommit(double percent)
    {
        _opacityCommitTimer?.Stop();

        _opacityCommitTimer = new DispatcherTimer { Interval = OpacityCommitDelay };
        _opacityCommitTimer.Tick += (_, _) =>
        {
            _opacityCommitTimer?.Stop();
            _opacityCommitTimer = null;
            Basic.MainWindowOpacity = ToOpacity(percent);
        };
        _opacityCommitTimer.Start();
    }

    /// <summary>
    ///     把还压在草稿里的百分比立刻写进配置，并停掉待触发的防抖计时。
    ///     页面离开可视树时调用（见 <c>BoardWindowSettingsPage</c> 的 <c>Unloaded</c>），
    ///     免得「拖完滑杆马上关页面」把最后一次拖动丢掉。
    /// </summary>
    public void FlushOpacityDraft()
    {
        _opacityCommitTimer?.Stop();
        _opacityCommitTimer = null;

        if (_opacityDraft is { } draft && Math.Abs(ToPercent(Basic.MainWindowOpacity) - draft) > 0.01)
            Basic.MainWindowOpacity = ToOpacity(draft);
    }

    private static double ToPercent(double opacity) =>
        (double.IsFinite(opacity) ? Math.Clamp(opacity, MinOpacityPercent / 100.0, 1.0) : 1.0) * 100;

    private static double ToOpacity(double percent) => ClampPercent(percent) / 100.0;

    private static double ClampPercent(double percent) =>
        double.IsFinite(percent) ? Math.Clamp(percent, MinOpacityPercent, MaxOpacityPercent) : MaxOpacityPercent;

    #endregion
}
