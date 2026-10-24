using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
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
///         文本与数值都绑草稿，失焦 / 回车 / 离开页面才提交——<see cref="MainConfigHandler" />
///         每次属性变化都落一次盘，双向绑等于每敲一个字符写一遍 <c>settings.json</c>。
///         开关与下拉是离散动作，直接双向绑配置。
///     </para>
/// </summary>
public partial class ExamDisplaySettingsPageViewModel : ViewModelBase
{
    private bool _isAttached;

    public ExamDisplaySettingsPageViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        ClockSideOptions =
        [
            new(ExamBoardClockSide.Left, CR.Settings_Exam_Display_ClockSide_Left),
            new(ExamBoardClockSide.Right, CR.Settings_Exam_Display_ClockSide_Right)
        ];

        ResetDrafts();
    }

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
        ResetDrafts();
    }

    /// <summary>断开订阅（页面离开可视树时调）。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        Settings.PropertyChanged -= OnSettingsChanged;
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
