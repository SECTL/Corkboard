using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.DutyRoster;
using Corkboard.Core.Models.SubConfigs.Duty;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;

namespace Corkboard.ViewModels.DutyRoster;

/// <summary>
///     周视图表头的一格：星期几 + 月/日 + 徽标（今天 / 休 / 补）。
/// </summary>
public sealed partial class DutyWeekHeaderItem : ObservableObject
{
    [ObservableProperty] private string _dayName = string.Empty;

    [ObservableProperty] private string _dateText = string.Empty;

    [ObservableProperty] private bool _isToday;

    [ObservableProperty] private bool _isOff;

    /// <summary>补班 / 节假日之类的短标记，没有就是空串。</summary>
    [ObservableProperty] private string _marker = string.Empty;

    public DateOnly Date { get; init; }
}

/// <summary>周视图里「某职务 × 某天」的一格。</summary>
public sealed partial class DutyWeekCell : ObservableObject
{
    [ObservableProperty] private string _text = string.Empty;

    /// <summary>「固」固定人员 / 「换」兑换 / 「罚」加罚 / 「加」增设；空串表示没有。</summary>
    [ObservableProperty] private string _badge = string.Empty;

    [ObservableProperty] private bool _isToday;

    /// <summary>这一天不值日（节假日 / 假期 / 手动停）——整格淡化。</summary>
    [ObservableProperty] private bool _isOff;

    [ObservableProperty] private bool _isEmpty;

    public DateOnly Date { get; init; }
}

/// <summary>周视图的一行：第一行是「值日小组」，之后每个职务一行。</summary>
public sealed class DutyWeekRow
{
    public string Title { get; init; } = string.Empty;

    /// <summary>这一行是不是「值日小组」那一行（画得比职务行重一点）。</summary>
    public bool IsGroupRow { get; init; }

    public IReadOnlyList<DutyWeekCell> Cells { get; init; } = [];

    /// <summary>
    ///     第 N 天的格子：DataGrid 的七列各自绑一个固定属性（Tabular 数据没法按列号取下标）。
    ///     列数与 <see cref="Cells" /> 对不上时给 null，模板里显示为空。
    /// </summary>
    public DutyWeekCell? Day1 => CellAt(0);

    public DutyWeekCell? Day2 => CellAt(1);

    public DutyWeekCell? Day3 => CellAt(2);

    public DutyWeekCell? Day4 => CellAt(3);

    public DutyWeekCell? Day5 => CellAt(4);

    public DutyWeekCell? Day6 => CellAt(5);

    public DutyWeekCell? Day7 => CellAt(6);

    private DutyWeekCell? CellAt(int index) => index < Cells.Count ? Cells[index] : null;
}

/// <summary>月视图的一格。</summary>
public sealed partial class DutyMonthCell : ObservableObject
{
    [ObservableProperty] private string _dayText = string.Empty;

    [ObservableProperty] private string _groupName = string.Empty;

    [ObservableProperty] private string _badge = string.Empty;

    [ObservableProperty] private bool _isToday;

    /// <summary>不属于本月（月历补齐用的上下月格子）。</summary>
    [ObservableProperty] private bool _isOutside;

    [ObservableProperty] private bool _isOff;

    public DateOnly Date { get; init; }
}

/// <summary>月视图的一行 = 一周 = 7 个格子（月历固定 6 行，每行 7 天）。</summary>
public sealed class DutyMonthRow
{
    public IReadOnlyList<DutyMonthCell> Cells { get; init; } = [];

    public DutyMonthCell? Week1 => CellAt(0);

    public DutyMonthCell? Week2 => CellAt(1);

    public DutyMonthCell? Week3 => CellAt(2);

    public DutyMonthCell? Week4 => CellAt(3);

    public DutyMonthCell? Week5 => CellAt(4);

    public DutyMonthCell? Week6 => CellAt(5);

    public DutyMonthCell? Week7 => CellAt(6);

    private DutyMonthCell? CellAt(int index) => index < Cells.Count ? Cells[index] : null;
}

/// <summary>日详情面板里的一行人员。</summary>
public sealed class DutyDayDetailEntry
{
    public string PostName { get; init; } = string.Empty;

    public string MemberName { get; init; } = string.Empty;

    public string Badge { get; init; } = string.Empty;
}

/// <summary>给兑换 / 指定 / 加罚对话框用的下拉项（小组、职务、人员通用）。</summary>
public sealed class DutyChoice
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>
///     值日表窗口的 ViewModel：持有「当前看哪一周 / 哪一月」，用排班引擎现算出周表与月历。
///     <para>
///         排班结果每次现算（一天几毫秒），不缓存——缓存一多就会在改规则、改节假日、改兑换之后
///         出现「表格还是旧的」。真正的权威数据只有 <see cref="DutyRosterService.Document" />。
///     </para>
///     <para>
///         节假日判定通过 <see cref="HolidayCalendar" /> 接进来（可为空 = 不跳任何节假日），
///         这样这一层不依赖节假日模块是否已就绪。
///     </para>
/// </summary>
public sealed partial class DutyRosterViewModel : ViewModelBase
{
    private readonly DutyRosterService _service;
    private readonly NtpTimeService _time;

    public DutyRosterViewModel(
        MainConfigHandler configHandler,
        DutyRosterService service,
        NtpTimeService time) : base(configHandler)
    {
        _service = service;
        _time = time;
        _anchor = TodayDate;
        _service.Changed += OnDocumentChanged;
        Rebuild();
    }

    /// <summary>节假日日历（由窗口在构造期注入；为空时按「不跳过」处理）。</summary>
    public IDutyHolidayCalendar? HolidayCalendar { get; set; }

    /// <summary>界面偏好（周起始日、默认视图、是否高亮今天……）。</summary>
    public DutySettingsConfig Settings => Config.DutySettings;

    /// <summary>今天是哪天：一律取校时后的本地时间，不用 DateTime.Now。</summary>
    private DateOnly TodayDate => DateOnly.FromDateTime(_time.NowLocal);

    /// <summary>窗口/测试用：注入「今天」以便固定版面与断言。</summary>
    public DateOnly TodayOverride { get; set; }

    private DateOnly Anchor => TodayOverride == default ? TodayDate : TodayOverride;

    private DateOnly _anchor;

    public ObservableCollection<DutyWeekHeaderItem> WeekHeaders { get; } = [];

    public ObservableCollection<DutyWeekRow> WeekRows { get; } = [];

    public ObservableCollection<DutyMonthCell> MonthCells { get; } = [];

    /// <summary>月视图 DataGrid 的行：6 行 × 7 天（<see cref="MonthCells" /> 按周切块）。</summary>
    public ObservableCollection<DutyMonthRow> MonthRows { get; } = [];

    /// <summary>月视图顶部那排「周一…周日」，跟着「周起始日」配置一起变。</summary>
    public ObservableCollection<string> MonthDayNames { get; } = [];

    public ObservableCollection<DutyDayDetailEntry> DayEntries { get; } = [];

    [ObservableProperty] private bool _isWeekView = true;

    [ObservableProperty] private string _rangeText = string.Empty;

    [ObservableProperty] private string _modeText = string.Empty;

    [ObservableProperty] private string _selectedDateText = string.Empty;

    [ObservableProperty] private string _selectedGroupText = string.Empty;

    [ObservableProperty] private string _selectedHintText = string.Empty;

    [ObservableProperty] private bool _isDayPanelOpen;

    [ObservableProperty] private DateOnly _selectedDate;

    /// <summary>文档里连一个小组都没有：界面上给一张「去设置」的引导卡。</summary>
    [ObservableProperty] private bool _isSetupRequired;

    /// <summary>切换周/月视图（工具栏的分段按钮）。</summary>
    [RelayCommand]
    private void ShowWeek()
    {
        if (IsWeekView)
            return;

        IsWeekView = true;
        Rebuild();
    }

    [RelayCommand]
    private void ShowMonth()
    {
        if (!IsWeekView)
            return;

        IsWeekView = false;
        Rebuild();
    }

    [RelayCommand]
    private void Previous()
    {
        Move(-1);
    }

    [RelayCommand]
    private void Next()
    {
        Move(1);
    }

    [RelayCommand]
    private void Today()
    {
        _anchor = Anchor;
        Rebuild();
    }

    /// <summary>点某一天：打开右侧日详情。</summary>
    [RelayCommand]
    private void SelectDate(DateOnly date)
    {
        if (date == default)
            return;

        SelectedDate = date;
        RefreshDayDetail();
        IsDayPanelOpen = true;
    }

    /// <summary>代码后置里点格子用（命令参数在 XAML 里传 DateOnly 不方便）。</summary>
    public void OpenDay(DateOnly date) => SelectDate(date);

    [RelayCommand]
    private void CloseDayPanel() => IsDayPanelOpen = false;

    /// <summary>日详情里那颗按钮：没有手动停值日时写「这天不值日」，已停时写「恢复自动」。</summary>
    [ObservableProperty] private string _dayOverrideButtonText = "这天不值日";

    /// <summary>可选小组（兑换 / 指定小组用）。</summary>
    public IReadOnlyList<DutyChoice> GroupChoices =>
        _service.Document.OrderedGroups.Select(group => new DutyChoice { Id = group.Id, Name = group.Name }).ToList();

    /// <summary>可选职务（个人兑换用）。</summary>
    public IReadOnlyList<DutyChoice> PostChoices =>
        _service.Document.OrderedPosts.Select(post => new DutyChoice { Id = post.Id, Name = post.Name }).ToList();

    /// <summary>可选人员（个人兑换 / 增设用）。</summary>
    public IReadOnlyList<DutyChoice> MemberChoices =>
        _service.ActiveMembers
            .OrderBy(member => member.Name)
            .Select(member => new DutyChoice { Id = member.RecordId, Name = member.Name })
            .ToList();

    /// <summary>当前是不是轮流模式（加罚对话框据此决定「顺延」是否可选）。</summary>
    public bool DocumentModeIsRotation =>
        _service.Document.Schedule.Mode == DutyScheduleMode.Rotation;

    /// <summary>手动停值日 / 恢复自动（落到文档的 DayOverrides，权重最高）。</summary>
    [RelayCommand]
    public void ToggleDayOverride()
    {
        if (SelectedDate == default)
            return;

        var existing = _service.Document.DayOverrides.Find(item => item.Date == SelectedDate);
        _service.SetDayOverride(
            SelectedDate,
            existing?.State == DutyDayOverrideState.Off ? DutyDayOverrideState.Auto : DutyDayOverrideState.Off,
            note: string.Empty);
        Rebuild();
    }

    /// <summary>指定这一天由某个小组扫（临时；<paramref name="permanent" /> = 从这天起一直如此）。</summary>
    public void ApplyGroupAssign(Guid groupId, bool permanent = false)
    {
        if (SelectedDate == default || groupId == Guid.Empty)
            return;

        _service.AddAdjustment(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.GroupAssign,
            Lifetime = permanent ? DutyLifetime.Permanent : DutyLifetime.Temporary,
            GroupId = groupId,
            AnchorDate = SelectedDate,
            Start = SelectedDate,
            End = permanent ? null : SelectedDate
        });
        Rebuild();
    }

    /// <summary>两个小组对调这一天（起止同一天时只是单日对调）。</summary>
    public void ApplyGroupSwap(Guid firstGroupId, Guid secondGroupId, bool permanent = false)
    {
        if (SelectedDate == default || firstGroupId == Guid.Empty || secondGroupId == Guid.Empty)
            return;

        _service.AddAdjustment(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.GroupSwap,
            Lifetime = permanent ? DutyLifetime.Permanent : DutyLifetime.Temporary,
            GroupId = firstGroupId,
            TargetGroupId = secondGroupId,
            AnchorDate = SelectedDate,
            Start = SelectedDate,
            End = permanent ? null : SelectedDate
        });
        Rebuild();
    }

    /// <summary>小组加罚：从这一天起再延长若干天。</summary>
    public void ApplyGroupPenalty(Guid groupId, int days, DutyPenaltyMode mode, bool permanent = false)
    {
        if (SelectedDate == default || groupId == Guid.Empty || days <= 0)
            return;

        _service.AddAdjustment(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.GroupPenalty,
            Lifetime = permanent ? DutyLifetime.Permanent : DutyLifetime.Temporary,
            GroupId = groupId,
            AnchorDate = SelectedDate,
            Start = SelectedDate,
            End = permanent ? null : SelectedDate,
            Days = Math.Clamp(days, 1, 14),
            PenaltyMode = mode
        });
        Rebuild();
    }

    /// <summary>个人兑换：这个职务上把 A 换成 B。</summary>
    public void ApplyPersonSwap(Guid postId, Guid fromMemberId, Guid toMemberId, bool permanent = false)
    {
        if (SelectedDate == default || postId == Guid.Empty || toMemberId == Guid.Empty)
            return;

        _service.AddAdjustment(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.PersonSwap,
            Lifetime = permanent ? DutyLifetime.Permanent : DutyLifetime.Temporary,
            PostId = postId,
            FromMemberId = fromMemberId == Guid.Empty ? null : fromMemberId,
            ToMemberId = toMemberId,
            AnchorDate = SelectedDate,
            Start = SelectedDate,
            End = permanent ? null : SelectedDate
        });
        Rebuild();
    }

    /// <summary>个人增设：这个职务上额外加一个人。</summary>
    public void ApplyPersonAdd(Guid postId, Guid memberId, bool permanent = false)
    {
        if (SelectedDate == default || postId == Guid.Empty || memberId == Guid.Empty)
            return;

        _service.AddAdjustment(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.PersonAdd,
            Lifetime = permanent ? DutyLifetime.Permanent : DutyLifetime.Temporary,
            PostId = postId,
            ToMemberId = memberId,
            AnchorDate = SelectedDate,
            Start = SelectedDate,
            End = permanent ? null : SelectedDate
        });
        Rebuild();
    }

    /// <summary>页面离开可视树时断开（VM 是 transient、服务是单例）。</summary>
    public void Detach() => _service.Changed -= OnDocumentChanged;

    /// <summary>文档被别处改过（设置页、历史撤销……）时重算整张表。</summary>
    private void OnDocumentChanged(object? sender, EventArgs e) => Rebuild();

    private void Move(int direction)
    {
        _anchor = IsWeekView
            ? _anchor.AddDays(7 * direction)
            : _anchor = new DateOnly(_anchor.Year, _anchor.Month, 1).AddMonths(direction);

        Rebuild();
    }

    private DayOfWeek ResolveWeekStart()
    {
        var value = Settings.WeekStart;
        return value is >= 0 and <= 6 ? (DayOfWeek)value : DayOfWeek.Monday;
    }

    private DateOnly ResolveWeekStartDate(DateOnly date)
    {
        var start = ResolveWeekStart();
        var delta = ((int)date.DayOfWeek - (int)start + 7) % 7;
        return date.AddDays(-delta);
    }

    /// <summary>按当前视图与日期重建表格内容。</summary>
    public void Rebuild()
    {
        IsSetupRequired = _service.Document.Groups.Count == 0;
        var scheduler = new DutyScheduler(_service.Document, HolidayCalendar);

        if (IsWeekView)
            BuildWeek(scheduler);
        else
            BuildMonth(scheduler);

        RefreshDayDetail();
    }

    private void BuildWeek(DutyScheduler scheduler)
    {
        var start = ResolveWeekStartDate(_anchor);
        var end = start.AddDays(6);
        var view = scheduler.BuildRange(start, end, Anchor);

        WeekHeaders.Clear();
        foreach (var day in view.Days)
        {
            WeekHeaders.Add(new DutyWeekHeaderItem
            {
                Date = day.Date,
                DayName = FormatDayName(day.Date.DayOfWeek),
                DateText = $"{day.Date.Month}/{day.Date.Day}",
                IsToday = day.HasMarker(DutyDayMarker.Today),
                IsOff = !day.HasDuty,
                Marker = ResolveMarkerText(day)
            });
        }

        WeekRows.Clear();

        // 第一行：这一周每天由哪个小组值日。
        WeekRows.Add(new DutyWeekRow
        {
            Title = "值日小组",
            IsGroupRow = true,
            Cells = view.Days.Select(day => new DutyWeekCell
            {
                Date = day.Date,
                Text = day.HasDuty ? day.GroupName : "不值日",
                IsToday = day.HasMarker(DutyDayMarker.Today),
                IsOff = !day.HasDuty,
                IsEmpty = string.IsNullOrEmpty(day.GroupName),
                Badge = ResolveCellBadge(day)
            }).ToList()
        });

        // 之后每个职务一行。
        foreach (var post in view.Posts)
        {
            var cells = new List<DutyWeekCell>(7);
            foreach (var day in view.Days)
            {
                var cell = day.Cells.FirstOrDefault(item => item.PostId == post.Id);
                var names = cell?.Entries.Select(entry => entry.Name).ToList() ?? [];
                var badges = cell?.Entries
                    .Select(entry => ResolveEntryBadge(entry.Kind))
                    .Where(badge => badge.Length > 0)
                    .Distinct()
                    .ToList() ?? [];

                cells.Add(new DutyWeekCell
                {
                    Date = day.Date,
                    Text = names.Count == 0 ? "—" : string.Join("、", names),
                    Badge = string.Join(" ", badges),
                    IsToday = day.HasMarker(DutyDayMarker.Today),
                    IsOff = !day.HasDuty,
                    IsEmpty = names.Count == 0
                });
            }

            WeekRows.Add(new DutyWeekRow
            {
                Title = post.Name,
                Cells = cells
            });
        }

        RangeText = $"{start.Month}月{start.Day}日 – {end.Month}月{end.Day}日";
        ModeText = _service.Document.Schedule.Mode == DutyScheduleMode.Rotation ? "小组轮流" : "固定周几";
    }

    private void BuildMonth(DutyScheduler scheduler)
    {
        var first = new DateOnly(_anchor.Year, _anchor.Month, 1);
        var start = ResolveWeekStartDate(first);
        var end = start.AddDays(41);
        var view = scheduler.BuildRange(start, end, Anchor);

        MonthDayNames.Clear();
        for (var i = 0; i < 7; i++)
            MonthDayNames.Add(FormatDayName(start.AddDays(i).DayOfWeek));

        MonthCells.Clear();
        foreach (var day in view.Days)
        {
            MonthCells.Add(new DutyMonthCell
            {
                Date = day.Date,
                DayText = day.Date.Day.ToString(System.Globalization.CultureInfo.CurrentCulture),
                GroupName = day.HasDuty ? day.GroupName : "不值日",
                Badge = ResolveCellBadge(day),
                IsToday = day.HasMarker(DutyDayMarker.Today),
                IsOutside = day.Date.Month != first.Month,
                IsOff = !day.HasDuty
            });
        }

        RangeText = $"{first.Year} 年 {first.Month} 月";
        ModeText = _service.Document.Schedule.Mode == DutyScheduleMode.Rotation ? "小组轮流" : "固定周几";

        // 月视图的 DataGrid 按「周」分行：42 格切成 6 行 × 7 列。
        MonthRows.Clear();
        for (var week = 0; week < 6; week++)
        {
            MonthRows.Add(new DutyMonthRow
            {
                Cells = MonthCells.Skip(week * 7).Take(7).ToList()
            });
        }
    }

    private void RefreshDayDetail()
    {
        if (SelectedDate == default)
            return;

        var scheduler = new DutyScheduler(_service.Document, HolidayCalendar);
        var day = scheduler.BuildDay(SelectedDate, Anchor);
        SelectedDateText = $"{SelectedDate.Year} 年 {SelectedDate.Month} 月 {SelectedDate.Day} 日 · "
                           + FormatDayName(SelectedDate.DayOfWeek);

        DayEntries.Clear();
        DayOverrideButtonText = _service.Document.DayOverrides.Exists(item =>
                                    item.Date == SelectedDate && item.State == DutyDayOverrideState.Off)
            ? "恢复自动"
            : "这天不值日";

        if (day is null)
            return;

        SelectedGroupText = day.HasDuty ? day.GroupName : "不值日";
        SelectedHintText = ResolveMarkerText(day);

        foreach (var cell in day.Cells)
        {
            foreach (var entry in cell.Entries)
            {
                DayEntries.Add(new DutyDayDetailEntry
                {
                    PostName = cell.PostName,
                    MemberName = entry.Name,
                    Badge = ResolveEntryBadge(entry.Kind)
                });
            }
        }
    }

    private string ResolveMarkerText(DutyDayPlan day)
    {
        var parts = new List<string>();
        if (day.HasMarker(DutyDayMarker.Today)) parts.Add("今天");
        if (day.HasMarker(DutyDayMarker.LegalHoliday)) parts.Add("法定假日");
        if (day.HasMarker(DutyDayMarker.MakeupWorkday)) parts.Add("调休补班");
        if (day.HasMarker(DutyDayMarker.Vacation)) parts.Add("假期");
        if (day.HasMarker(DutyDayMarker.DayOff)) parts.Add("手动停值日");
        if (day.HasMarker(DutyDayMarker.Penalty)) parts.Add("加罚");
        if (day.HasMarker(DutyDayMarker.Exchange)) parts.Add("兑换");
        if (day.HasMarker(DutyDayMarker.PersonExchange)) parts.Add("个人兑换");
        if (day.HasMarker(DutyDayMarker.NoGroupConfigured)) parts.Add("未配小组");
        if (day.HasMarker(DutyDayMarker.Unassigned)) parts.Add("缺人");
        if (day.HasMarker(DutyDayMarker.Conflict)) parts.Add("人员冲突");
        return string.Join(" · ", parts);
    }

    private static string ResolveCellBadge(DutyDayPlan day)
    {
        if (day.HasMarker(DutyDayMarker.Penalty)) return "罚";
        if (day.HasMarker(DutyDayMarker.Exchange)) return "换";
        if (day.HasMarker(DutyDayMarker.Unassigned)) return "缺";
        return string.Empty;
    }

    private static string ResolveEntryBadge(DutyEntryKind kind) => kind switch
    {
        DutyEntryKind.Fixed => "固",
        DutyEntryKind.Substitute => "换",
        DutyEntryKind.Added => "加",
        _ => string.Empty
    };

    private static string FormatDayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };
}

