namespace Corkboard.Core.DutyRoster;

/// <summary>值日日期判定：把「星期规则 + 节假日 + 补班 + 手动覆盖」合成一个布尔值。</summary>
public static class DutyCalendar
{
    /// <summary>
    ///     某天是否值日。
    ///     <para>
    ///         优先级：手动覆盖 &gt; 用户假期区间 &gt; 法定节假日 &gt; 调休补班 &gt; 星期规则。
    ///         补班日即使不在 <see cref="DutyScheduleConfig.Weekdays" /> 里也照常值日；
    ///         把 <see cref="DutyScheduleConfig.WorkOnMakeupDays" /> 关掉就退回星期规则。
    ///     </para>
    /// </summary>
    public static bool IsDutyDay(
        DateOnly date,
        DutyScheduleConfig schedule,
        IDutyHolidayCalendar? holidays = null,
        IReadOnlyDictionary<DateOnly, DutyDayOverride>? overrides = null)
    {
        if (overrides is not null && overrides.TryGetValue(date, out var overridden))
        {
            if (overridden.State == DutyDayOverrideState.Duty)
                return true;
            if (overridden.State == DutyDayOverrideState.Off)
                return false;
        }

        holidays ??= NullDutyHolidayCalendar.Instance;

        // 用户自建假期（寒暑假）不受「跳过法定节假日」开关影响。
        if (holidays.IsVacation(date))
            return false;

        if (schedule.SkipLegalHolidays && holidays.IsLegalHoliday(date))
            return false;

        if (schedule.WorkOnMakeupDays && holidays.IsMakeupWorkday(date))
            return true;

        return DutyWeekdays.Contains(schedule.Weekdays, date.DayOfWeek);
    }

    /// <summary>枚举 [from, to] 里所有值日日期。</summary>
    public static List<DateOnly> EnumerateDutyDays(
        DateOnly from,
        DateOnly to,
        DutyScheduleConfig schedule,
        IDutyHolidayCalendar? holidays = null,
        IReadOnlyDictionary<DateOnly, DutyDayOverride>? overrides = null)
    {
        var days = new List<DateOnly>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (IsDutyDay(date, schedule, holidays, overrides))
                days.Add(date);
        }

        return days;
    }

    /// <summary>取 [from, to] 里第一个值日日期（没有则为 null）。</summary>
    public static DateOnly? FirstDutyDay(
        DateOnly from,
        DateOnly to,
        DutyScheduleConfig schedule,
        IDutyHolidayCalendar? holidays = null,
        IReadOnlyDictionary<DateOnly, DutyDayOverride>? overrides = null)
    {
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (IsDutyDay(date, schedule, holidays, overrides))
                return date;
        }

        return null;
    }
}
