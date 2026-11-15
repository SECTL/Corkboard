using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;
using Corkboard.Core.Exam.Theme;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Exam;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Exam;

/// <summary>
///     「看板显示」设置页：即将开始的阈值、时钟摆哪边，以及「即将开始 / 进行中」两种形态下
///     要不要收起右侧的考试表。
///     <para>
///         这些都是**跨看板**的显示偏好，所以留在 <c>settings.json</c> 里；
///         标题、副标题与考试安排跟着看板文件走，在「考试安排」页里编辑。
///     </para>
///     <para>
///         **看板主题的来源也在这一页**（背景分区下面那张下拉卡）：背景到底透哪套底色，
///         取决于这里挑的是「跟随应用外观 / 某套预设 / 自定义」。13 项具体颜色仍在「看板外观」页，
///         两处以同一份 <see cref="ExamBoardThemeConfig" /> 为准，别各存一份。
///     </para>
///     <para>
///         文本与数值都绑草稿，失焦 / 回车 / 离开页面才提交——<see cref="MainConfigHandler" />
///         每次属性变化都落一次盘，双向绑等于每敲一个字符写一遍 <c>settings.json</c>。
///         开关与下拉是离散动作，直接双向绑配置。
///     </para>
/// </summary>
public partial class ExamDisplaySettingsPageViewModel : ViewModelBase
{
    private readonly MainConfigHandler _configHandler;
    private readonly ExamBoardThemeService _themeService;
    private ExamBoardThemeConfig? _observedTheme;
    private bool _isAttached;

    public ExamDisplaySettingsPageViewModel(MainConfigHandler configHandler, ExamBoardThemeService themeService)
        : base(configHandler)
    {
        _configHandler = configHandler;
        _themeService = themeService;

        ClockSideOptions =
        [
            new(ExamBoardClockSide.Left, CR.Settings_Exam_Display_ClockSide_Left),
            new(ExamBoardClockSide.Right, CR.Settings_Exam_Display_ClockSide_Right)
        ];

        ThemeChoices = BuildThemeChoices();

        ResetDrafts();
    }

    /// <summary>背景用的那套主题：跟随应用外观 / 5 套预设 / 自定义。顺序即下拉顺序。</summary>
    public IReadOnlyList<ExamBoardThemeChoice> ThemeChoices { get; }

    /// <summary>
    ///     「看板主题」下拉的选中项。写配置时只落 <c>Source</c> / <c>PresetId</c>，
    ///     自定义覆盖色一项都不动（那些在「看板外观」页里改）。
    /// </summary>
    public ExamBoardThemeChoice SelectedThemeChoice
    {
        get
        {
            var theme = CurrentTheme;
            return ThemeChoices.FirstOrDefault(choice => choice.Matches(theme.Source, theme.PresetId))
                   ?? ThemeChoices[0];
        }
        set
        {
            if (value is null)
                return;

            var theme = CurrentTheme;
            if (value.Matches(theme.Source, theme.PresetId))
                return;

            theme.Source = value.Source;
            if (value.Source == ExamBoardThemeSource.Preset && value.PresetId is { } presetId)
                theme.PresetId = presetId;

            // 看板窗口靠这个服务换资源字典，写完必须让它重算一次颜色。
            _themeService.Refresh();
            OnPropertyChanged();
        }
    }

    private ExamBoardThemeConfig CurrentTheme => _configHandler.Data.ExamBoardTheme;

    private static IReadOnlyList<ExamBoardThemeChoice> BuildThemeChoices()
    {
        var choices = new List<ExamBoardThemeChoice>
        {
            new(ExamBoardThemeSource.FollowApp, null,
                CR.Settings_Exam_Theme_Source_FollowApp)
        };

        foreach (var preset in ExamBoardThemePresets.All)
            choices.Add(new ExamBoardThemeChoice(ExamBoardThemeSource.Preset, preset.Id, Text(preset.NameKey)));

        choices.Add(new ExamBoardThemeChoice(ExamBoardThemeSource.Custom, null,
            CR.Settings_Exam_Theme_Source_Custom));

        return choices;
    }

    /// <summary>按资源键现取文案：Core 层只给键，不依赖界面资源类。</summary>
    private static string Text(string key) => CR.ResourceManager.GetString(key, CR.Culture) ?? key;

    public ExamBoardSettingsConfig Settings => Config.ExamBoard;

    /// <summary>「即将开始」阈值的草稿（分钟）。</summary>
    [ObservableProperty]
    private decimal? _thresholdDraft;

    /// <summary>「剩余时间变红」门槛的草稿（分钟，0 = 不提醒）。</summary>
    [ObservableProperty]
    private decimal? _warningDraft;

    /// <summary>时钟摆左边还是右边。</summary>
    public IReadOnlyList<ExamClockSideOption> ClockSideOptions { get; }

    public ExamClockSideOption SelectedClockSideOption
    {
        get => ClockSideOptions.FirstOrDefault(option => option.Side == Settings.ClockSide) ?? ClockSideOptions[0];
        set
        {
            if (value is null || Settings.ClockSide == value.Side)
                return;

            Settings.ClockSide = value.Side;
            OnPropertyChanged();
        }
    }

    /// <summary>挂上配置变化订阅（页面进入可视树时调）。重复调用不会重复挂。</summary>
    public void Attach()
    {
        // 页面是 transient、配置是单例：订阅必须成对，见 Detach。
        if (_isAttached)
            return;

        _isAttached = true;
        Settings.PropertyChanged += OnSettingsChanged;
        AttachTheme();
        ResetDrafts();
    }

    /// <summary>断开订阅（页面离开可视树时调）。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        Settings.PropertyChanged -= OnSettingsChanged;
        DetachTheme();
    }

    /// <summary>主题配置在「看板外观」页里也会被改，这里跟着刷新下拉的选中项。</summary>
    private void AttachTheme()
    {
        DetachTheme();

        _observedTheme = CurrentTheme;
        _observedTheme.PropertyChanged += OnThemeChanged;
    }

    private void DetachTheme()
    {
        if (_observedTheme is not null)
            _observedTheme.PropertyChanged -= OnThemeChanged;

        _observedTheme = null;
    }

    private void OnThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExamBoardThemeConfig.Source)
            or nameof(ExamBoardThemeConfig.PresetId) or null)
        {
            OnPropertyChanged(nameof(SelectedThemeChoice));
        }
    }

    /// <summary>把还压在草稿里的数值立刻写进配置（失焦 / 回车 / 离开页面时调）。</summary>
    public void CommitAll()
    {
        CommitThreshold();
        CommitWarning();
    }

    private void CommitThreshold()
    {
        var value = ThresholdDraft is { } draft
            ? ExamStatusPolicy.ClampUpcomingMinutes((int)draft)
            : ExamStatusPolicy.ClampUpcomingMinutes(Settings.UpcomingThresholdMinutes);

        if (Settings.UpcomingThresholdMinutes != value)
            Settings.UpcomingThresholdMinutes = value;

        var normalized = (decimal)value;
        if (ThresholdDraft != normalized)
            ThresholdDraft = normalized;
    }

    private void CommitWarning()
    {
        var value = WarningDraft is { } draft
            ? ExamStatusPolicy.ClampRemainingWarningMinutes((int)draft)
            : ExamStatusPolicy.ClampRemainingWarningMinutes(Settings.RemainingWarningMinutes);

        if (Settings.RemainingWarningMinutes != value)
            Settings.RemainingWarningMinutes = value;

        var normalized = (decimal)value;
        if (WarningDraft != normalized)
            WarningDraft = normalized;
    }

    /// <summary>把草稿整体同步成配置里的值（首次进页面、云端恢复设置之后）。</summary>
    private void ResetDrafts()
    {
        ThresholdDraft = ExamStatusPolicy.ClampUpcomingMinutes(Settings.UpcomingThresholdMinutes);
        WarningDraft = ExamStatusPolicy.ClampRemainingWarningMinutes(Settings.RemainingWarningMinutes);
        OnPropertyChanged(nameof(SelectedClockSideOption));
    }

    /// <summary>配置被外部改了（云端恢复设置）：把草稿重新对齐，免得界面停在旧值上。</summary>
    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null or nameof(ExamBoardSettingsConfig.UpcomingThresholdMinutes):
                ThresholdDraft = ExamStatusPolicy.ClampUpcomingMinutes(Settings.UpcomingThresholdMinutes);
                break;
            case nameof(ExamBoardSettingsConfig.RemainingWarningMinutes):
                WarningDraft = ExamStatusPolicy.ClampRemainingWarningMinutes(Settings.RemainingWarningMinutes);
                break;
            case nameof(ExamBoardSettingsConfig.ClockSide):
                OnPropertyChanged(nameof(SelectedClockSideOption));
                break;
        }
    }
}

/// <summary>时钟位置下拉项。</summary>
public sealed record ExamClockSideOption(ExamBoardClockSide Side, string DisplayName);

/// <summary>
///     「看板主题」下拉的一行：跟随应用外观 / 某套预设 / 自定义。
///     <para>
///         <c>Source</c> 与 <c>PresetId</c> 与 <see cref="ExamBoardThemeConfig" /> 完全一致——
///         这一页只是把「选哪套主题」搬到了背景下面，没有第二份数据。
///     </para>
/// </summary>
public sealed record ExamBoardThemeChoice(ExamBoardThemeSource Source, string? PresetId, string DisplayName)
{
    /// <summary>这一行是不是就是配置里现在那一档（预设还要比 <c>PresetId</c>）。</summary>
    public bool Matches(ExamBoardThemeSource source, string? presetId) =>
        Source == source
        && (Source != ExamBoardThemeSource.Preset || string.Equals(PresetId, presetId, StringComparison.Ordinal));
}
