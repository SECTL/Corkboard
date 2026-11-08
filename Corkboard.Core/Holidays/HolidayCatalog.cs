using Corkboard.Core.DutyRoster;

namespace Corkboard.Core.Holidays;

/// <summary>某一天的数据来自哪里。界面上「来源」一列要照这个标。</summary>
public enum HolidayOrigin
{
    /// <summary>没有任何数据，也不在周末（普通工作日）。</summary>
    None = 0,

    /// <summary>数据源里没有这一天，按周末规则判。</summary>
    Weekend = 1,

    /// <summary>随包内置数据。</summary>
    Builtin = 2,

    /// <summary>在线缓存数据。</summary>
    Online = 3,

    /// <summary>用户在节假日窗口里手动设置（放假 / 值日 / 忽略数据源）。</summary>
    Manual = 4
}

/// <summary>合并链算出来的某一天：表里的日期、类型与来源都从这里来。</summary>
public sealed class HolidayDayInfo
{
    public DateOnly Date { get; init; }

    /// <summary>节日名；数据源没有（手动新增、普通周末）就是空串。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>true = 放假（不值日）；false = 补班（照常值日）。</summary>
    public bool IsOffDay { get; init; }

    public HolidayOrigin Origin { get; init; }

    /// <summary>用户覆盖状态；没有覆盖是 <see cref="HolidayOverrideState.Auto" />。</summary>
    public HolidayOverrideState OverrideState { get; init; } = HolidayOverrideState.Auto;

    /// <summary>用户覆盖的备注（手动新增时界面拿它当名字）。</summary>
    public string Note { get; init; } = string.Empty;

    public bool IsVacation { get; init; }

    /// <summary>是不是数据源里真实存在的一天（不是星期规则兜出来的）。</summary>
    public bool HasSourceData => Origin is HolidayOrigin.Online or HolidayOrigin.Builtin or HolidayOrigin.Manual;
}

/// <summary>
///     节假日合并链：在线缓存 → 随包内置 → 用户覆盖 → 星期规则，最终回答「这天值不值日」。
///     <para>
///         优先级（<c>.tmp/duty-roster-plan.md</c> §7）：
///         用户覆盖（off / work / ignore）&gt; 在线缓存 &gt; 随包内置 &gt; 星期规则（周六周日休）。
///         用户覆盖是 <see cref="HolidayOverrideState.Ignore" /> 时**整条数据链都跳过**，直接退回星期规则。
///     </para>
///     <para>
///         数据按年懒合并：<c>online[Y]</c> 与 <c>online[Y+1]</c> 取并集（跨年的 1 月 / 12 月日期
///         可能写在次年文件里；同一天两边都有时以更晚的 <c>Y+1</c> 为准，因为它更新）。
///     </para>
///     <para>
///         这个类就是 <see cref="IDutyHolidayCalendar" /> 的实现：值日表拿它判「法定假日 / 补班 / 假期区间」。
///         构造后不可变；用户改了覆盖由上层重新造一个（<c>HolidayService</c> 负责）。
///     </para>
/// </summary>
public sealed class HolidayCatalog : IDutyHolidayCalendar
{
    private readonly Dictionary<int, Dictionary<DateOnly, Entry>> _merged = [];

    public HolidayCatalog(
        HolidayOverrides? overrides = null,
        IReadOnlyDictionary<int, HolidayYearData>? online = null,
        IReadOnlyDictionary<int, HolidayYearData>? builtin = null)
    {
        Overrides = overrides ?? new HolidayOverrides();
        Online = online ?? new Dictionary<int, HolidayYearData>();
        Builtin = builtin ?? new Dictionary<int, HolidayYearData>();
    }

    public HolidayOverrides Overrides { get; }

    /// <summary>在线缓存（年份 → 数据）。</summary>
    public IReadOnlyDictionary<int, HolidayYearData> Online { get; }

    /// <summary>随包内置（年份 → 数据）。</summary>
    public IReadOnlyDictionary<int, HolidayYearData> Builtin { get; }

    /// <summary>周六 / 周日按「休」算（数据缺失时的兜底规则）。</summary>
    public static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>某天算不算「法定节假日」（数据缺失时周末也算休，值日表据此跳过）。</summary>
    public bool IsLegalHoliday(DateOnly date) => Resolve(date).IsOffDay;

    /// <summary>某天算不算「调休补班」：只有数据/手动画成「上班」才算，周末兜底不算。</summary>
    public bool IsMakeupWorkday(DateOnly date)
    {
        var info = Resolve(date);
        return !info.IsOffDay && info.Origin is HolidayOrigin.Online or HolidayOrigin.Builtin or HolidayOrigin.Manual;
    }

    /// <summary>用户自建的假期区间（寒暑假、校历调整）。</summary>
    public bool IsVacation(DateOnly date) => Overrides.IsVacation(date);

    /// <summary>这天有没有任何可用的数据源 / 手动记录（不含周末兜底）。</summary>
    public bool HasData(int year) => GetMerged(year).Count > 0 || HasManualOverrideIn(year);

    /// <summary>这年的数据源里有几天的原始数据（在线优先，内置兜底）。</summary>
    public int SourceDayCount(int year) => GetSourceYear(year)?.Days.Count ?? 0;

    /// <summary>某年的原始数据（在线优先、其次内置）；都没有返回 null。</summary>
    public HolidayYearData? GetSourceYear(int year)
    {
        if (Online.TryGetValue(year, out var online) && online.Days.Count > 0)
            return online;

        return Builtin.TryGetValue(year, out var builtin) && builtin.Days.Count > 0 ? builtin : null;
    }

    /// <summary>界面上「来源」卡片的标注；没有数据时返回 null（页面改成「暂按星期规则」）。</summary>
    public HolidayAttribution? GetAttribution(int year)
    {
        if (Online.TryGetValue(year, out var online) && online.Days.Count > 0)
            return online.ToAttribution();

        if (Builtin.TryGetValue(year, out var builtin) && builtin.Days.Count > 0)
        {
            var attribution = builtin.ToAttribution();
            attribution.SourceName = HolidaySourceCatalog.BuiltinName;
            return attribution;
        }

        return null;
    }

    /// <summary>
    ///     把「某天的判定」连同来源一起算出来。
    ///     <para>永远返回非 null：没有数据的工作日就是普通一天（<see cref="HolidayOrigin.None" />）。</para>
    /// </summary>
    public HolidayDayInfo Resolve(DateOnly date)
    {
        var state = Overrides.StateOf(date);
        var note = Overrides.Days.Find(item => item.Date == date)?.Note ?? string.Empty;
        var vacation = Overrides.IsVacation(date);

        if (state == HolidayOverrideState.Off)
        {
            return new HolidayDayInfo
            {
                Date = date,
                Name = GetMerged(date.Year).GetValueOrDefault(date)?.Day.Name ?? string.Empty,
                IsOffDay = true,
                Origin = HolidayOrigin.Manual,
                OverrideState = state,
                Note = note,
                IsVacation = vacation
            };
        }

        if (state == HolidayOverrideState.Work)
        {
            return new HolidayDayInfo
            {
                Date = date,
                Name = GetMerged(date.Year).GetValueOrDefault(date)?.Day.Name ?? string.Empty,
                IsOffDay = false,
                Origin = HolidayOrigin.Manual,
                OverrideState = state,
                Note = note,
                IsVacation = vacation
            };
        }

        if (state == HolidayOverrideState.Ignore)
        {
            return new HolidayDayInfo
            {
                Date = date,
                IsOffDay = IsWeekend(date),
                Origin = HolidayOrigin.Manual,
                OverrideState = state,
                Note = note,
                IsVacation = vacation
            };
        }

        if (GetMerged(date.Year).TryGetValue(date, out var entry))
        {
            return new HolidayDayInfo
            {
                Date = date,
                Name = entry.Day.Name,
                IsOffDay = entry.Day.IsOffDay,
                Origin = entry.Origin,
                IsVacation = vacation
            };
        }

        return new HolidayDayInfo
        {
            Date = date,
            IsOffDay = IsWeekend(date),
            Origin = IsWeekend(date) ? HolidayOrigin.Weekend : HolidayOrigin.None,
            IsVacation = vacation
        };
    }

    /// <summary>
    ///     某年要显示在表里的全部日期：合并后的数据日 + 手动新增的覆盖日（不含普通周末与普通工作日）。
    ///     已按日期升序排好。
    /// </summary>
    public IReadOnlyList<HolidayDayInfo> GetDays(int year)
    {
        var dates = new SortedSet<DateOnly>();
        foreach (var date in GetMerged(year).Keys)
        {
            if (date.Year == year)
                dates.Add(date);
        }

        foreach (var item in Overrides.Days)
        {
            if (item.Date.Year == year && item.State != HolidayOverrideState.Auto)
                dates.Add(item.Date);
        }

        var result = new List<HolidayDayInfo>(dates.Count);
        foreach (var date in dates)
        {
            var info = Resolve(date);
            // 手动「忽略数据源」且数据源本来就没有这一天时，不往表里塞普通工作日/周末。
            if (info.Origin == HolidayOrigin.Manual
                && info.OverrideState == HolidayOverrideState.Ignore
                && !GetMerged(year).ContainsKey(date))
            {
                continue;
            }

            result.Add(info);
        }

        return result;
    }

    private bool HasManualOverrideIn(int year) =>
        Overrides.Days.Exists(item => item.Date.Year == year && item.State != HolidayOverrideState.Auto);

    private Dictionary<DateOnly, Entry> GetMerged(int year)
    {
        if (_merged.TryGetValue(year, out var cached))
            return cached;

        var map = new Dictionary<DateOnly, Entry>();
        AddTier(map, Builtin, year, HolidayOrigin.Builtin);
        AddTier(map, Builtin, year + 1, HolidayOrigin.Builtin);
        AddTier(map, Online, year, HolidayOrigin.Online);
        AddTier(map, Online, year + 1, HolidayOrigin.Online);

        _merged[year] = map;
        return map;
    }

    private static void AddTier(
        Dictionary<DateOnly, Entry> map,
        IReadOnlyDictionary<int, HolidayYearData> source,
        int year,
        HolidayOrigin origin)
    {
        if (!source.TryGetValue(year, out var data))
            return;

        foreach (var day in data.Days)
            map[day.Date] = new Entry(day, origin);
    }

    private sealed record Entry(HolidayDay Day, HolidayOrigin Origin);
}
