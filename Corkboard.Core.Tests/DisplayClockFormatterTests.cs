using System.Globalization;
using Corkboard.Core.Display;
using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Tests;

/// <summary>
///     钟面那几行字怎么拼：12 / 24 小时制、显示不显示秒、日期三种写法，以及「跟着界面语言走」。
///     <para>
///         以前日期是写死的中文格式，切到英文界面照样是「2026年10月18日」。
///         现在统一走 <see cref="DisplayClockFormatter" />，格式化结果由文化决定，这个类钉住它。
///     </para>
/// </summary>
public class DisplayClockFormatterTests
{
    private static readonly DateTime Evening = new(2026, 10, 26, 21, 42, 7);

    private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Format_TwentyFourHour_KeepsTheLeadingZero()
    {
        var text = DisplayClockFormatter.Format(
            Evening, DisplayClockFormat.TwentyFourHour, showSeconds: true, showDate: false, DisplayDateStyle.Full, Chinese);

        Assert.Equal("21:42", text.Main);
        Assert.Equal("07", text.Seconds);
        Assert.Equal(string.Empty, text.Period);
        Assert.Equal(string.Empty, text.Date);
    }

    [Fact]
    public void Format_TwelveHour_DropsTheLeadingZeroAndAddsThePeriod()
    {
        var text = DisplayClockFormatter.Format(
            Evening, DisplayClockFormat.TwelveHour, showSeconds: true, showDate: false, DisplayDateStyle.Full, English);

        Assert.Equal("9:42", text.Main);
        Assert.Equal("07", text.Seconds);
        Assert.Equal(Evening.ToString("tt", English), text.Period);
    }

    [Fact]
    public void Format_HidesSecondsWhenTurnedOff()
    {
        var text = DisplayClockFormatter.Format(
            Evening, DisplayClockFormat.TwentyFourHour, showSeconds: false, showDate: false, DisplayDateStyle.Full, Chinese);

        Assert.Equal("21:42", text.Main);
        Assert.Equal(string.Empty, text.Seconds);
    }

    /// <summary>
    ///     「大秒」那一档画的是 <see cref="DisplayClockText.Combined" /> 这一整串：
    ///     时分秒之间必须有那个冒号，整串一次性画完（看板模式以前是两个 TextBlock 并排拼的，
    ///     拼出「21:42 07」——没冒号、粗细也不一致，用户截图反馈过）。
    /// </summary>
    [Theory]
    [InlineData(DisplayClockFormat.TwentyFourHour, "21:42:07")]
    [InlineData(DisplayClockFormat.TwelveHour, "9:42:07")]
    public void Combined_JoinsHoursMinutesAndSecondsWithAColon(DisplayClockFormat format, string expected)
    {
        var text = DisplayClockFormatter.Format(
            Evening, format, showSeconds: true, showDate: false, DisplayDateStyle.Full, English);

        Assert.Equal(expected, text.Combined);
    }

    [Fact]
    public void Combined_StopsAtTheMinutesWhenSecondsAreHidden()
    {
        var text = DisplayClockFormatter.Format(
            Evening, DisplayClockFormat.TwentyFourHour, showSeconds: false, showDate: false, DisplayDateStyle.Full, Chinese);

        Assert.Equal("21:42", text.Combined);
    }

    [Fact]
    public void Format_DateFollowsTheChosenStyle()
    {
        var full = DisplayClockFormatter.FormatDate(DateOnly.FromDateTime(Evening), DisplayDateStyle.Full, Chinese);
        var monthDay = DisplayClockFormatter.FormatDate(DateOnly.FromDateTime(Evening), DisplayDateStyle.MonthDay, Chinese);
        var numeric = DisplayClockFormatter.FormatDate(DateOnly.FromDateTime(Evening), DisplayDateStyle.Numeric, Chinese);

        Assert.Contains("2026", full);
        Assert.Contains("10", monthDay);
        Assert.DoesNotContain("2026", monthDay);
        Assert.Contains("2026", numeric);
    }

    [Fact]
    public void Format_DateIsLocalized()
    {
        var date = DateOnly.FromDateTime(Evening);

        Assert.Contains("星期一", DisplayClockFormatter.FormatDate(date, DisplayDateStyle.Full, Chinese));
        Assert.Contains("Monday", DisplayClockFormatter.FormatDate(date, DisplayDateStyle.MonthDay, English));
    }

    [Fact]
    public void Format_ShowDateTogglesTheDateLine()
    {
        var withDate = DisplayClockFormatter.Format(
            Evening, DisplayClockFormat.TwentyFourHour, showSeconds: false, showDate: true, DisplayDateStyle.Full, Chinese);
        var withoutDate = DisplayClockFormatter.Format(
            Evening, DisplayClockFormat.TwentyFourHour, showSeconds: false, showDate: false, DisplayDateStyle.Full, Chinese);

        Assert.NotEqual(string.Empty, withDate.Date);
        Assert.Equal(string.Empty, withoutDate.Date);
    }

    /// <summary>预览浮窗里那句示例：只是把「现在」换成固定时刻，格式仍然跟着配置。</summary>
    [Fact]
    public void Sample_UsesTheSameShapesAsTheRealClock()
    {
        var sample = DisplayClockFormatter.Sample(Evening, showSeconds: true, DisplayClockFormat.TwelveHour);

        Assert.Contains(":", sample.Main);
        Assert.NotEqual(string.Empty, sample.Seconds);

        // 预览与钟面读同一串（「9:42:07」），别在这儿另拼一个带空格的版本。
        Assert.Equal($"{sample.Main}:{sample.Seconds}", sample.Combined);
        Assert.Equal(string.Empty, sample.Date);
    }

    /// <summary>
    ///     「公历后面带农历」：公历那串照旧，后面用「空格 + 间隔号（U+00B7）+ 空格」接农历。
    ///     那个间隔号是硬编码在格式化器里的，别换成别的圆点或连字符。
    /// </summary>
    [Fact]
    public void FormatDate_GregorianAndLunar_JoinsTheTwoWithAMiddleDot()
    {
        if (!LunarDateFormatter.IsSupported)
            return;

        var date = new DateOnly(2024, 2, 10);

        var text = DisplayClockFormatter.FormatDate(date, DisplayDateStyle.Full, Chinese, DisplayDateCalendar.GregorianAndLunar);

        Assert.Contains(" · 农历", text);
        Assert.StartsWith(DisplayClockFormatter.FormatDate(date, DisplayDateStyle.Full, Chinese), text);
        Assert.EndsWith("农历正月初一", text);
    }

    /// <summary>「只农历」：整行就只剩农历，公历那串一个字都不留。</summary>
    [Fact]
    public void FormatDate_Lunar_IsJustTheLunarText()
    {
        if (!LunarDateFormatter.IsSupported)
            return;

        var text = DisplayClockFormatter.FormatDate(
            new DateOnly(2024, 2, 10), DisplayDateStyle.Full, Chinese, DisplayDateCalendar.Lunar);

        Assert.Equal("农历正月初一", text);
        Assert.StartsWith("农历", text);
        Assert.DoesNotContain("2024", text);
    }

    /// <summary>
    ///     农历拿不到时，「只农历」与「公历带农历」<b>都退回公历那串</b>——
    ///     宁可只显示公历，也不能让日期那行空掉。
    ///     <para>
    ///         这里用超出农历支持范围的日期（2101-01-28 之后 <see cref="System.Globalization.ChineseLunisolarCalendar" />
    ///         不再支持）来制造「拿不到」：它在任何机器上都成立。
    ///         另一条路径是整个运行时都没有农历（<c>InvariantGlobalization</c>），见于下一个用例。
    ///     </para>
    /// </summary>
    [Theory]
    [InlineData(DisplayDateCalendar.Lunar)]
    [InlineData(DisplayDateCalendar.GregorianAndLunar)]
    public void FormatDate_FallsBackToGregorianWhenLunarIsUnavailable(DisplayDateCalendar calendar)
    {
        if (!LunarDateFormatter.IsSupported)
            return;

        var date = new DateOnly(2101, 6, 1);
        var gregorian = DisplayClockFormatter.FormatDate(date, DisplayDateStyle.Full, Chinese);

        Assert.Equal(string.Empty, LunarDateFormatter.Format(date, Chinese));
        Assert.Equal(gregorian, DisplayClockFormatter.FormatDate(date, DisplayDateStyle.Full, Chinese, calendar));
    }

    /// <summary>
    ///     运行时整个没有农历（ICU 被裁掉）时同样退回公历。这一档在有 ICU 的机器上测不出来，
    ///     所以短路掉而不是让 CI 挂。
    /// </summary>
    [Theory]
    [InlineData(DisplayDateCalendar.Lunar)]
    [InlineData(DisplayDateCalendar.GregorianAndLunar)]
    public void FormatDate_FallsBackToGregorianWhenLunarIsNotSupportedAtAll(DisplayDateCalendar calendar)
    {
        if (LunarDateFormatter.IsSupported)
            return;

        var date = DateOnly.FromDateTime(Evening);
        var gregorian = DisplayClockFormatter.FormatDate(date, DisplayDateStyle.Full, Chinese);

        Assert.Equal(gregorian, DisplayClockFormatter.FormatDate(date, DisplayDateStyle.Full, Chinese, calendar));
    }

    /// <summary>
    ///     时钟那一整份文字也要跟着历法走：<see cref="DisplayClockFormatter.Format" /> 必须把
    ///     <c>calendar</c> 透传给日期那一段（漏传的话界面上永远只有公历）。
    /// </summary>
    [Fact]
    public void Format_PassesTheCalendarThroughToTheDateLine()
    {
        if (!LunarDateFormatter.IsSupported)
            return;

        var text = DisplayClockFormatter.Format(
            Evening, DisplayClockFormat.TwentyFourHour, showSeconds: true, showDate: true,
            DisplayDateStyle.Full, Chinese, DisplayDateCalendar.GregorianAndLunar);

        Assert.Equal("21:42", text.Main);
        Assert.Equal("07", text.Seconds);
        Assert.Contains(" · 农历", text.Date);
    }

    /// <summary>默认参数是「只公历」——老的调用点（不传 calendar）行为一点都不能变。</summary>
    [Fact]
    public void FormatDate_DefaultsToGregorianOnly()
    {
        var date = DateOnly.FromDateTime(Evening);

        var withoutArgument = DisplayClockFormatter.FormatDate(date, DisplayDateStyle.Full, Chinese);
        var explicitGregorian = DisplayClockFormatter.FormatDate(date, DisplayDateStyle.Full, Chinese, DisplayDateCalendar.Gregorian);

        Assert.Equal(withoutArgument, explicitGregorian);
        Assert.DoesNotContain("农历", explicitGregorian);
    }
}
