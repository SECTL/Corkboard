using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Exam;

/// <summary>
///     「看板显示」设置页：标题与副标题、副标题颜色、即将开始的阈值、时钟摆哪边、
///     以及「即将开始 / 进行中」两种形态下要不要收起右侧的考试表。
///     <para>
///         文本与数值都绑草稿，失焦 / 回车 / 离开页面才提交——<see cref="MainConfigHandler" />
///         每次属性变化都落一次盘，双向绑等于每敲一个字符写一遍 <c>settings.json</c>。
///         开关与下拉是离散动作，直接双向绑配置。
///     </para>
/// </summary>
public partial class ExamDisplaySettingsPageViewModel : ViewModelBase
{
    /// <summary>取色器拖动时会连发很多次，攒 400ms 再落盘（与作业板默认颜色同一套做法）。</summary>
    private static readonly TimeSpan ColorCommitDelay = TimeSpan.FromMilliseconds(400);

    private Color? _subtitleColorDraft;
    private DispatcherTimer? _subtitleColorCommitTimer;
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

    #region 标题与副标题

    /// <summary>主标题草稿。留空 = 用默认名（「考试看板」）。</summary>
    [ObservableProperty]
    private string _titleDraft = string.Empty;

    /// <summary>副标题草稿。留空 = 不显示这一行。</summary>
    [ObservableProperty]
    private string _subtitleDraft = string.Empty;

    /// <summary>副标题取色器的草稿。</summary>
    public Color SubtitleColorDraft
    {
        get => _subtitleColorDraft ?? Settings.SubtitleColor ?? ResolveThemeSecondaryColor();
        set
        {
            if (_subtitleColorDraft == value)
                return;

            _subtitleColorDraft = value;
            OnPropertyChanged();
            ScheduleColorCommit(value);
        }
    }

    /// <summary>副标题颜色是否跟随主题（配置里为空就是跟随），对应设置卡上的开关。</summary>
    public bool FollowSubtitleThemeColor
    {
        get => Settings.SubtitleColor is null;
        set
        {
            if (value == FollowSubtitleThemeColor)
                return;

            CancelColorCommit();

            if (value)
            {
                Settings.SubtitleColor = null;
            }
            else
            {
                // 从「跟随主题」切到自定义时，起点取主题当前的次要文字色，
                // 直接给黑色的话深色模式下副标题会糊在背景里。
                _subtitleColorDraft ??= ResolveThemeSecondaryColor();
                Settings.SubtitleColor = _subtitleColorDraft;
                OnPropertyChanged(nameof(SubtitleColorDraft));
            }

            // 取色器只在「自定义」时显示，换档位要通知这一条。
            OnPropertyChanged();
        }
    }

    #endregion

    #region 阈值与形态

    /// <summary>「即将开始」阈值的草稿（分钟）。</summary>
    [ObservableProperty]
    private decimal? _thresholdDraft;

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

    #endregion

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
        CancelColorCommit();
        Settings.PropertyChanged -= OnSettingsChanged;
    }

    /// <summary>把还压在草稿里的文本、数值与颜色立刻写进配置（失焦 / 回车 / 离开页面时调）。</summary>
    public void CommitAll()
    {
        CommitTitle();
        CommitSubtitle();
        CommitThreshold();
        FlushColorDraft();
    }

    private void CommitTitle()
    {
        var value = (TitleDraft ?? string.Empty).Trim();
        if (!string.Equals(Settings.Title, value, StringComparison.Ordinal))
            Settings.Title = value;

        if (!string.Equals(TitleDraft, value, StringComparison.Ordinal))
            TitleDraft = value;
    }

    private void CommitSubtitle()
    {
        var value = (SubtitleDraft ?? string.Empty).Trim();
        if (!string.Equals(Settings.Subtitle, value, StringComparison.Ordinal))
            Settings.Subtitle = value;

        if (!string.Equals(SubtitleDraft, value, StringComparison.Ordinal))
            SubtitleDraft = value;
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

    /// <summary>把还压在草稿里的颜色立刻写进配置，并停掉待触发的防抖计时。</summary>
    public void FlushColorDraft()
    {
        CancelColorCommit();

        if (_subtitleColorDraft is { } draft && !FollowSubtitleThemeColor && Settings.SubtitleColor != draft)
            Settings.SubtitleColor = draft;
    }

    /// <summary>把草稿整体同步成配置里的值（首次进页面、云端恢复设置之后）。</summary>
    private void ResetDrafts()
    {
        TitleDraft = Settings.Title ?? string.Empty;
        SubtitleDraft = Settings.Subtitle ?? string.Empty;
        ThresholdDraft = ExamStatusPolicy.ClampUpcomingMinutes(Settings.UpcomingThresholdMinutes);
        _subtitleColorDraft = Settings.SubtitleColor;
        OnPropertyChanged(nameof(SubtitleColorDraft));
        OnPropertyChanged(nameof(FollowSubtitleThemeColor));
        OnPropertyChanged(nameof(SelectedClockSideOption));
    }

    private void ScheduleColorCommit(Color value)
    {
        CancelColorCommit();

        _subtitleColorCommitTimer = new DispatcherTimer { Interval = ColorCommitDelay };
        _subtitleColorCommitTimer.Tick += (_, _) =>
        {
            CancelColorCommit();

            // 已经切回「跟随主题」了：别再把手里的颜色写回配置。
            if (!FollowSubtitleThemeColor)
                Settings.SubtitleColor = value;
        };
        _subtitleColorCommitTimer.Start();
    }

    private void CancelColorCommit()
    {
        _subtitleColorCommitTimer?.Stop();
        _subtitleColorCommitTimer = null;
    }

    /// <summary>配置被外部改了（云端恢复设置）：把草稿重新对齐，免得界面停在旧值上。</summary>
    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null or nameof(ExamBoardSettingsConfig.Title):
                TitleDraft = Settings.Title ?? string.Empty;
                break;
            case nameof(ExamBoardSettingsConfig.Subtitle):
                SubtitleDraft = Settings.Subtitle ?? string.Empty;
                break;
            case nameof(ExamBoardSettingsConfig.SubtitleColor):
                _subtitleColorDraft = Settings.SubtitleColor;
                OnPropertyChanged(nameof(SubtitleColorDraft));
                OnPropertyChanged(nameof(FollowSubtitleThemeColor));
                break;
            case nameof(ExamBoardSettingsConfig.UpcomingThresholdMinutes):
                ThresholdDraft = ExamStatusPolicy.ClampUpcomingMinutes(Settings.UpcomingThresholdMinutes);
                break;
            case nameof(ExamBoardSettingsConfig.ClockSide):
                OnPropertyChanged(nameof(SelectedClockSideOption));
                break;
        }
    }

    /// <summary>主题里的「次要文字色」——副标题默认就用它（比标题淡一档）。</summary>
    private static Color ResolveThemeSecondaryColor()
    {
        // 条件访问不能带 out 参数，所以先取实例再查资源。
        if (Application.Current is { } application)
        {
            // 必须显式带 ThemeVariant，否则拿到的是亮色字典里的颜色。
            var variant = application.ActualThemeVariant;

            if (application.TryGetResource("TextFillColorSecondaryBrush", variant, out var resource)
                && resource is ISolidColorBrush brush)
            {
                return brush.Color;
            }
        }

        return Colors.Gray;
    }
}

/// <summary>时钟位置下拉项。</summary>
public sealed record ExamClockSideOption(ExamBoardClockSide Side, string DisplayName);
