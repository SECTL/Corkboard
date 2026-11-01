using System.Globalization;
using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Display;

/// <summary>时钟那一块要显示的四段文字：主时间、秒、上午/下午、日期。</summary>
/// <param name="Main">主时间：「21:42」/「9:42」。</param>
/// <param name="Seconds">秒：「07」；不显示秒时是空串。</param>
/// <param name="Period">上午/下午（AM/PM）；24 小时制时是空串。</param>
/// <param name="Date">日期那行；不显示日期时是空串。</param>
public readonly record struct DisplayClockText(string Main, string Seconds, string Period, string Date)
{
    /// <summary>「21:42:07」——主时间、冒号与秒连成的一整串（考试看板上那个钟的写法）。</summary>
    /// <remarks>
    ///     看板模式「大秒」那一档（默认）**整串一次画出来**：同一个 TextBlock、同一个字重，
    ///     中间那个冒号也在里面。以前主时间与秒是两个 TextBlock 并排拼的，秒那一段没带冒号、
    ///     也没带主时间的 <c>FontWeight</c>，拼出来是「04:03 47」——两截、还一粗一细
    ///     （用户截图反馈过）。只有「小秒」那一档才拆成尾巴，冒号仍留在主时间身上。
    /// </remarks>
    public string Combined => Seconds.Length == 0 ? Main : $"{Main}:{Seconds}";
}

/// <summary>
///     看板模式时钟 / 日期的文字格式化 —— 纯函数，有单测。
///     <para>
///         时间制式、显不显示秒、日期写法全在这里算，全屏窗口与设置页的预览浮窗读同一份，
///         <b>不要在 XAML 或 ViewModel 里另写一个格式串</b>（曾经写死成「{0}年{1}月{2}日」，
///         英文界面里也是中文）。
///     </para>
///     <para>
///         日期一律走 <see cref="CultureInfo.CurrentCulture" />：中文出「2026年10月18日星期日」，
///         英文出「Sunday, October 18, 2026」。
///     </para>
///     <para>
///         历法由 <see cref="DisplayDateCalendar" /> 选：只公历 / 只农历 / 公历后面带农历
///         （<c>公历 · 农历</c>，中间是空格 + U+00B7 + 空格）。农历那半由
///         <see cref="LunarDateFormatter" /> 拼，<b>农历不可用时一律退回公历那一串</b>
///         （见 <see cref="LunarDateFormatter.IsSupported" />）。
///     </para>
/// </summary>
public static class DisplayClockFormatter
{
    /// <summary>把「现在」格式化成时钟那块要的四段文字。</summary>
    public static DisplayClockText Format(
        DateTime now,
        DisplayClockFormat clockFormat,
        bool showSeconds,
        bool showDate,
        DisplayDateStyle dateStyle,
        CultureInfo? culture = null,
        DisplayDateCalendar calendar = DisplayDateCalendar.Gregorian)
    {
        culture ??= CultureInfo.CurrentCulture;

        var twelveHour = clockFormat == DisplayClockFormat.TwelveHour;
        var main = now.ToString(twelveHour ? "h:mm" : "HH:mm", culture);
        var seconds = showSeconds ? now.ToString("ss", culture) : string.Empty;
        var period = twelveHour
            ? now.Hour < 12
                ? culture.DateTimeFormat.AMDesignator
                : culture.DateTimeFormat.PMDesignator
            : string.Empty;
        var date = showDate
            ? FormatDate(DateOnly.FromDateTime(now), dateStyle, culture, calendar)
            : string.Empty;

        return new DisplayClockText(main, seconds, period, date);
    }

    /// <summary>只格式化日期那行（设置页预览也读它）。</summary>
    /// <param name="date">要显示的日期。</param>
    /// <param name="dateStyle">公历那串的写法（完整 / 只到月日 / 数字）。</param>
    /// <param name="culture">跟着界面语言走；<c>null</c> 时用当前文化。</param>
    /// <param name="calendar">
    ///     用哪种历法：只公历 / 只农历 / 公历后面带农历。
    ///     选农历而农历不可用时退回公历那串——宁可只剩公历，也不能让日期那行空掉。
    /// </param>
    public static string FormatDate(
        DateOnly date,
        DisplayDateStyle dateStyle,
        CultureInfo? culture = null,
        DisplayDateCalendar calendar = DisplayDateCalendar.Gregorian)
    {
        culture ??= CultureInfo.CurrentCulture;
        var weekday = culture.DateTimeFormat.GetDayName(date.DayOfWeek);

        var gregorian = dateStyle switch
        {
            DisplayDateStyle.MonthDay => $"{date.ToString("M", culture)} {weekday}",
            DisplayDateStyle.Numeric => $"{date.ToString("d", culture)} {weekday}",
            _ => date.ToString("D", culture)
        };

        if (calendar == DisplayDateCalendar.Gregorian)
            return gregorian;

        // 空串 = 这台机器上农历用不了（ICU / InvariantGlobalization）或日期超出农历支持范围。
        var lunar = LunarDateFormatter.Format(date, culture);
        if (lunar.Length == 0)
            return gregorian;

        return calendar == DisplayDateCalendar.Lunar ? lunar : $"{gregorian} · {lunar}";
    }

    /// <summary>设置页里那个预览样本：「21:42」这类，用当前时刻拼一份。</summary>
    public static DisplayClockText Sample(DateTime now, bool showSeconds, DisplayClockFormat clockFormat) =>
        Format(now, clockFormat, showSeconds, false, DisplayDateStyle.Full);
}
