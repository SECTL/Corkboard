using Avalonia.Threading;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Services.Config;
using Corkboard.Platforms.Abstractions;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>基础设置页的 ViewModel：直接暴露配置子树，页面双向绑定即可。</summary>
public partial class BasicSettingsPageViewModel(
    MainConfigHandler configHandler,
    IWindowFeatureService windowFeatures,
    IAutostartService autostart) : ViewModelBase(configHandler)
{
    public BasicSettingsConfig Basic => Config.Basic;

    /// <summary>语言下拉项的来源：只有中文和英文，显示名固定用各自的写法。</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(LanguageMode.ChineseSimplified, CR.Settings_Basic_Language_ChineseSimplified),
        new(LanguageMode.English, CR.Settings_Basic_Language_English)
    ];

    /// <summary>下拉选中项。配置里若是越界值（旧版本落盘的日语 2），就退回第一项中文。</summary>
    public LanguageOption SelectedLanguage
    {
        get => LanguageOptions.FirstOrDefault(option => option.Mode == Basic.Language) ?? LanguageOptions[0];
        set
        {
            if (value is null || value.Mode == Basic.Language)
                return;

            Basic.Language = value.Mode;
            // 语言在 XAML 加载前定好，运行期改只能重启才对全部界面生效。
            LanguageChangeRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>语言改动后由页面请求重启（由 <c>SettingsView.RequestRestartApp</c> 在标题栏亮出提示）。</summary>
    public event EventHandler? LanguageChangeRequested;

    /// <summary>
    ///     当前平台是否真的能置底到桌面。骨架里只有 Windows 实现了这个能力，
    ///     其余平台把开关禁用掉，而不是让用户改一个不生效的配置。
    /// </summary>
    public bool IsPinToDesktopSupported =>
        (windowFeatures.SupportedFeatures & WindowFeatures.DesktopBottom) != 0;

    /// <summary>
    ///     当前平台是否实现了开机自启。没实现的平台整张卡隐藏，
    ///     而不是留一个点不动的开关（见 AGENTS.md 的设置硬规矩）。
    /// </summary>
    public bool IsAutostartSupported => autostart.IsSupported;

    /// <summary>当前平台是否能把窗口设成点击穿透（没实现的平台整张卡隐藏）。</summary>
    public bool IsClickThroughSupported =>
        (windowFeatures.SupportedFeatures & WindowFeatures.ClickThrough) != 0;

    /// <summary>当前平台是否能设置窗口不透明度（没实现的平台整张卡隐藏）。</summary>
    public bool IsWindowOpacitySupported =>
        (windowFeatures.SupportedFeatures & WindowFeatures.WindowOpacity) != 0;

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
    ///     页面离开可视树时调用（见 <c>BasicSettingsPage</c> 的 <c>Unloaded</c>），
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

    /// <summary>
    ///     写入开机自启系统集成。失败时把配置回滚并返回失败原因，
    ///     由页面弹提示——绝不把失败当成功落盘。
    /// </summary>
    public bool TryApplyAutostart(bool enabled, out string? error)
    {
        if (autostart.TrySetEnabled(enabled, out error))
            return true;

        Basic.Autostart = !enabled;
        return false;
    }
}
