namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     看板模式时钟的制式。枚举按数字落盘，<b>只能在末尾追加成员</b>。
/// </summary>
public enum DisplayClockFormat
{
    /// <summary>24 小时制（「21:42」）。<b>默认</b>。</summary>
    TwentyFourHour = 0,

    /// <summary>12 小时制（「9:42」＋「下午」/「PM」）。</summary>
    TwelveHour = 1
}

/// <summary>
///     时钟下面那行日期的写法。枚举按数字落盘，<b>只能在末尾追加成员</b>。
/// </summary>
public enum DisplayDateStyle
{
    /// <summary>完整日期：「2026年10月18日星期日」/「Sunday, October 18, 2026」。<b>默认</b>。</summary>
    Full = 0,

    /// <summary>只到月日：「10月18日 星期日」。屏幕窄的时候不占地方。</summary>
    MonthDay = 1,

    /// <summary>数字日期：「2026/10/18 星期日」。</summary>
    Numeric = 2
}

/// <summary>
///     看板模式里那张作业板展示哪些作业。枚举按数字落盘，<b>只能在末尾追加成员</b>。
/// </summary>
public enum DisplayBoardScope
{
    /// <summary>全部作业（跟作业板上看到的一致）。<b>默认</b>。</summary>
    All = 0,

    /// <summary>只要今天到期（含已经逾期没清掉的），展示时把别的先收起来。</summary>
    DueToday = 1
}

/// <summary>
///     时钟下面那行日期用哪种历法。枚举按数字落盘，<b>只能在末尾追加成员</b>。
/// </summary>
public enum DisplayDateCalendar
{
    /// <summary>只公历（「2026年10月18日星期日」）。<b>默认</b>。</summary>
    Gregorian = 0,

    /// <summary>只农历（「农历九月初八」）；农历不可用时退回公历。</summary>
    Lunar = 1,

    /// <summary>公历后面带农历（「2026年10月18日星期日 · 农历九月初八」）；农历不可用时只剩公历。</summary>
    GregorianAndLunar = 2
}
