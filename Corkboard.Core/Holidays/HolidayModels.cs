namespace Corkboard.Core.Holidays;

/// <summary>一天节假日数据（来自在线源或随包内置）。</summary>
public sealed class HolidayDay
{
    public DateOnly Date { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>true = 放假；false = 调休补班（照常值日）。</summary>
    public bool IsOffDay { get; set; }
}

/// <summary>
///     某一年的节假日数据。<c>papers</c> 是国务院公告链接，界面上「来源」要带上。
/// </summary>
public sealed class HolidayYearData
{
    public int Year { get; set; }

    /// <summary>数据源名（holiday-cn / chinese-days / built-in）。</summary>
    public string SourceName { get; set; } = "holiday-cn";

    /// <summary>数据源仓库地址，供「来源」展示。</summary>
    public string SourceUrl { get; set; } = "https://github.com/NateScarlet/holiday-cn";

    public string License { get; set; } = "MIT";

    /// <summary>国务院公告（每年可能多条）。</summary>
    public List<string> Papers { get; set; } = [];

    public DateTimeOffset FetchedAt { get; set; }

    public List<HolidayDay> Days { get; set; } = [];

    public HolidayDay? Find(DateOnly date) => Days.Find(day => day.Date == date);

    public int OffDayCount => Days.Count(day => day.IsOffDay);

    public int MakeupDayCount => Days.Count(day => !day.IsOffDay);

    public HolidayAttribution ToAttribution() => new()
    {
        SourceName = SourceName,
        SourceUrl = SourceUrl,
        License = License,
        Year = Year,
        DayCount = Days.Count,
        OffDayCount = OffDayCount,
        FetchedAt = FetchedAt,
        Papers = Papers.ToList()
    };
}

/// <summary>节假日「来源」信息：界面上必须标出来（数据不是我们编的）。</summary>
public sealed class HolidayAttribution
{
    public string SourceName { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public string License { get; set; } = string.Empty;

    public int Year { get; set; }

    public int DayCount { get; set; }

    public int OffDayCount { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public IReadOnlyList<string> Papers { get; set; } = [];
}

/// <summary>用户对某一天的覆盖。数字落盘，只能末尾增删。</summary>
public enum HolidayOverrideState
{
    /// <summary>不覆盖，按数据源判定。</summary>
    Auto = 0,

    /// <summary>强制放假（跳过值日）。</summary>
    Off = 1,

    /// <summary>强制补班（照常值日）。</summary>
    Work = 2,

    /// <summary>忽略数据源，退回周末规则。</summary>
    Ignore = 3
}

public sealed class HolidayOverride
{
    public DateOnly Date { get; set; }

    public HolidayOverrideState State { get; set; } = HolidayOverrideState.Auto;

    public string Note { get; set; } = string.Empty;
}

/// <summary>用户自建的假期区间（寒暑假、校历调整），始终跳过值日。</summary>
public sealed class HolidayVacationRange
{
    public DateOnly Start { get; set; }

    public DateOnly End { get; set; }

    public string Note { get; set; } = string.Empty;

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}

/// <summary>
///     用户覆盖：<c>data/holidays/overrides.json</c>。
///     逐日覆盖优先于数据源；假期区间优先于一切「法定」判定（但值日表里「这天值日」的覆盖还要更高一层）。
/// </summary>
public sealed class HolidayOverrides
{
    public List<HolidayOverride> Days { get; set; } = [];

    public List<HolidayVacationRange> Vacations { get; set; } = [];

    public HolidayOverrideState StateOf(DateOnly date) =>
        Days.Find(item => item.Date == date)?.State ?? HolidayOverrideState.Auto;

    public bool IsVacation(DateOnly date) => Vacations.Exists(range => range.Contains(date));

    public bool IsEmpty => Days.Count == 0 && Vacations.Count == 0;

    /// <summary>去重、修掉起止颠倒的区间，按日期排序。</summary>
    public void Normalize()
    {
        Days ??= [];
        Vacations ??= [];

        var byDate = new Dictionary<DateOnly, HolidayOverride>();
        foreach (var item in Days)
        {
            if (item is null || item.Date == default)
                continue;

            if (item.State is not (HolidayOverrideState.Auto
                or HolidayOverrideState.Off
                or HolidayOverrideState.Work
                or HolidayOverrideState.Ignore))
            {
                item.State = HolidayOverrideState.Auto;
            }

            item.Note = item.Note?.Trim() ?? string.Empty;
            byDate[item.Date] = item;
        }

        Days = byDate.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList();

        var vacations = new List<HolidayVacationRange>();
        foreach (var range in Vacations)
        {
            if (range is null || range.Start == default)
                continue;

            if (range.End == default || range.End < range.Start)
                range.End = range.Start;

            range.Note = range.Note?.Trim() ?? string.Empty;
            vacations.Add(range);
        }

        Vacations = vacations.OrderBy(range => range.Start).ToList();
    }
}
