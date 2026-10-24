using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Exam;

/// <summary>
///     「自动放映」设置页：提前几分钟弹、错过开考要不要补弹、最后一场考完多久收，
///     外加一块状态卡说明「下一次放映是什么时候」与「立即放映」。
///     <para>
///         每块看板自己带一个「自动放映」开关（在「考试安排」页的看板列表里），
///         这里只放**全局**的那几档参数——同一台机器上所有看板共用一套提前量与延迟。
///     </para>
///     <para>
///         本页由键控 DI 以 transient 创建，而 <see cref="IExamService" /> 与
///         <see cref="NtpTimeService" /> 是单例：订阅了就必须在页面离开可视树时断开
///         （<see cref="Attach" /> / <see cref="Detach" /> 由页面 code-behind 调）。
///     </para>
/// </summary>
public partial class ExamAutoShowSettingsPageViewModel : ViewModelBase
{
    /// <summary>状态卡要跟着时间走（「现在就该放映了」会自己变），15 秒刷一次足够。</summary>
    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly IExamService _examService;
    private readonly NtpTimeService _timeService;
    private readonly DispatcherTimer _statusTimer;
    private bool _isAttached;

    public ExamAutoShowSettingsPageViewModel(
        MainConfigHandler configHandler,
        IExamService examService,
        NtpTimeService timeService)
        : base(configHandler)
    {
        _examService = examService;
        _timeService = timeService;

        _statusTimer = new DispatcherTimer { Interval = StatusRefreshInterval };
        _statusTimer.Tick += (_, _) => RefreshSummaries();

        ResetDrafts();
    }

    /// <summary>自动放映的全局参数（每块看板开不开在 <see cref="ExamBoardDocument.AutoShowEnabled" /> 上）。</summary>
    public ExamAutoShowSettingsConfig Settings => Config.ExamAutoShow;

    /// <summary>输入框的上下限直接取策略里的常量，别在界面上另写一套数字。</summary>
    public int MinLeadMinutes => ExamAutoShowPolicy.MinLeadMinutes;

    /// <inheritdoc cref="MinLeadMinutes" />
    public int MaxLeadMinutes => ExamAutoShowPolicy.MaxLeadMinutes;

    /// <inheritdoc cref="MinLeadMinutes" />
    public int MinCloseDelayMinutes => ExamAutoShowPolicy.MinCloseDelayMinutes;

    /// <inheritdoc cref="MinCloseDelayMinutes" />
    public int MaxCloseDelayMinutes => ExamAutoShowPolicy.MaxCloseDelayMinutes;

    /// <summary>提前量草稿（分钟）。</summary>
    [ObservableProperty]
    private decimal? _leadMinutesDraft;

    /// <summary>考完延迟草稿（分钟）。</summary>
    [ObservableProperty]
    private decimal? _closeDelayMinutesDraft;

    /// <summary>状态卡第一行：下一次放映是什么时候 / 没有可放的考试 / 还没有看板参加。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>状态卡第二行：「2 / 3 块看板参加自动放映」，一眼看出范围。</summary>
    [ObservableProperty]
    private string _boardSummaryText = string.Empty;

    /// <summary>挂上考试数据、校时服务与配置的变化订阅（页面进入可视树时调）。重复调用不会重复挂。</summary>
    public void Attach()
    {
        if (_isAttached)
            return;

        _isAttached = true;
        _examService.Changed += OnServiceChanged;
        _timeService.StatusChanged += OnTimeStatusChanged;
        Settings.PropertyChanged += OnSettingsChanged;
        _statusTimer.Start();
        ResetDrafts();
    }

    /// <summary>断开订阅（页面离开可视树时调）。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        _statusTimer.Stop();
        _examService.Changed -= OnServiceChanged;
        _timeService.StatusChanged -= OnTimeStatusChanged;
        Settings.PropertyChanged -= OnSettingsChanged;
    }

    /// <summary>提交两个数值草稿并夹进合法区间（失焦 / 离开页面时调）。</summary>
    public void CommitAll()
    {
        var lead = LeadMinutesDraft is { } leadDraft
            ? ExamAutoShowPolicy.ClampLeadMinutes((int)leadDraft)
            : ExamAutoShowPolicy.ClampLeadMinutes(Settings.LeadMinutes);

        if (Settings.LeadMinutes != lead)
            Settings.LeadMinutes = lead;

        var delay = CloseDelayMinutesDraft is { } delayDraft
            ? ExamAutoShowPolicy.ClampCloseDelayMinutes((int)delayDraft)
            : ExamAutoShowPolicy.ClampCloseDelayMinutes(Settings.CloseDelayMinutes);

        if (Settings.CloseDelayMinutes != delay)
            Settings.CloseDelayMinutes = delay;

        // 夹取过的值同步回界面，免得显示的和落盘的不是一个数。
        LeadMinutesDraft = lead;
        CloseDelayMinutesDraft = delay;

        RefreshSummaries();
    }

    /// <summary>
    ///     「立即放映」：不等到点，现在就把全屏看板铺出来（就是手动按了那次自动放映）。
    ///     不动「当前看板」——预览不该顺手把用户平时看的板子换掉。
    /// </summary>
    [RelayCommand]
    private void Preview()
    {
        CommitAll();
        App.ShowExamBoardWindow();
    }

    private void ResetDrafts()
    {
        LeadMinutesDraft = ExamAutoShowPolicy.ClampLeadMinutes(Settings.LeadMinutes);
        CloseDelayMinutesDraft = ExamAutoShowPolicy.ClampCloseDelayMinutes(Settings.CloseDelayMinutes);
        RefreshSummaries();
    }

    /// <summary>重算状态卡那两行文字。</summary>
    private void RefreshSummaries()
    {
        var now = _timeService.NowLocal;
        var boards = _examService.Boards.Where(board => board is not null).ToList();
        var enabled = boards.Where(board => board.AutoShowEnabled).ToList();

        BoardSummaryText = string.Format(CR.Settings_Exam_AutoShow_BoardSummary, enabled.Count, boards.Count);

        if (enabled.Count == 0)
        {
            StatusText = CR.Settings_Exam_AutoShow_NoBoard;
            return;
        }

        // 多块看板都开着的时候取最先到点的那一场，与看门狗的判断口径一致。
        (ExamEntry Exam, DateTime TriggerAt)? next = null;
        foreach (var board in enabled)
        {
            if (ExamAutoShowPolicy.FindNextTrigger(board.Exams, now, Settings.LeadMinutes) is not { } trigger)
                continue;

            if (next is null || trigger.TriggerAt < next.Value.TriggerAt)
                next = trigger;
        }

        if (next is not { } upcoming)
        {
            StatusText = CR.Settings_Exam_AutoShow_NoExam;
            return;
        }

        var exam = upcoming.Exam;
        var when = string.Format(
            "{0} {1}",
            ExamTimeFormatter.FormatDate(exam.Date, DateOnly.FromDateTime(now)),
            ExamTimeFormatter.FormatTimeRange(exam));

        StatusText = upcoming.TriggerAt <= now
            ? string.Format(CR.Settings_Exam_AutoShow_NextNow, exam.Subject, when)
            : string.Format(CR.Settings_Exam_AutoShow_Next, exam.Subject, when,
                ExamTimeFormatter.FormatTime(TimeOnly.FromDateTime(upcoming.TriggerAt)));
    }

    private void OnServiceChanged(object? sender, EventArgs e) => RefreshSummaries();

    private void OnTimeStatusChanged(object? sender, EventArgs e) => RefreshSummaries();

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // 提前量一改，「下一次放映」的时刻就跟着变；延迟不影响状态卡。
            case null or nameof(ExamAutoShowSettingsConfig.LeadMinutes):
                RefreshSummaries();
                break;
            case nameof(ExamAutoShowSettingsConfig.CloseDelayMinutes):
            case nameof(ExamAutoShowSettingsConfig.AlsoShowAfterStart):
                break;
        }
    }
}
