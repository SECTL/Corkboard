namespace Corkboard.Core.DutyRoster;

/// <summary>
///     排班模式。
///     <para>
///         枚举按数字落盘（<c>ConfigServiceBase.JsonOptions</c> 没有字符串枚举转换器），
///         所以只能在末尾追加成员，不能插入或调换顺序。
///     </para>
/// </summary>
public enum DutyScheduleMode
{
    /// <summary>固定周几：周一…周日各自绑一个小组。</summary>
    FixedWeekday = 0,

    /// <summary>小组轮流：按小组顺序逐段轮换。</summary>
    Rotation = 1
}

/// <summary>加罚（延长天数）在排班上的处理方式。</summary>
public enum DutyPenaltyMode
{
    /// <summary>吞掉：受罚组占用其后的值日段，被占的小组这一段不扫（固定模式只有这一种）。</summary>
    Swallow = 0,

    /// <summary>后推：受罚组这一段变长，其后所有小组整体后移（仅轮流模式可选）。</summary>
    PushBack = 1
}

/// <summary>手动调整的生命周期。</summary>
public enum DutyLifetime
{
    /// <summary>临时：只在起止日期内生效，默认值。</summary>
    Temporary = 0,

    /// <summary>永久：没有结束日期；兑换类永久调整会同时改写规则并留审计。</summary>
    Permanent = 1
}

/// <summary>手动调整的种类。</summary>
public enum DutyAdjustmentKind
{
    /// <summary>某天由指定小组打扫（临时指定 / 代班）。</summary>
    GroupAssign = 0,

    /// <summary>两个小组对调它们各自的值日段。</summary>
    GroupSwap = 1,

    /// <summary>小组加罚（延长天数）。</summary>
    GroupPenalty = 2,

    /// <summary>个人兑换：某职务上把 A 换成 B。</summary>
    PersonSwap = 3,

    /// <summary>个人增设：某职务上额外加一个人（不替换原人）。</summary>
    PersonAdd = 4
}

/// <summary>某一天是否值日的手动覆盖状态。</summary>
public enum DutyDayOverrideState
{
    /// <summary>跟随规则（默认）。</summary>
    Auto = 0,

    /// <summary>强制值日（即使落在节假日/假期区间）。</summary>
    Duty = 1,

    /// <summary>强制不值日（即使落在补班日/正常星期）。</summary>
    Off = 2
}

/// <summary>固定人员与该职务原本的组内分配之间的关系。</summary>
public enum DutyFixedRelation
{
    /// <summary>替换：命中的星期里，该职务只显示固定人员（默认）。</summary>
    Replace = 0,

    /// <summary>叠加：固定人员与该职务的组内成员同时出现。</summary>
    Additional = 1
}

/// <summary>值日表窗口的默认视图。</summary>
public enum DutyViewMode
{
    Week = 0,
    Month = 1
}

/// <summary>
///     星期位掩码工具：<c>bit0 = 周一</c> … <c>bit6 = 周日</c>。
///     <para>
///         和 <see cref="DayOfWeek" /> 的编号（周日 = 0）不同，转换一律走这里，
///         不要在各处自己写 <c>1 &lt;&lt; (int)day</c>。
///     </para>
/// </summary>
public static class DutyWeekdays
{
    /// <summary>周一至周日全选。</summary>
    public const int All = 0b1111111;

    /// <summary>周一至周五（默认值日星期）。</summary>
    public const int Workdays = 0b0011111;

    public static int ToBit(DayOfWeek day) => 1 << (((int)day + 6) % 7);

    public static bool Contains(int mask, DayOfWeek day) => (mask & ToBit(day)) != 0;

    public static int FromDays(IEnumerable<DayOfWeek> days)
    {
        var mask = 0;
        foreach (var day in days)
            mask |= ToBit(day);
        return mask;
    }

    /// <summary>按周一…周日的顺序枚举掩码里的星期。</summary>
    public static IEnumerable<DayOfWeek> Enumerate(int mask)
    {
        for (var bit = 0; bit < 7; bit++)
        {
            if ((mask & (1 << bit)) == 0)
                continue;

            // bit0 = 周一（DayOfWeek.Monday = 1）… bit6 = 周日（0）。
            yield return bit == 6 ? DayOfWeek.Sunday : (DayOfWeek)(bit + 1);
        }
    }

    /// <summary>去掉越界位；为 0 时回落到默认工作日。</summary>
    public static int Normalize(int mask)
    {
        mask &= All;
        return mask == 0 ? Workdays : mask;
    }

    /// <summary>该掩码里第一个值日星期（找不到时回落到周一）。</summary>
    public static DayOfWeek First(int mask)
    {
        foreach (var day in Enumerate(mask))
            return day;
        return DayOfWeek.Monday;
    }
}
