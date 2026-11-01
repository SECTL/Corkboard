using System.Globalization;

namespace Corkboard.Core.Display;

/// <summary>
///     农历（阴历）那行字的格式化 —— 纯静态、无 UI 依赖，有单测。
///     <para>
///         看板模式的日期那行可以选「只公历 / 只农历 / 公历后面带农历」
///         （<see cref="Corkboard.Core.Enums.Configs.DisplayDateCalendar" />），农历那一半由这里拼。
///         输出<b>只到月日、不带年份</b>——时钟那行本来就窄，再挂个年份要折行。
///     </para>
///     <para>
///         ⚠️ 农历日历靠运行时的 ICU 数据：<c>InvariantGlobalization</c> 或裁掉 ICU 的发行版上
///         <see cref="ChineseLunisolarCalendar" /> 根本用不了。所以这里每一步都当「可能失败」处理：
///         <see cref="IsSupported" /> 说这台机器上能不能用，<see cref="Format" /> 拿不到就返回空串，
///         由调用方（<see cref="DisplayClockFormatter" />）退回公历那串。
///         <b>看板那行字不能因为农历不可用就空掉，更不能把时钟抛崩。</b>
///     </para>
/// </summary>
public static class LunarDateFormatter
{
    /// <summary>中文月份名，下标 0 是正月。闰月只是在前面多一个「闰」字，月名不变。</summary>
    private static readonly string[] ChineseMonths =
    [
        "正月", "二月", "三月", "四月", "五月", "六月",
        "七月", "八月", "九月", "十月", "冬月", "腊月"
    ];

    /// <summary>中文日名，下标 0 是初一。二十之后从「廿一」数到「廿九」，月底是「三十」。</summary>
    private static readonly string[] ChineseDays =
    [
        "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九",
        "二十", "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九",
        "三十"
    ];

    /// <summary>
    ///     运行时可用的农历日历；<c>null</c> 表示这台机器上农历用不了（构造或试读就抛异常）。
    ///     <para>
    ///         只造一次：<see cref="ChineseLunisolarCalendar" /> 是纯查表的无状态对象，
    ///         但每行日期都 new 一个没必要（看板每秒都在重算日期，日期一天才变一次）。
    ///     </para>
    /// </summary>
    private static readonly ChineseLunisolarCalendar? ChineseCalendar = CreateCalendar();

    /// <summary>本机运行时的 .NET 农历日历能不能用（ICU / <c>InvariantGlobalization</c> 下可能不可用）。</summary>
    public static bool IsSupported => ChineseCalendar is not null;

    /// <summary>
    ///     农历写法：中文界面「农历十月初八」，英文界面「Lunar month 10, day 8」；不支持时返回空串。
    /// </summary>
    /// <param name="date">公历日期（时钟那行显示的这一天）。</param>
    /// <param name="culture">判断用哪种语言拼；<c>null</c> 时用 <see cref="CultureInfo.CurrentCulture" />。</param>
    /// <returns>
    ///     农历串（中文前缀「农历」、英文前缀「Lunar」）；农历不可用、或日期超出农历支持范围
    ///     （1901-02-19 之前 / 2101-01-28 之后）时是 <see cref="string.Empty" />。
    /// </returns>
    public static string Format(DateOnly date, CultureInfo? culture = null)
    {
        if (ChineseCalendar is null)
            return string.Empty;

        culture ??= CultureInfo.CurrentCulture;

        int realMonth;
        int day;
        bool isLeapMonth;
        try
        {
            var value = date.ToDateTime(TimeOnly.MinValue);
            var lunarYear = ChineseCalendar.GetYear(value);
            day = ChineseCalendar.GetDayOfMonth(value);
            isLeapMonth = TryResolveMonth(ChineseCalendar, value, lunarYear, out realMonth);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or ArgumentException)
        {
            // 日期落在农历不支持的时间窗里（早于 1901-02-19 / 晚于 2101-01-28）就是走到这儿。
            return string.Empty;
        }

        if (realMonth is < 1 or > 12 || day is < 1 or > 30)
            return string.Empty;

        return culture.TwoLetterISOLanguageName == "zh"
            ? $"农历{(isLeapMonth ? "闰" : string.Empty)}{ChineseMonths[realMonth - 1]}{ChineseDays[day - 1]}"
            : $"Lunar {(isLeapMonth ? "leap month" : "month")} {realMonth}, day {day}";
    }

    /// <summary>
    ///     把 <see cref="ChineseLunisolarCalendar.GetMonth" /> 那个「一年 13 个槽位」的月号
    ///     折成真实的月份（1..12），并说出它是不是闰月。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="ChineseLunisolarCalendar.GetMonth(DateTime)" /> 在闰年返回 1..13，
    ///         而 <see cref="ChineseLunisolarCalendar.GetLeapMonth(int)" /> 给的是<b>闰月占的那个槽位号</b>
    ///         （1 基；0 = 当年不闰）。两者差一位，因为闰月是「重复前一个月」，占的是
    ///         「被闰月份 + 1」那个槽位：
    ///     </para>
    ///     <list type="bullet">
    ///         <item><c>month &lt; leapMonth</c>：闰月之前的正常月，真实月份就是 <c>month</c>。</item>
    ///         <item><c>month == leapMonth</c>：这个槽位自己就是闰月，真实月份是 <c>leapMonth - 1</c>。</item>
    ///         <item><c>month &gt; leapMonth</c>：闰月之后的月份整体被顶后了一位，真实月份要减 1。</item>
    ///     </list>
    ///     <para>
    ///         实测（本机 ICU，<c>GetLeapMonth</c> 的返回值 / 对应的闰月）：2023 → 3 = 闰二月，
    ///         2025 → 7 = 闰六月，2020 → 5 = 闰四月，2028 → 6 = 闰五月；2021 / 2022 / 2024 / 2026
    ///         返回 0（当年不闰）。拿 2023 年（闰二月）对一遍三种情形：2023-02-20 的 rawMonth 是 2
    ///         （闰月之前 → 二月初一）；2023-03-22 的 rawMonth 正好等于 3（闰月本身 → 闰二月初一）；
    ///         2023-04-20 的 rawMonth 是 4（闰月之后 → 真实月份 3，三月初一）。
    ///     </para>
    /// </remarks>
    /// <returns>闰月时返回 <c>true</c>；正常月（或当年不闰）返回 <c>false</c>。</returns>
    private static bool TryResolveMonth(
        ChineseLunisolarCalendar calendar,
        DateTime value,
        int lunarYear,
        out int realMonth)
    {
        var rawMonth = calendar.GetMonth(value);
        var leapMonth = calendar.GetLeapMonth(lunarYear);

        if (leapMonth == 0)
        {
            realMonth = rawMonth;
            return false;
        }

        if (rawMonth == leapMonth)
        {
            realMonth = leapMonth - 1;
            return true;
        }

        realMonth = rawMonth > leapMonth ? rawMonth - 1 : rawMonth;
        return false;
    }

    /// <summary>造一个能用的农历日历并试读一次；不支持（ICU 缺失 / InvariantGlobalization）时返回 <c>null</c>。</summary>
    /// <remarks>
    ///     构造与「读一次」都要试：有的运行时构造不抛、到真正查表时才
    ///     <see cref="PlatformNotSupportedException" />（裁掉 ICU 的发行版就是这种）。
    /// </remarks>
    private static ChineseLunisolarCalendar? CreateCalendar()
    {
        try
        {
            var calendar = new ChineseLunisolarCalendar();
            var probe = new DateTime(2024, 2, 10); // 农历甲辰年正月初一，落在支持范围内
            _ = calendar.GetYear(probe);
            _ = calendar.GetMonth(probe);
            _ = calendar.GetDayOfMonth(probe);
            _ = calendar.GetLeapMonth(calendar.GetYear(probe));
            return calendar;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
