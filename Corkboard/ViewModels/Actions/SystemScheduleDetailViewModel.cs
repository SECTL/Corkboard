using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Actions;
using Corkboard.Core.Actions.Schedule;
using Corkboard.Core.Display;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Display;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using Corkboard.Services.Actions;
using Corkboard.ViewModels.SettingsPages.Display;
using LR = Corkboard.Langs.Action.Resources;

namespace Corkboard.ViewModels.Actions;

/// <summary>系统日程详情里「下一次发生」的一行。</summary>
public sealed class SystemScheduleRunItem
{
    public string TimeText { get; init; } = "";

    public string Title { get; init; } = "";
}

/// <summary>
///     系统日程详情（考试自动放映 / 看板模式自动进出）：说明来自哪一项设置、下一次什么时候发生，
///     并把这项设置的开关就地摆出来直接改（不再跳设置页）。
///     <para>
///         考试那边改的是 config（提前量、考后收起、自动换看板）与当前看板的
///         <see cref="ExamBoardDocument.AutoShowEnabled" />；看板模式那边改的是
///         <see cref="DisplaySettingsConfig.AutoShow" /> 与时段列表。每次提交都显式落盘一次。
///     </para>
/// </summary>
public sealed partial class SystemScheduleDetailViewModel : ObservableObject
{
    private readonly IScheduleSourceRegistry _sources;
    private readonly ActionExecutor _executor;
    private readonly ActionStateStore _state;
    private readonly NtpTimeService _time;
    private readonly MainConfigHandler _config;
    private readonly IExamService _exam;

    private ActionOccurrenceItem? _item;
    private bool _loading;

    public SystemScheduleDetailViewModel(IScheduleSourceRegistry sources, ActionExecutor executor,
        ActionStateStore state, NtpTimeService time, MainConfigHandler config, IExamService exam)
    {
        _sources = sources;
        _executor = executor;
        _state = state;
        _time = time;
        _config = config;
        _exam = exam;
    }

    /// <summary>请求关掉弹层。</summary>
    public event EventHandler? CloseRequested;

    public ObservableCollection<SystemScheduleRunItem> NextRuns { get; } = [];

    public ObservableCollection<DisplaySlotItem> SlotItems { get; } = [];

    public string Title { get; private set; } = LR.Action_System_DetailTitle;

    public string Description { get; private set; } = "";

    public string KindText { get; private set; } = "";

    public bool CanPause { get; private set; }

    /// <summary>考试自动放映：面板里换成考试那套控件。</summary>
    [ObservableProperty] private bool _isExam;

    /// <summary>看板模式自动进出：面板里换成时段编辑器。</summary>
    [ObservableProperty] private bool _isDisplay;

    [ObservableProperty] private bool _isPausedToday;

    public string PauseTodayText => IsPausedToday ? LR.Action_System_ResumeToday : LR.Action_System_PauseToday;

    [ObservableProperty] private string _nextText = "";

    [ObservableProperty] private bool _hasNext;

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _errorText = "";

    [ObservableProperty] private bool _hasError;

    // ── 考试自动放映 ──────────────────────────────────────────────────────────

    /// <summary>当前看板参不参加自动放映（落盘在看板文件自己的 <c>auto_show_enabled</c>）。</summary>
    [ObservableProperty] private bool _examEnabled;

    /// <summary>开考时自动切到当天要考的那一块看板。</summary>
    [ObservableProperty] private bool _examAutoSwitchBoard;

    /// <summary>开考前多少分钟弹出（1–120）。</summary>
    [ObservableProperty] private decimal? _leadMinutesValue;

    /// <summary>开考之后还补不补弹。</summary>
    [ObservableProperty] private bool _alsoShowAfterStart;

    /// <summary>考完再过多少分钟收起（0–120）。</summary>
    [ObservableProperty] private decimal? _closeDelayValue;

    // ── 看板模式自动进出 ──────────────────────────────────────────────────────

    /// <summary>到点自动进入看板模式。</summary>
    [ObservableProperty] private bool _displayAutoShow;

    [ObservableProperty] private bool _canAddSlot;

    partial void OnIsPausedTodayChanged(bool value) => OnPropertyChanged(nameof(PauseTodayText));

    partial void OnCanAddSlotChanged(bool value) => AddSlotCommand.NotifyCanExecuteChanged();

    /// <summary>载入一条系统日程行。</summary>
    public void Load(ActionOccurrenceItem item)
    {
        _item = item;
        _loading = true;
        try
        {
            IsPausedToday = item.IsPausedToday;
            CanPause = item.CanPause;
            KindText = ActionTextFormatter.DescribeSourceKind(item.Kind switch
            {
                ScheduleItemKind.DisplayAutoShow => ActionRunSourceKind.DisplayAutoShow,
                _ => ActionRunSourceKind.ExamAutoShow
            });
            Title = item.Title;
            IsDisplay = item.Kind == ScheduleItemKind.DisplayAutoShow;
            IsExam = !IsDisplay;

            Description = IsDisplay
                ? LR.Action_System_DisplayAutoShow_Description
                : LR.Action_System_ExamAutoShow_Description;

            var source = _sources.Sources.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, item.SourceId, StringComparison.Ordinal));

            if (IsExam) LoadExamSettings();
            else LoadDisplaySettings();

            BuildNextRuns(source);
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadExamSettings()
    {
        var settings = _config.Data.ExamAutoShow;
        ExamEnabled = _exam.ActiveBoard.AutoShowEnabled;
        ExamAutoSwitchBoard = settings.AutoSwitchBoard;
        LeadMinutesValue = settings.LeadMinutes;
        AlsoShowAfterStart = settings.AlsoShowAfterStart;
        CloseDelayValue = settings.CloseDelayMinutes;
    }

    private void LoadDisplaySettings()
    {
        var settings = _config.Data.Display;
        DisplayAutoShow = settings.AutoShow;

        SlotItems.Clear();
        RefreshSlotItems(settings);
    }

    private void RefreshSlotItems(DisplaySettingsConfig settings)
    {
        SlotItems.Clear();
        if (settings.Slots is null) return;

        foreach (var slot in settings.Slots)
        {
            if (slot is null) continue;
            SlotItems.Add(new DisplaySlotItem(slot, OnSlotChanged, RemoveSlotCore));
        }

        UpdateCanAddSlot();
    }

    private void UpdateCanAddSlot() => CanAddSlot = SlotItems.Count < DisplaySlot.MaxCount;

    private void OnSlotChanged()
    {
        if (_loading) return;
        _config.Save();
        UpdateCanAddSlot();
    }

    private void RemoveSlotCore(DisplaySlotItem item)
    {
        if (item is null) return;

        _config.Data.Display.Slots?.Remove(item.Slot);
        SlotItems.Remove(item);

        if (!_loading)
        {
            _config.Save();
            UpdateCanAddSlot();
        }
    }

    private void BuildNextRuns(IScheduleSource? source)
    {
        NextRuns.Clear();
        if (source is null)
        {
            HasNext = false;
            NextText = LR.Action_NextNone;
            return;
        }

        var now = _time.NowLocal;
        var today = DateOnly.FromDateTime(now);
        var items = source.GetItems(today.ToDateTime(TimeOnly.MinValue), today.AddDays(14).ToDateTime(TimeOnly.MinValue))
            .Where(entry => entry.Start >= now)
            .Take(5);

        foreach (var entry in items)
        {
            NextRuns.Add(new SystemScheduleRunItem
            {
                TimeText = ActionViewText.DescribeWhen(entry.Start, now),
                Title = entry.Title
            });
        }

        HasNext = NextRuns.Count > 0;
        NextText = HasNext ? LR.Action_System_NextRuns : LR.Action_NextNone;
    }

    // ── 考试设置写回 ──────────────────────────────────────────────────────────

    partial void OnExamEnabledChanged(bool value)
    {
        if (_loading) return;
        if (!_exam.SetAutoShow(_exam.ActiveBoard.Id, value))
            ExamEnabled = _exam.ActiveBoard.AutoShowEnabled;
    }

    partial void OnExamAutoSwitchBoardChanged(bool value)
    {
        if (_loading) return;
        _config.Data.ExamAutoShow.AutoSwitchBoard = value;
        _config.Save();
    }

    partial void OnLeadMinutesValueChanged(decimal? value)
    {
        if (_loading) return;

        var clamped = ExamAutoShowPolicy.ClampLeadMinutes((int)(value ?? ExamAutoShowPolicy.DefaultLeadMinutes));
        _config.Data.ExamAutoShow.LeadMinutes = clamped;
        _config.Save();

        if (LeadMinutesValue != clamped) SetLeadMinutesSilently(clamped);
    }

    partial void OnAlsoShowAfterStartChanged(bool value)
    {
        if (_loading) return;
        _config.Data.ExamAutoShow.AlsoShowAfterStart = value;
        _config.Save();
    }

    partial void OnCloseDelayValueChanged(decimal? value)
    {
        if (_loading) return;

        var clamped = ExamAutoShowPolicy.ClampCloseDelayMinutes(
            (int)(value ?? ExamAutoShowPolicy.DefaultCloseDelayMinutes));
        _config.Data.ExamAutoShow.CloseDelayMinutes = clamped;
        _config.Save();

        if (CloseDelayValue != clamped) SetCloseDelaySilently(clamped);
    }

    private void SetLeadMinutesSilently(int value)
    {
        _loading = true;
        LeadMinutesValue = value;
        _loading = false;
    }

    private void SetCloseDelaySilently(int value)
    {
        _loading = true;
        CloseDelayValue = value;
        _loading = false;
    }

    // ── 看板模式设置写回 ──────────────────────────────────────────────────────

    partial void OnDisplayAutoShowChanged(bool value)
    {
        if (_loading) return;
        _config.Data.Display.AutoShow = value;
        _config.Save();
    }

    [RelayCommand(CanExecute = nameof(CanAddSlot))]
    private void AddSlot()
    {
        var settings = _config.Data.Display;
        if (settings.Slots is null) return;
        if (settings.Slots.Count >= DisplaySlot.MaxCount) return;

        var slot = DisplaySlot.CreateDefault();
        settings.Slots.Add(slot);
        SlotItems.Add(new DisplaySlotItem(slot, OnSlotChanged, RemoveSlotCore));

        UpdateCanAddSlot();
        _config.Save();
    }

    // ── 立即执行 / 今天不执行 / 关闭 ──────────────────────────────────────────

    [RelayCommand]
    private async Task ExecuteNow()
    {
        if (_item is null || IsBusy || _executor.IsExecuting) return;

        IsBusy = true;
        try
        {
            var kind = _item.Kind == ScheduleItemKind.DisplayAutoShow
                ? ActionRunSourceKind.DisplayAutoShow
                : ActionRunSourceKind.ExamAutoShow;
            var steps = _item.Item?.TargetIds
                            .Select(id => new ActionStep { TargetId = id, Operation = BoardOperation.Show })
                            .ToList()
                        ?? [];

            var run = await _executor.ExecuteSystemAsync(kind, _item.SourceId, _item.Title, steps,
                _time.NowLocal, ActionRunTrigger.Manual);

            if (run.Status is ActionRunStatus.Failed or ActionRunStatus.Partial)
            {
                HasError = true;
                ErrorText = string.Join("；", run.Steps
                    .Where(step => !string.IsNullOrWhiteSpace(step.Message))
                    .Select(step => step.Message));
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void PauseToday()
    {
        if (_item is null || !CanPause) return;

        var today = DateOnly.FromDateTime(_time.NowLocal);
        var sourceId = _item.SourceId;
        if (string.IsNullOrEmpty(sourceId)) return;

        if (IsPausedToday)
            _state.Unsuppress(sourceId, today);
        else
            _state.Suppress(sourceId, today);

        _state.Save();
        IsPausedToday = !IsPausedToday;
        _item.IsPausedToday = IsPausedToday;
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
