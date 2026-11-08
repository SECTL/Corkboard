using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Actions;
using Corkboard.Core.Actions.Schedule;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using Corkboard.Services.Actions;
using Corkboard.Services.Actions.Schedule;
using Corkboard.Services.Ui;
using Microsoft.Extensions.DependencyInjection;
using LR = Corkboard.Langs.Action.Resources;

namespace Corkboard.ViewModels.Actions;

/// <summary>窗口的三个页签。</summary>
public enum ActionTab
{
    List,
    Week,
    Month
}

/// <summary>工具区右侧的筛选。</summary>
public enum ActionFilter
{
    All,
    Mine,
    System
}

/// <summary>
///     行动窗口（日程中心）的窗口级 ViewModel：加载规则、合并三路日程、筛选搜索、暂停全部、页签切换、
///     立即执行与编辑回调。视图层只负责把这里拼好的字符串与集合画出来。
/// </summary>
public sealed partial class ActionWindowViewModel : ViewModelBase
{
    private readonly ActionRuleStore _rules;
    private readonly ActionRunStore _runs;
    private readonly ActionExecutor _executor;
    private readonly IScheduleSourceRegistry _sources;
    private readonly ActionStateStore _state;
    private readonly PageOverlayService _overlay;
    private readonly IServiceProvider _services;
    private readonly NtpTimeService _time;
    private readonly DispatcherTimer _timer;

    private readonly List<ActionOccurrenceItem> _mineCache = [];
    private readonly List<ActionOccurrenceItem> _systemCache = [];
    private readonly Dictionary<string, string> _itemSourceIds = new(StringComparer.Ordinal);

    private bool _attached;
    private bool _refreshing;
    private DateOnly _weekStart;
    private DateOnly _visibleMonth;
    private DateOnly _selectedDate;
    private DateOnly _lastDay;

    public ActionWindowViewModel(MainConfigHandler configHandler, ActionRuleStore rules, ActionRunStore runs,
        ActionExecutor executor, IScheduleSourceRegistry sources, ActionStateStore state,
        PageOverlayService overlay, IServiceProvider services, NtpTimeService time) : base(configHandler)
    {
        _rules = rules;
        _runs = runs;
        _executor = executor;
        _sources = sources;
        _state = state;
        _overlay = overlay;
        _services = services;
        _time = time;

        var today = DateOnly.FromDateTime(_time.NowLocal);
        _weekStart = StartOfWeek(today);
        _visibleMonth = new DateOnly(today.Year, today.Month, 1);
        _selectedDate = today;
        _lastDay = today;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>需要打开规则编辑器（新建 / 编辑）。</summary>
    public event EventHandler<ActionEditorViewModel>? EditorRequested;

    /// <summary>需要打开系统日程的只读详情。</summary>
    public event EventHandler<SystemScheduleDetailViewModel>? DetailRequested;

    // ── 集合 ──────────────────────────────────────────────────────────────────

    /// <summary>列表页「系统日程」组。</summary>
    public ObservableCollection<ActionOccurrenceItem> SystemItems { get; } = [];

    /// <summary>列表页「我的行动」组。</summary>
    public ObservableCollection<ActionOccurrenceItem> MyItems { get; } = [];

    /// <summary>周视图的七列。</summary>
    public ObservableCollection<ActionWeekColumn> WeekColumns { get; } = [];

    /// <summary>月视图的 42 个日格。</summary>
    public ObservableCollection<ActionMonthCell> MonthCells { get; } = [];

    /// <summary>月视图下方明细区的行。</summary>
    public ObservableCollection<ActionOccurrenceItem> DayItems { get; } = [];

    /// <summary>月视图星期头的七个文案（周一为首列）。</summary>
    public IReadOnlyList<string> WeekdayHeaders { get; } =
    [
        LR.Action_WeekdayHeader_Monday, LR.Action_WeekdayHeader_Tuesday, LR.Action_WeekdayHeader_Wednesday,
        LR.Action_WeekdayHeader_Thursday, LR.Action_WeekdayHeader_Friday, LR.Action_WeekdayHeader_Saturday,
        LR.Action_WeekdayHeader_Sunday
    ];

    // ── 页签 / 筛选 / 搜索 ────────────────────────────────────────────────────

    [ObservableProperty] private ActionTab _tab = ActionTab.List;

    [ObservableProperty] private ActionFilter _filter = ActionFilter.All;

    partial void OnTabChanged(ActionTab value)
    {
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsWeekView));
        OnPropertyChanged(nameof(IsMonthView));
    }

    partial void OnFilterChanged(ActionFilter value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterMine));
        OnPropertyChanged(nameof(IsFilterSystem));
        ApplyFilter();
        BuildWeek();
        BuildMonth();
        NotifyEmpties();
    }

    public bool IsListView => Tab == ActionTab.List;

    public bool IsWeekView => Tab == ActionTab.Week;

    public bool IsMonthView => Tab == ActionTab.Month;

    public bool IsFilterAll => Filter == ActionFilter.All;

    public bool IsFilterMine => Filter == ActionFilter.Mine;

    public bool IsFilterSystem => Filter == ActionFilter.System;

    /// <summary>搜索框只过滤列表页。</summary>
    [ObservableProperty] private string _searchText = "";

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
        NotifyEmpties();
    }

    // ── 状态栏 / 提示条 ───────────────────────────────────────────────────────

    [ObservableProperty] private string _nextText = "";

    [ObservableProperty] private string _countText = "";

    [ObservableProperty] private string _todayCountText = "";

    [ObservableProperty] private string _weekTitle = "";

    [ObservableProperty] private string _monthTitle = "";

    [ObservableProperty] private string _dayTitle = "";

    [ObservableProperty] private string _errorText = "";

    [ObservableProperty] private bool _hasError;

    [ObservableProperty] private bool _isBusy;

    /// <summary>rules.json 读坏 / 版本过高：窗口只读。</summary>
    public bool IsReadOnly => _rules.IsReadOnly;

    /// <summary>全局暂停（写 ActionSettings.Enabled 的反面）。</summary>
    public bool IsPaused
    {
        get => !Config.ActionSettings.Enabled;
        set
        {
            if (IsPaused == value) return;
            Config.ActionSettings.Enabled = !value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PauseText));
            OnPropertyChanged(nameof(HasBanner));
            OnPropertyChanged(nameof(BannerText));
        }
    }

    public string PauseText => IsPaused ? LR.Action_ResumeAll : LR.Action_PauseAll;

    /// <summary>暂停时窗口顶部那条提示（全局暂停不作用于系统日程）。</summary>
    public bool HasBanner => IsPaused;

    public string BannerText => LR.Action_PausedBanner;

    // ── 空态 ──────────────────────────────────────────────────────────────────

    public bool HasSystemItems => SystemItems.Count > 0;

    public bool HasMyItems => MyItems.Count > 0;

    public bool IsListEmpty => SystemItems.Count == 0 && MyItems.Count == 0;

    public bool IsWeekEmpty => WeekColumns.All(column => column.Items.Count == 0);

    public bool IsMonthEmpty => DayItems.Count == 0;

    public bool HasDayItems => DayItems.Count > 0;

    /// <summary>一条规则都没有时才展示三颗示例（有规则但被筛选掉时不展示）。</summary>
    public bool ShowExamples => _rules.Rules.Count == 0 && !IsReadOnly;

    // ── 生命周期 ──────────────────────────────────────────────────────────────

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _rules.Changed += OnRulesChanged;
        _executor.ActionRunRecorded += OnActionRunRecorded;
        Config.ActionSettings.PropertyChanged += OnActionSettingsChanged;
        _timer.Start();
        Refresh();
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _rules.Changed -= OnRulesChanged;
        _executor.ActionRunRecorded -= OnActionRunRecorded;
        Config.ActionSettings.PropertyChanged -= OnActionSettingsChanged;
        _timer.Stop();
    }

    private void OnRulesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

    private void OnActionRunRecorded(object? sender, ActionRun run) => Dispatcher.UIThread.Post(Refresh);

    private void OnActionSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or nameof(Config.ActionSettings.Enabled))) return;
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PauseText));
        OnPropertyChanged(nameof(HasBanner));
        OnPropertyChanged(nameof(BannerText));
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var today = DateOnly.FromDateTime(_time.NowLocal);
        if (today != _lastDay)
        {
            _lastDay = today;
            Refresh();
            return;
        }

        RefreshStatusBar();
    }

    // ── 刷新 ──────────────────────────────────────────────────────────────────

    public void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var now = _time.NowLocal;
            _lastDay = DateOnly.FromDateTime(now);

            if (IsReadOnly)
            {
                HasError = true;
                ErrorText = string.IsNullOrWhiteSpace(_rules.LoadError)
                    ? LR.Action_Error_RulesUnreadable
                    : _rules.LoadError;
            }
            else if (!HasError)
            {
                ErrorText = "";
            }

            _itemSourceIds.Clear();
            BuildSystemCache(now);
            BuildMineCache(now);
            ApplyFilter();
            BuildWeek();
            BuildMonth();
            RefreshStatusBar();
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(ShowExamples));
        }
        catch (Exception ex)
        {
            // 刷新链路中途炸掉时别让窗口变成「看起来正常但什么都不动」：
            // 把原因写到顶部错误条，用户至少知道发生了什么。
            SetError(string.Format(CultureInfo.CurrentCulture, LR.Action_Error_RefreshFailed,
                ex.GetBaseException().Message));
        }
        finally
        {
            _refreshing = false;
        }

        NotifyEmpties();
    }

    private void NotifyEmpties()
    {
        OnPropertyChanged(nameof(HasSystemItems));
        OnPropertyChanged(nameof(HasMyItems));
        OnPropertyChanged(nameof(IsListEmpty));
        OnPropertyChanged(nameof(IsWeekEmpty));
        OnPropertyChanged(nameof(IsMonthEmpty));
        OnPropertyChanged(nameof(HasDayItems));
        OnPropertyChanged(nameof(ShowExamples));
    }

    private void BuildMineCache(DateTime now)
    {
        _mineCache.Clear();
        var from = DateOnly.FromDateTime(now).AddDays(-7);
        var to = DateOnly.FromDateTime(now);
        var runs = _runs.ReadRange(from, to);

        foreach (var rule in _rules.Rules)
        {
            var ruleRuns = runs
                .Where(run => run.SourceKind == ActionRunSourceKind.CustomRule &&
                              string.Equals(run.SourceId, rule.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            var item = ActionOccurrenceItem.FromRule(rule, now, ruleRuns);
            item.EnabledChanged = OnItemEnabledChanged;
            _mineCache.Add(item);
        }

        _mineCache.Sort((x, y) => CompareListRows(x, y, now));
    }

    private static int CompareListRows(ActionOccurrenceItem x, ActionOccurrenceItem y, DateTime now)
    {
        if (x.IsEnabled != y.IsEnabled) return x.IsEnabled ? -1 : 1;
        if (x.IsEnabled)
        {
            var xNext = x.Rule is { } xr ? ActionSchedule.NextOccurrence(xr, now) : null;
            var yNext = y.Rule is { } yr ? ActionSchedule.NextOccurrence(yr, now) : null;
            if ((xNext is null) != (yNext is null)) return xNext is null ? 1 : -1;
            if (xNext is { } a && yNext is { } b) return a.CompareTo(b);
            return string.Compare(x.Title, y.Title, StringComparison.CurrentCulture);
        }

        var xUpdated = x.Rule?.UpdatedAt ?? x.Rule?.CreatedAt ?? DateTime.MinValue;
        var yUpdated = y.Rule?.UpdatedAt ?? y.Rule?.CreatedAt ?? DateTime.MinValue;
        return yUpdated.CompareTo(xUpdated);
    }

    private void BuildSystemCache(DateTime now)
    {
        _systemCache.Clear();
        var today = DateOnly.FromDateTime(now);
        var from = today.ToDateTime(TimeOnly.MinValue);
        var to = today.AddDays(31).ToDateTime(TimeOnly.MinValue);
        var runs = _runs.ReadRange(today.AddDays(-7), today);

        foreach (var source in _sources.Sources)
        {
            // 自定义行动的投影就是「我的行动」组本身，别再重复一行。
            if (string.Equals(source.Id, CustomActionScheduleSource.SourceId, StringComparison.Ordinal)) continue;

            var items = source.GetItems(from, to);
            var kind = GuessKind(source.Id);
            var sourceRuns = runs.Where(run => RunKindMatches(run, kind)).ToList();
            var paused = source.CanPause && _state.IsSuppressed(source.Id, today);
            _systemCache.Add(ActionOccurrenceItem.FromSource(source, items, paused, sourceRuns, now));

            foreach (var scheduleItem in items) _itemSourceIds[scheduleItem.Id] = source.Id;
        }
    }

    private static ScheduleItemKind GuessKind(string sourceId) =>
        sourceId.Contains("display", StringComparison.OrdinalIgnoreCase)
            ? ScheduleItemKind.DisplayAutoShow
            : ScheduleItemKind.ExamAutoShow;

    private static bool RunKindMatches(ActionRun run, ScheduleItemKind kind) => kind switch
    {
        ScheduleItemKind.DisplayAutoShow => run.SourceKind == ActionRunSourceKind.DisplayAutoShow,
        ScheduleItemKind.ExamAutoShow => run.SourceKind == ActionRunSourceKind.ExamAutoShow,
        _ => run.SourceKind == ActionRunSourceKind.CustomRule
    };

    private void ApplyFilter()
    {
        var keyword = SearchText.Trim();
        MyItems.Clear();
        SystemItems.Clear();

        if (Filter != ActionFilter.System)
        {
            foreach (var item in _mineCache)
            {
                if (keyword.Length > 0 && !Matches(item, keyword)) continue;
                MyItems.Add(item);
            }
        }

        if (Filter != ActionFilter.Mine)
        {
            foreach (var item in _systemCache)
            {
                if (keyword.Length > 0 && !Matches(item, keyword)) continue;
                SystemItems.Add(item);
            }
        }
    }

    private static bool Matches(ActionOccurrenceItem item, string keyword)
    {
        foreach (var part in new[]
                 {
                     item.Title, item.TriggerText, item.ConditionText, item.TargetsText, item.EffectText,
                     item.ResultText
                 })
        {
            if (!string.IsNullOrEmpty(part) && part.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
                return true;
        }

        return false;
    }

    // ── 周视图 ────────────────────────────────────────────────────────────────

    private void BuildWeek()
    {
        _weekStart = StartOfWeek(_weekStart);
        WeekColumns.Clear();

        var now = _time.NowLocal;
        var startDt = _weekStart.ToDateTime(TimeOnly.MinValue);
        var endExclusive = startDt.AddDays(7);

        var includeMine = Filter != ActionFilter.System;
        var includeSystem = Filter != ActionFilter.Mine;
        var runs = _runs.ReadRange(DateOnly.FromDateTime(startDt), DateOnly.FromDateTime(endExclusive).AddDays(-1));

        var buckets = new List<ActionOccurrenceItem>?[7];

        if (includeMine)
        {
            var occurrences = ActionSchedule.OccurrencesBetween(_rules.Rules, startDt.AddTicks(-1), endExclusive.AddTicks(-1));
            foreach (var occurrence in occurrences)
            {
                var index = (occurrence.At.Date - startDt.Date).Days;
                if (index is < 0 or > 6) continue;

                var ruleRuns = runs
                    .Where(run => run.SourceKind == ActionRunSourceKind.CustomRule &&
                                  string.Equals(run.SourceId, occurrence.Rule.Id.ToString(),
                                      StringComparison.OrdinalIgnoreCase) &&
                                  Math.Abs((run.OccurrenceAt - occurrence.At).TotalSeconds) < 90)
                    .ToList();
                (buckets[index] ??= []).Add(ActionOccurrenceItem.FromOccurrence(occurrence.Rule, occurrence.At, ruleRuns, now));
            }
        }

        if (includeSystem)
        {
            foreach (var scheduleItem in CollectSystemItems(startDt, endExclusive))
            {
                for (var i = 0; i < 7; i++)
                {
                    var date = _weekStart.AddDays(i);
                    if (!Overlaps(scheduleItem, date)) continue;

                    var sourceId = _itemSourceIds.GetValueOrDefault(scheduleItem.Id, "");
                    var paused = sourceId.Length > 0 && _state.IsSuppressed(sourceId, date);
                    var itemRuns = runs.Where(run => RunKindMatches(run, scheduleItem.Kind)).ToList();
                    (buckets[i] ??= []).Add(ActionOccurrenceItem.FromScheduleItem(scheduleItem, paused, itemRuns, now));
                }
            }
        }

        var today = DateOnly.FromDateTime(_time.NowLocal);
        for (var i = 0; i < 7; i++)
        {
            var date = _weekStart.AddDays(i);
            var column = new ActionWeekColumn
            {
                Date = date,
                HeaderText = ActionViewText.WeekdayName(date.DayOfWeek),
                DayText = date.ToString("MM/dd", CultureInfo.CurrentCulture),
                IsToday = date == today
            };
            foreach (var item in (buckets[i] ?? [])
                         .OrderBy(entry => entry.Start)
                         .ThenBy(entry => entry.IsInterval ? 1 : 0))
                column.Items.Add(item);
            WeekColumns.Add(column);
        }

        WeekTitle = string.Format(CultureInfo.CurrentCulture, LR.Action_Week_Range,
            _weekStart.ToDateTime(TimeOnly.MinValue).ToString("M/d", CultureInfo.CurrentCulture),
            _weekStart.AddDays(6).ToDateTime(TimeOnly.MinValue).ToString("M/d", CultureInfo.CurrentCulture));
    }

    private IEnumerable<ScheduleItem> CollectSystemItems(DateTime from, DateTime to)
    {
        foreach (var source in _sources.Sources)
        {
            if (string.Equals(source.Id, CustomActionScheduleSource.SourceId, StringComparison.Ordinal)) continue;

            foreach (var item in source.GetItems(from, to))
            {
                _itemSourceIds.TryAdd(item.Id, source.Id);
                yield return item;
            }
        }
    }

    private static bool Overlaps(ScheduleItem item, DateOnly date)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var end = item.End ?? item.Start;
        return item.Start < dayEnd && end >= dayStart;
    }

    [RelayCommand]
    private void PrevWeek()
    {
        _weekStart = _weekStart.AddDays(-7);
        BuildWeek();
        NotifyEmpties();
    }

    [RelayCommand]
    private void NextWeek()
    {
        _weekStart = _weekStart.AddDays(7);
        BuildWeek();
        NotifyEmpties();
    }

    [RelayCommand]
    private void ThisWeek()
    {
        _weekStart = StartOfWeek(DateOnly.FromDateTime(_time.NowLocal));
        BuildWeek();
        NotifyEmpties();
    }

    // ── 月视图 ────────────────────────────────────────────────────────────────

    private void BuildMonth()
    {
        var firstOfMonth = new DateOnly(_visibleMonth.Year, _visibleMonth.Month, 1);
        var gridStart = firstOfMonth.AddDays(-MondayIndex(firstOfMonth));
        var today = DateOnly.FromDateTime(_time.NowLocal);
        var includeMine = Filter != ActionFilter.System;
        var includeSystem = Filter != ActionFilter.Mine;

        var gridEndExclusive = gridStart.ToDateTime(TimeOnly.MinValue).AddDays(42);
        var systemItems = includeSystem
            ? CollectSystemItems(gridStart.ToDateTime(TimeOnly.MinValue), gridEndExclusive).ToList()
            : [];

        MonthCells.Clear();
        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            var entries = new List<(DateTime Start, ActionMonthChip Chip)>();

            if (includeMine)
            {
                foreach (var rule in _rules.Rules)
                {
                    if (!rule.Enabled) continue;
                    if (!ActionSchedule.OccursOn(rule, date)) continue;

                    var at = date.ToDateTime(rule.Trigger.Time);
                    if (!ActionSchedule.IsWithinCondition(rule, at)) continue;

                    entries.Add((at, new ActionMonthChip
                    {
                        Text = $"{at.ToString("HH:mm", CultureInfo.CurrentCulture)} {ActionTextFormatter.Summarize(rule)}",
                        IsMine = true
                    }));
                }
            }

            if (includeSystem)
            {
                var dayStart = date.ToDateTime(TimeOnly.MinValue);
                foreach (var scheduleItem in systemItems)
                {
                    if (!Overlaps(scheduleItem, date)) continue;

                    var visibleStart = scheduleItem.Start < dayStart ? dayStart : scheduleItem.Start;
                    var isDisplay = scheduleItem.Kind == ScheduleItemKind.DisplayAutoShow;
                    entries.Add((visibleStart, new ActionMonthChip
                    {
                        Text = $"{visibleStart.ToString("HH:mm", CultureInfo.CurrentCulture)} {scheduleItem.Title}",
                        IsExam = !isDisplay,
                        IsDisplay = isDisplay
                    }));
                }
            }

            entries.Sort((left, right) => left.Start.CompareTo(right.Start));
            var chips = entries.Take(3).Select(entry => entry.Chip).ToList();

            var cell = new ActionMonthCell
            {
                Date = date,
                DayText = date.Day.ToString(CultureInfo.InvariantCulture),
                IsCurrentMonth = date.Month == firstOfMonth.Month && date.Year == firstOfMonth.Year,
                IsToday = date == today,
                IsSelected = date == _selectedDate,
                Count = entries.Count
            };
            cell.SetChips(chips, entries.Count - chips.Count);
            MonthCells.Add(cell);
        }

        MonthTitle = firstOfMonth.ToDateTime(TimeOnly.MinValue).ToString("y", CultureInfo.CurrentCulture);
        BuildDayItems();
    }

    private void BuildDayItems()
    {
        DayItems.Clear();
        var now = _time.NowLocal;
        var date = _selectedDate;
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var includeMine = Filter != ActionFilter.System;
        var includeSystem = Filter != ActionFilter.Mine;
        var runs = _runs.ReadRange(date, date);

        if (includeMine)
        {
            foreach (var rule in _rules.Rules)
            {
                if (!rule.Enabled) continue;
                foreach (var occurrence in ActionSchedule.OccurrencesOf(rule, dayStart.AddTicks(-1), dayEnd.AddTicks(-1)))
                {
                    var ruleRuns = runs
                        .Where(run => run.SourceKind == ActionRunSourceKind.CustomRule &&
                                      string.Equals(run.SourceId, rule.Id.ToString(), StringComparison.OrdinalIgnoreCase) &&
                                      Math.Abs((run.OccurrenceAt - occurrence).TotalSeconds) < 90)
                        .ToList();
                    DayItems.Add(ActionOccurrenceItem.FromOccurrence(rule, occurrence, ruleRuns, now));
                }
            }
        }

        if (includeSystem)
        {
            foreach (var item in CollectSystemItems(dayStart, dayEnd))
            {
                if (!Overlaps(item, date)) continue;

                var sourceId = _itemSourceIds.GetValueOrDefault(item.Id, "");
                var paused = sourceId.Length > 0 && _state.IsSuppressed(sourceId, date);
                var itemRuns = runs.Where(run => RunKindMatches(run, item.Kind)).ToList();
                DayItems.Add(ActionOccurrenceItem.FromScheduleItem(item, paused, itemRuns, now));
            }
        }

        var ordered = DayItems.OrderBy(entry => entry.Start).ThenBy(entry => entry.IsInterval ? 1 : 0).ToList();
        DayItems.Clear();
        foreach (var item in ordered) DayItems.Add(item);

        DayTitle = string.Format(CultureInfo.CurrentCulture, LR.Action_Month_DetailTitle,
            date.ToDateTime(TimeOnly.MinValue).ToString("M", CultureInfo.CurrentCulture),
            ActionViewText.WeekdayName(date.DayOfWeek));

        NotifyEmpties();
    }

    private static int MondayIndex(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;

    private static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-MondayIndex(date));

    [RelayCommand]
    private void PrevMonth()
    {
        _visibleMonth = _visibleMonth.AddMonths(-1);
        SyncSelectedDateToMonth();
        BuildMonth();
        NotifyEmpties();
    }

    [RelayCommand]
    private void NextMonth()
    {
        _visibleMonth = _visibleMonth.AddMonths(1);
        SyncSelectedDateToMonth();
        BuildMonth();
        NotifyEmpties();
    }

    [RelayCommand]
    private void ThisMonth()
    {
        var today = DateOnly.FromDateTime(_time.NowLocal);
        _visibleMonth = new DateOnly(today.Year, today.Month, 1);
        _selectedDate = today;
        BuildMonth();
        NotifyEmpties();
    }

    private void SyncSelectedDateToMonth()
    {
        if (_selectedDate.Year == _visibleMonth.Year && _selectedDate.Month == _visibleMonth.Month) return;
        _selectedDate = _visibleMonth;
    }

    [RelayCommand]
    private void SelectDay(ActionMonthCell? cell)
    {
        if (cell is null || cell.Date == _selectedDate) return;
        _selectedDate = cell.Date;
        foreach (var monthCell in MonthCells) monthCell.IsSelected = monthCell.Date == _selectedDate;
        BuildDayItems();
    }

    // ── 页签 / 筛选 ───────────────────────────────────────────────────────────

    [RelayCommand]
    private void SwitchTab(string? key) => Tab = key switch
    {
        "week" => ActionTab.Week,
        "month" => ActionTab.Month,
        _ => ActionTab.List
    };

    [RelayCommand]
    private void SetFilter(string? key) => Filter = key switch
    {
        "mine" => ActionFilter.Mine,
        "system" => ActionFilter.System,
        _ => ActionFilter.All
    };

    // ── 新建 / 编辑 / 立即执行 ────────────────────────────────────────────────

    /// <summary>「暂停全部行动 / 恢复全部行动」——写 ActionSettings.Enabled，系统日程不受影响。</summary>
    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    [RelayCommand]
    private void NewAction() => OpenEditor(null);

    /// <summary>空态里那三颗示例：只预填编辑器，不落盘。</summary>
    [RelayCommand]
    private void NewExample(string? key)
    {
        var rule = new ActionRule
        {
            Id = Guid.NewGuid(),
            Enabled = true,
            Steps = [new ActionStep { TargetId = BoardTargetIds.Homework, Operation = BoardOperation.Show }]
        };

        switch (key)
        {
            case "weekly":
                rule = rule with
                {
                    Trigger = new ActionTrigger
                    {
                        Kind = ActionTriggerKind.Weekly,
                        Time = new TimeOnly(12, 10),
                        Days = ActionDayFlags.Weekdays
                    },
                    Steps = [new ActionStep { TargetId = BoardTargetIds.Homework, Operation = BoardOperation.Hide }]
                };
                break;

            case "once":
                var today = DateOnly.FromDateTime(_time.NowLocal);
                var date = new DateOnly(today.Year, 11, 20);
                if (date < today) date = date.AddYears(1);
                rule = rule with
                {
                    Trigger = new ActionTrigger
                    {
                        Kind = ActionTriggerKind.Once,
                        Date = date,
                        Time = new TimeOnly(8, 0)
                    },
                    Steps = [new ActionStep { TargetId = BoardTargetIds.ExamBoard, Operation = BoardOperation.Show }]
                };
                break;

            default:
                rule = rule with { Trigger = new ActionTrigger { Kind = ActionTriggerKind.Daily, Time = new TimeOnly(7, 40) } };
                break;
        }

        OpenEditor(rule);
    }

    [RelayCommand]
    private void Edit(ActionOccurrenceItem? item)
    {
        if (item is null) return;
        if (item.IsSystem)
        {
            OpenDetail(item);
            return;
        }

        if (IsReadOnly || item.Rule is null) return;
        OpenEditor(item.Rule);
    }

    private void OpenEditor(ActionRule? rule)
    {
        if (IsReadOnly) return;

        try
        {
            var editor = _services.GetRequiredService<ActionEditorViewModel>();
            editor.Load(rule);
            ClearError();
            EditorRequested?.Invoke(this, editor);
        }
        catch (Exception ex)
        {
            // 编辑器起不来时必须让用户看见原因：只记日志的话界面上就是「点了没反应」。
            SetError(string.Format(CultureInfo.CurrentCulture, LR.Action_Error_EditorFailed,
                ex.GetBaseException().Message));
        }
    }

    private void OpenDetail(ActionOccurrenceItem item)
    {
        try
        {
            var detail = _services.GetRequiredService<SystemScheduleDetailViewModel>();
            detail.Load(item);
            ClearError();
            DetailRequested?.Invoke(this, detail);
        }
        catch (Exception ex)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, LR.Action_Error_EditorFailed,
                ex.GetBaseException().Message));
        }
    }

    [RelayCommand]
    private async Task RunNow(ActionOccurrenceItem? item)
    {
        if (item is null || IsBusy) return;
        if (_executor.IsExecuting)
        {
            SetError(LR.Action_Message_Busy);
            return;
        }

        var now = _time.NowLocal;
        IsBusy = true;
        try
        {
            ActionRun run;
            if (item.IsSystem)
            {
                var kind = item.Kind == ScheduleItemKind.DisplayAutoShow
                    ? ActionRunSourceKind.DisplayAutoShow
                    : ActionRunSourceKind.ExamAutoShow;
                var steps = item.Item?.TargetIds
                                .Select(id => new ActionStep { TargetId = id, Operation = BoardOperation.Show })
                                .ToList()
                            ?? [];
                run = await _executor.ExecuteSystemAsync(kind, item.SourceId, item.Title, steps, now,
                    ActionRunTrigger.Manual);
            }
            else if (item.Rule is { } rule)
            {
                var occurrenceAt = item.IsOccurrencePoint ? item.Start : now;
                run = await _executor.ExecuteRuleAsync(rule, occurrenceAt, ActionRunTrigger.Manual);
            }
            else
            {
                return;
            }

            if (run.Status is ActionRunStatus.Failed or ActionRunStatus.Partial)
            {
                var detail = string.Join("；", run.Steps
                    .Where(step => !string.IsNullOrWhiteSpace(step.Message))
                    .Select(step => step.Message));
                SetError(string.IsNullOrWhiteSpace(detail) ? ActionTextFormatter.DescribeRunStatus(run.Status) : detail);
            }
            else
            {
                ClearError();
            }
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    /// <summary>系统日程「今天不执行 / 恢复今天执行」。</summary>
    [RelayCommand]
    private void PauseToday(ActionOccurrenceItem? item)
    {
        if (item is null || !item.CanPause) return;
        var today = DateOnly.FromDateTime(_time.NowLocal);
        var sourceId = ResolveSourceId(item);
        if (string.IsNullOrEmpty(sourceId)) return;

        if (item.IsPausedToday)
            _state.Unsuppress(sourceId, today);
        else
            _state.Suppress(sourceId, today);

        _state.Save();
        Refresh();
    }

    [RelayCommand]
    private void ToggleHistory(ActionOccurrenceItem? item)
    {
        if (item is null) return;
        item.IsHistoryOpen = !item.IsHistoryOpen;
    }

    private string ResolveSourceId(ActionOccurrenceItem item)
    {
        if (!item.IsSystem) return "";

        if (item.Item is { } scheduleItem && _itemSourceIds.TryGetValue(scheduleItem.Id, out var mapped))
            return mapped;

        return item.SourceId;
    }

    private void OnItemEnabledChanged(ActionOccurrenceItem item, bool enabled)
    {
        if (IsReadOnly || item.RuleId is not { } id) return;
        try
        {
            _rules.SetEnabled(id, enabled);
        }
        catch (ActionStoreReadOnlyException ex)
        {
            SetError(ex.Message);
        }
    }

    // ── 状态栏 ────────────────────────────────────────────────────────────────

    private void RefreshStatusBar()
    {
        var now = _time.NowLocal;
        var enabled = _rules.Rules.Where(rule => rule.Enabled).ToList();

        DateTime? nextAt = null;
        var nextText = "";
        foreach (var rule in enabled)
        {
            if (ActionSchedule.NextOccurrence(rule, now) is not { } at) continue;
            if (nextAt is not null && at >= nextAt) continue;
            nextAt = at;
            nextText = $"{ActionViewText.DescribeWhen(at, now)} {ActionTextFormatter.DescribeSteps(rule.Steps)}";
        }

        foreach (var row in _systemCache)
        {
            if (row.Start <= now || (nextAt is not null && row.Start >= nextAt)) continue;
            nextAt = row.Start;
            nextText = $"{ActionViewText.DescribeWhen(row.Start, now)} {row.EffectText}";
        }

        NextText = nextAt is null
            ? LR.Action_NextNone
            : string.Format(CultureInfo.CurrentCulture, LR.Action_Next, nextText.Trim());
        CountText = string.Format(CultureInfo.CurrentCulture, LR.Action_EnabledCount, enabled.Count,
            _rules.Rules.Count);
        TodayCountText = string.Format(CultureInfo.CurrentCulture, LR.Action_TodayCount,
            _runs.ReadDay(DateOnly.FromDateTime(now)).Count);
    }

    private void SetError(string message)
    {
        HasError = true;
        ErrorText = message;
    }

    private void ClearError()
    {
        if (IsReadOnly) return;
        HasError = false;
        ErrorText = "";
    }

    /// <summary>窗口关闭弹层时用（Esc / 关闭按钮）。</summary>
    public void CloseOverlay() => _overlay.Close();
}

/// <summary>视图层共用的少量文本拼装（星期名、相对日期），与业务判断无关。</summary>
internal static class ActionViewText
{
    public static string WeekdayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => LR.Action_WeekdayHeader_Monday,
        DayOfWeek.Tuesday => LR.Action_WeekdayHeader_Tuesday,
        DayOfWeek.Wednesday => LR.Action_WeekdayHeader_Wednesday,
        DayOfWeek.Thursday => LR.Action_WeekdayHeader_Thursday,
        DayOfWeek.Friday => LR.Action_WeekdayHeader_Friday,
        DayOfWeek.Saturday => LR.Action_WeekdayHeader_Saturday,
        _ => LR.Action_WeekdayHeader_Sunday
    };

    /// <summary>「今天 07:40」/「明天 07:40」/「11-05 07:40」。</summary>
    public static string DescribeWhen(DateTime at, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var date = DateOnly.FromDateTime(at);
        var clock = at.ToString("HH:mm", CultureInfo.CurrentCulture);
        if (date == today) return $"{LR.Action_Relative_Today} {clock}";
        if (date == today.AddDays(1)) return $"{LR.Action_Relative_Tomorrow} {clock}";
        return at.ToString("MM-dd HH:mm", CultureInfo.CurrentCulture);
    }
}
