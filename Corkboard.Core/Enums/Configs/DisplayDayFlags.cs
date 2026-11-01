namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     自动进出的「星期几」——一个时段可以在固定几天生效。
///     <para>
///         落盘是数字（位掩码），所以已有的位值<b>不能改</b>，新值只能往上加。
///         <see cref="EveryDay" /> 是默认值：不设星期就是每天。
///     </para>
/// </summary>
[Flags]
public enum DisplayDayFlags
{
    /// <summary>一天都不选（会被归一化成 <see cref="EveryDay" />：零长度的重复没有意义）。</summary>
    None = 0,

    /// <summary>周一。</summary>
    Monday = 1 << 0,

    /// <summary>周二。</summary>
    Tuesday = 1 << 1,

    /// <summary>周三。</summary>
    Wednesday = 1 << 2,

    /// <summary>周四。</summary>
    Thursday = 1 << 3,

    /// <summary>周五。</summary>
    Friday = 1 << 4,

    /// <summary>周六。</summary>
    Saturday = 1 << 5,

    /// <summary>周日。</summary>
    Sunday = 1 << 6,

    /// <summary>周一到周五。</summary>
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,

    /// <summary>周六、周日。</summary>
    Weekend = Saturday | Sunday,

    /// <summary>每一天（默认）。</summary>
    EveryDay = Weekdays | Weekend
}

/// <summary>星期位掩码与具体日期之间的换算，全项目只此一份。</summary>
public static class DisplayDayFlagsExtensions
{
    /// <summary>所有合法的位。</summary>
    public const DisplayDayFlags All = DisplayDayFlags.EveryDay;

    /// <summary>按 ISO 顺序列出七天（周一在前），设置页照着排按钮。</summary>
    public static readonly IReadOnlyList<DisplayDayFlags> Ordered =
    [
        DisplayDayFlags.Monday,
        DisplayDayFlags.Tuesday,
        DisplayDayFlags.Wednesday,
        DisplayDayFlags.Thursday,
        DisplayDayFlags.Friday,
        DisplayDayFlags.Saturday,
        DisplayDayFlags.Sunday
    ];

    /// <summary>某个 <see cref="DayOfWeek" /> 对应的位。</summary>
    public static DisplayDayFlags ForDayOfWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => DisplayDayFlags.Monday,
        DayOfWeek.Tuesday => DisplayDayFlags.Tuesday,
        DayOfWeek.Wednesday => DisplayDayFlags.Wednesday,
        DayOfWeek.Thursday => DisplayDayFlags.Thursday,
        DayOfWeek.Friday => DisplayDayFlags.Friday,
        DayOfWeek.Saturday => DisplayDayFlags.Saturday,
        _ => DisplayDayFlags.Sunday
    };

    /// <summary>某一天对应的位。</summary>
    public static DisplayDayFlags ForDate(DateOnly date) => ForDayOfWeek(date.DayOfWeek);

    /// <summary>把脏值折成合法掩码：一位都没选、或者带了不存在的位，都当成「每天」。</summary>
    public static DisplayDayFlags Normalize(DisplayDayFlags flags)
    {
        var value = (int)flags;
        var all = (int)All;
        return (value & ~all) != 0 || (value & all) == 0 ? All : flags;
    }

    /// <summary>这一天在不在选中范围里（脏值按「每天」算）。</summary>
    public static bool Includes(DisplayDayFlags flags, DateOnly date) =>
        (Normalize(flags) & ForDate(date)) != 0;
}
