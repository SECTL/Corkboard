using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.DutyRoster;
using Corkboard.Core.Holidays;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using Corkboard.Services.Desktop;
using Corkboard.Services.Holidays;

namespace Corkboard.ViewModels.Duty;

/// <summary>节假日表格里的一行（日期 / 星期 / 节日 / 类型 / 来源 / 操作）。</summary>
public sealed class HolidayRowItem
{
    public DateOnly Date { get; init; }

    public string DateText { get; init; } = string.Empty;

    public string WeekdayText { get; init; } = string.Empty;

    public string NameText { get; init; } = string.Empty;

    /// <summary>放假 / 补班 / 假期区间。</summary>
    public string TypeText { get; init; } = string.Empty;

    /// <summary>在线 / 内置 / 手动 / 周末。</summary>
    public string OriginText { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;

    public bool IsOffDay { get; init; }

    public bool CanMarkWork { get; init; }

    public bool CanMarkOff { get; init; }

    public bool CanRestore { get; init; }
}

/// <summary>假期区间列表里的一行。</summary>
public sealed class HolidayVacationRowItem
{
    public DateOnly Start { get; init; }

    public DateOnly End { get; init; }

    public string DateText { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;
}

/// <summary>影响预览的一行（未来 14 天值不值日）。</summary>
public sealed class HolidayImpactRowItem
{
    public string DateText { get; init; } = string.Empty;

    public string WeekdayText { get; init; } = string.Empty;

    public bool IsDuty { get; init; }

    /// <summary>值日 / 跳过。</summary>
    public string ActionText { get; init; } = string.Empty;

    /// <summary>为什么（正常值日 / 法定假日 / 周末 / 假期区间 / 调休补班……）。</summary>
    public string ReasonText { get; init; } = string.Empty;
}

/// <summary>
///     「节假日」独立窗口的 ViewModel：数据源状态、年份表格、假期区间、手动新增与未来 14 天影响预览。
///     <para>
///         合并链、缓存、刷新全在 <see cref="HolidayService" /> / Core 的 <see cref="HolidayCatalog" /> 里；
///         这里只做展示与转发，**不要**在 VM 里再写一套「哪天算放假」的判断——
///         影响预览也走 <see cref="DutyCalendar" />，保证和值日表算出来的完全一致。
///     </para>
///     <para>
///         VM 是 transient、节假日服务是单例：订阅在 <see cref="Attach" /> 里挂、<see cref="Detach" /> 里解，
///         否则每开一次窗口就多一份处理器。
///     </para>
///     <para>
///         ⚠️ 文案暂为硬编码中文，资源键见每个 TODO；改语言时统一换成 resx。
///     </para>
/// </summary>
public sealed partial class DutyHolidayViewModel : ViewModelBase
{
    private readonly HolidayService _holidays;
    private readonly DutyRosterService _roster;
    private readonly NtpTimeService _time;
    private readonly IExternalLauncher _launcher;

    private bool _attached;
    private bool _suppressYearReload;
    private string _paperUrl = string.Empty;
    private string _sourceUrl = string.Empty;
    private string _licenseUrl = string.Empty;

    public DutyHolidayViewModel(
        MainConfigHandler configHandler,
        HolidayService holidays,
        DutyRosterService roster,
        NtpTimeService time,
        IExternalLauncher launcher) : base(configHandler)
    {
        _holidays = holidays;
        _roster = roster;
        _time = time;
        _launcher = launcher;

        VacationStart = Today.ToDateTime(TimeOnly.MinValue);
        VacationEnd = Today.ToDateTime(TimeOnly.MinValue);
        ManualDate = Today.ToDateTime(TimeOnly.MinValue);

        EnsureYears(reload: false);
        Reload();
    }

    /// <summary>年份切换后要重建的内容都在监听这个事件。</summary>
    public ObservableCollection<int> Years { get; } = [];

    public ObservableCollection<HolidayRowItem> Rows { get; } = [];

    public ObservableCollection<HolidayVacationRowItem> Vacations { get; } = [];

    public ObservableCollection<HolidayImpactRowItem> Impact { get; } = [];

    [ObservableProperty] private int _selectedYear;

    [ObservableProperty] private string _sourceNameText = "—";

    [ObservableProperty] private string _sourceDetailText = "—";

    [ObservableProperty] private string _fetchedAtText = "—";

    [ObservableProperty] private string _lastCheckText = "—";

    [ObservableProperty] private string _licenseText = "MIT";

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private string _failureText = string.Empty;

    [ObservableProperty] private bool _showFailure;

    [ObservableProperty] private bool _showMissingBanner;

    [ObservableProperty] private string _missingBannerText = string.Empty;

    [ObservableProperty] private bool _hasPapers;

    [ObservableProperty] private bool _isRefreshing;

    [ObservableProperty] private DateTime? _vacationStart;

    [ObservableProperty] private DateTime? _vacationEnd;

    [ObservableProperty] private string _vacationNote = string.Empty;

    [ObservableProperty] private DateTime? _manualDate;

    [ObservableProperty] private int _manualKindIndex;

    [ObservableProperty] private string _manualNote = string.Empty;

    public DateOnly Today => _holidays.Today;

    partial void OnSelectedYearChanged(int value)
    {
        if (!_suppressYearReload)
            Reload();
    }

    /// <summary>挂上服务的 Changed 订阅（窗口 Opened 时调）。</summary>
    public void Attach()
    {
        if (_attached)
            return;

        _attached = true;
        _holidays.Changed += OnHolidayChanged;
        Reload();
    }

    /// <summary>断开订阅（窗口 Closed 时调）：服务是单例，不断开就会越挂越多。</summary>
    public void Detach()
    {
        if (!_attached)
            return;

        _attached = false;
        _holidays.Changed -= OnHolidayChanged;
    }

    private void OnHolidayChanged(object? sender, EventArgs e) => Reload();

    /// <summary>整块重建：年份下拉、来源卡、表格、假期区间、影响预览。</summary>
    public void Reload()
    {
        EnsureYears(reload: true);
        ReloadSource();
        ReloadRows();
        ReloadVacations();
        ReloadImpact();

        var status = _holidays.Status;
        IsRefreshing = status.IsRefreshing;
        // TODO(资源键: Settings_DutyHoliday_Status_Online / _Refreshing / _Failed) 状态行。
        StatusText = status.IsRefreshing
            ? "正在刷新…"
            : status.LastError is null
                ? status.LastSuccessUtc is { } success
                    ? $"上次成功：{FormatLocal(success)}"
                    : "还没有成功拉取过，正在使用内置 / 星期规则"
                : $"上次刷新失败：{status.LastError}（继续使用缓存）";
        ShowFailure = status.LastError is not null;
        FailureText = status.LastError ?? string.Empty;
        LastCheckText = status.LastCheckUtc is { } check ? FormatLocal(check) : "—";
    }

    private void EnsureYears(bool reload)
    {
        var current = Today.Year;
        var wanted = new SortedSet<int> { current - 1, current, current + 1 };

        foreach (var year in _holidays.OnlineYears.Keys)
            wanted.Add(year);
        foreach (var year in _holidays.BuiltinYears.Keys)
            wanted.Add(year);
        foreach (var item in _holidays.Catalog.Overrides.Days)
            wanted.Add(item.Date.Year);
        foreach (var range in _holidays.Catalog.Overrides.Vacations)
        {
            wanted.Add(range.Start.Year);
            wanted.Add(range.End.Year);
        }

        var ordered = wanted.OrderByDescending(year => year).ToList();
        if (Years.SequenceEqual(ordered))
        {
            if (SelectedYear == 0)
            {
                _suppressYearReload = true;
                SelectedYear = current;
                _suppressYearReload = false;
            }

            return;
        }

        _suppressYearReload = true;
        Years.Clear();
        foreach (var year in ordered)
            Years.Add(year);

        SelectedYear = ordered.Contains(SelectedYear) ? SelectedYear : current;
        _suppressYearReload = false;
        if (!reload)
            return;

        ReloadRows();
    }

    private void ReloadSource()
    {
        var attribution = _holidays.Catalog.GetAttribution(SelectedYear);
        HasPapers = false;
        _paperUrl = string.Empty;

        if (attribution is null)
        {
            SourceNameText = "—";
            SourceDetailText = $"{SelectedYear} 年暂无数据，按星期规则判断";
            FetchedAtText = "—";
            LicenseText = "MIT";
            _sourceUrl = HolidaySourceCatalog.HolidayCnRepoUrl;
            _licenseUrl = HolidaySourceCatalog.HolidayCnRepoUrl + "/blob/master/LICENSE";
            ShowMissingBanner = true;
            MissingBannerText = $"{SelectedYear} 年安排尚未发布，暂按星期规则";
            return;
        }

        SourceNameText = attribution.SourceName == HolidaySourceCatalog.BuiltinName
            ? "随包内置（holiday-cn）"
            : attribution.SourceName;
        SourceDetailText =
            $"{attribution.Year} 年 · {attribution.DayCount} 条（放假 {attribution.OffDayCount} · 补班 {attribution.DayCount - attribution.OffDayCount}）";
        FetchedAtText = attribution.FetchedAt == default ? "—" : FormatLocal(attribution.FetchedAt);
        LicenseText = attribution.License;
        _sourceUrl = string.IsNullOrWhiteSpace(attribution.SourceUrl)
            ? HolidaySourceCatalog.HolidayCnRepoUrl
            : attribution.SourceUrl;
        _licenseUrl = _sourceUrl.TrimEnd('/') + "/blob/master/LICENSE";
        _paperUrl = attribution.Papers.Count > 0 ? attribution.Papers[0] : string.Empty;
        HasPapers = _paperUrl.Length > 0;
        ShowMissingBanner = false;
        MissingBannerText = string.Empty;
    }

    private void ReloadRows()
    {
        Rows.Clear();
        foreach (var info in _holidays.Catalog.GetDays(SelectedYear))
        {
            var isVacation = info.IsVacation;
            // TODO(资源键: Settings_DutyHoliday_Type_Off / _Work / _Vacation / _Origin_* / _Empty)
            Rows.Add(new HolidayRowItem
            {
                Date = info.Date,
                DateText = info.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                WeekdayText = WeekdayName(info.Date),
                NameText = string.IsNullOrWhiteSpace(info.Name)
                    ? string.IsNullOrWhiteSpace(info.Note) ? "—" : info.Note
                    : info.Name,
                TypeText = isVacation ? "假期区间" : info.IsOffDay ? "放假" : "补班",
                OriginText = info.Origin switch
                {
                    HolidayOrigin.Manual => "手动",
                    HolidayOrigin.Online => "在线",
                    HolidayOrigin.Builtin => "内置",
                    HolidayOrigin.Weekend => "周末",
                    _ => "—"
                },
                Note = info.Note,
                IsOffDay = info.IsOffDay,
                CanMarkWork = info.IsOffDay || info.OverrideState != HolidayOverrideState.Work,
                CanMarkOff = !info.IsOffDay || info.OverrideState != HolidayOverrideState.Off,
                CanRestore = info.OverrideState != HolidayOverrideState.Auto
            });
        }
    }

    private void ReloadVacations()
    {
        Vacations.Clear();
        foreach (var range in _holidays.Vacations)
        {
            Vacations.Add(new HolidayVacationRowItem
            {
                Start = range.Start,
                End = range.End,
                DateText = $"{range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} — {range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
                Note = string.IsNullOrWhiteSpace(range.Note) ? "—" : range.Note
            });
        }
    }

    private void ReloadImpact()
    {
        Impact.Clear();
        var schedule = _roster.Document.Schedule;
        var overrides = _roster.Document.DayOverrides.ToDictionary(item => item.Date);
        var today = Today;

        for (var offset = 0; offset < 14; offset++)
        {
            var date = today.AddDays(offset);
            var duty = DutyCalendar.IsDutyDay(date, schedule, _holidays, overrides);
            Impact.Add(new HolidayImpactRowItem
            {
                DateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                WeekdayText = WeekdayName(date),
                IsDuty = duty,
                // TODO(资源键: Settings_DutyHoliday_Impact_Duty / _Skip)
                ActionText = duty ? "值日" : "跳过",
                ReasonText = DescribeImpact(date, duty)
            });
        }
    }

    private string DescribeImpact(DateOnly date, bool duty)
    {
        var catalog = _holidays.Catalog;
        if (!duty)
        {
            if (catalog.IsVacation(date))
                return "假期区间";

            var info = catalog.Resolve(date);
            if (info.IsOffDay)
                return info.Origin == HolidayOrigin.Weekend ? "周末" : "法定假日";

            return "排班规则";
        }

        if (catalog.IsMakeupWorkday(date))
            return "调休补班";

        return catalog.Resolve(date).Origin == HolidayOrigin.Manual ? "手动值日" : "正常值日";
    }

    // ── 用户操作（页面 code-behind 转发过来） ───────────────────────────────

    public void MarkOff(HolidayRowItem row) => ApplyOverride(row.Date, HolidayOverrideState.Off, row.Note);

    public void MarkWork(HolidayRowItem row) => ApplyOverride(row.Date, HolidayOverrideState.Work, row.Note);

    public void Restore(HolidayRowItem row) => ApplyOverride(row.Date, HolidayOverrideState.Auto, string.Empty);

    private void ApplyOverride(DateOnly date, HolidayOverrideState state, string note)
    {
        if (_holidays.SetOverride(date, state, note))
            return;

        StatusText = "写入节假日设置失败，请检查数据目录权限";
    }

    /// <summary>手动新增一天：日期 + 类型（放假 / 改为值日）+ 备注。</summary>
    public void AddManual()
    {
        var date = ManualDate is { } value ? DateOnly.FromDateTime(value) : Today;
        var state = ManualKindIndex == 0 ? HolidayOverrideState.Off : HolidayOverrideState.Work;

        if (!_holidays.SetOverride(date, state, ManualNote))
        {
            StatusText = "写入节假日设置失败，请检查数据目录权限";
            return;
        }

        ManualNote = string.Empty;
    }

    /// <summary>新增假期区间：起止都可空（空 = 今天）。</summary>
    public void AddVacation()
    {
        var start = VacationStart is { } startValue ? DateOnly.FromDateTime(startValue) : Today;
        var end = VacationEnd is { } endValue ? DateOnly.FromDateTime(endValue) : start;
        if (end < start)
            (start, end) = (end, start);

        if (!_holidays.AddVacation(start, end, VacationNote))
        {
            StatusText = "写入假期区间失败，请检查数据目录权限";
            return;
        }

        VacationNote = string.Empty;
    }

    public void RemoveVacation(HolidayVacationRowItem item)
    {
        if (!_holidays.RemoveVacation(item.Start, item.End))
            StatusText = "没有找到这段假期，可能已经被删掉了";
    }

    // ── 外链 ────────────────────────────────────────────────────────────────

    /// <summary>「查看国务院公告」：数据里 papers 第一条；没有就退回国务院政策页。</summary>
    public void OpenPaper()
    {
        var url = string.IsNullOrWhiteSpace(_paperUrl)
            ? HolidaySourceCatalog.GovernmentNoticeUrl
            : _paperUrl;
        _launcher.TryOpenUri(url);
    }

    public void OpenSource()
    {
        if (!string.IsNullOrWhiteSpace(_sourceUrl))
            _launcher.TryOpenUri(_sourceUrl);
    }

    public void OpenLicense()
    {
        if (!string.IsNullOrWhiteSpace(_licenseUrl))
            _launcher.TryOpenUri(_licenseUrl);
    }

    // ── 刷新 ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsRefreshing)
            return;

        IsRefreshing = true;
        StatusText = "正在刷新…";
        try
        {
            var result = await _holidays.RefreshAsync(force: true);
            StatusText = result.Busy
                ? "正在刷新…"
                : result.Success
                    ? $"已刷新 {result.UpdatedYears} 个年份"
                    : result.Message ?? "刷新失败，继续使用缓存";
        }
        catch (Exception)
        {
            StatusText = "刷新失败，继续使用缓存";
        }
        finally
        {
            IsRefreshing = false;
            Reload();
        }
    }

    // ── 展示辅助 ────────────────────────────────────────────────────────────

    private static string FormatLocal(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>星期几的短名。TODO(资源键: Settings_DutyHoliday_Weekday_Mon … _Sun)</summary>
    public static string WeekdayName(DateOnly date) => date.DayOfWeek switch
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
