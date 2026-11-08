namespace Corkboard.Core.DutyRoster;

/// <summary>
///     某职务的固定人员：在指定星期（可再限起止日期）由这个人负责该职务。
///     <para>
///         一个职务可以有多条记录，用来按星期分工——例：擦柜子由甲负责周一至周三、
///         乙负责周四至周五，就是两条记录。<see cref="Relation" /> 决定命中时是顶替
///         还是追加到组内成员旁边。
///     </para>
/// </summary>
public sealed class DutyFixedAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PostId { get; set; }

    public Guid MemberId { get; set; }

    /// <summary>命中的星期（位掩码，见 <see cref="DutyWeekdays" />）。</summary>
    public int Weekdays { get; set; } = DutyWeekdays.All;

    /// <summary>生效起日（含；空表示不限）。</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>生效止日（含；空表示不限）。</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>替换（默认）还是叠加。</summary>
    public DutyFixedRelation Relation { get; set; } = DutyFixedRelation.Replace;

    /// <summary>某一天这条固定记录是否生效（只看星期与日期区间，不看节假日）。</summary>
    public bool IsEffectiveOn(DateOnly date)
    {
        if (!DutyWeekdays.Contains(Weekdays, date.DayOfWeek))
            return false;

        if (StartDate is { } start && date < start)
            return false;

        if (EndDate is { } end && date > end)
            return false;

        return true;
    }
}
