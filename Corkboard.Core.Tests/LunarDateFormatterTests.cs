using System.Globalization;
using Corkboard.Core.Display;

namespace Corkboard.Core.Tests;

/// <summary>
///     农历那行字怎么拼：五个锚点日期、英文写法、闰月（闰二月 / 闰六月这一档）、
///     以及「农历在这台机器上用不了」时的兜底。
///     <para>
///         锚点日期全部是查过农历表的事实（2024 / 2025 / 2026 的春节都是正月初一，
///         2023 年闰二月），<b>跑出来对不上要先怀疑自己算错，别改期望值</b>。
///     </para>
///     <para>
///         这里所有用例都显式传文化，不读「当前文化」——CI 跑在 en-US 的 runner 上，
///         依赖机器语言的断言在那边必挂。
///     </para>
/// </summary>
public class LunarDateFormatterTests
{
    private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>中文月名，按「农历」后面那两个字对一遍，顺便确认没把十月 / 冬月 / 腊月写混。</summary>
    private static readonly string[] ExpectedMonths =
    [
        "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月"
    ];

    /// <summary>中文日名：初一…初十、十一…十九、二十、廿一…廿九、三十。</summary>
    private static readonly string[] ExpectedDays =
    [
        "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九",
        "二十", "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九",
        "三十"
    ];

    /// <summary>
    ///     五个锚点：三个春节（正月初一）、一个普通月首、一个闰月月首。
    ///     最后一条是这套实现里最容易写错的地方——2023 年闰二月，
    ///     <c>GetLeapMonth(2023)</c> 返回 3（闰月占第 3 个槽位），所以 rawMonth 等于 3 时
    ///     真实月份是 2、而且要带上「闰」字。
    /// </summary>
    [Theory]
    [InlineData(2024, 2, 10, "农历正月初一")]
    [InlineData(2025, 1, 29, "农历正月初一")]
    [InlineData(2026, 2, 17, "农历正月初一")]
    [InlineData(2023, 2, 20, "农历二月初一")]
    [InlineData(2023, 3, 22, "农历闰二月初一")]
    public void Format_Chinese_UsesTheTraditionalMonthAndDayNames(int year, int month, int day, string expected)
    {
        var actual = LunarDateFormatter.Format(new DateOnly(year, month, day), Chinese);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    ///     英文界面：前缀是 <c>Lunar</c>，闰月写 <c>leap month</c>，而且<b>一个汉字都不许漏出来</b>
    ///     （以前日期那行就是写死的中文格式串，切到英文界面照样是中文）。
    /// </summary>
    [Theory]
    [InlineData(2024, 2, 10, "Lunar month 1, day 1")]
    [InlineData(2025, 1, 29, "Lunar month 1, day 1")]
    [InlineData(2026, 2, 17, "Lunar month 1, day 1")]
    [InlineData(2023, 2, 20, "Lunar month 2, day 1")]
    [InlineData(2023, 3, 22, "Lunar leap month 2, day 1")]
    public void Format_English_StartsWithLunarAndHasNoChineseCharacters(int year, int month, int day, string expected)
    {
        if (!LunarDateFormatter.IsSupported)
            return;

        var actual = LunarDateFormatter.Format(new DateOnly(year, month, day), English);

        Assert.Equal(expected, actual);
        Assert.StartsWith("Lunar", actual);
        Assert.False(actual.Any(IsChineseCharacter), $"英文输出里漏出了汉字：「{actual}」");
    }

    /// <summary>闰月与它前面那个正常月必须分得开：2023 年闰二月前后那几天不能撞成同一串。</summary>
    [Fact]
    public void Format_KeepsTheLeapMonthApartFromTheMonthItRepeats()
    {
        if (!LunarDateFormatter.IsSupported)
            return;

        var lastDayOfFebruary = LunarDateFormatter.Format(new DateOnly(2023, 3, 21), Chinese);
        var firstDayOfLeapFebruary = LunarDateFormatter.Format(new DateOnly(2023, 3, 22), Chinese);
        var firstDayOfMarch = LunarDateFormatter.Format(new DateOnly(2023, 4, 20), Chinese);

        Assert.Equal("农历二月三十", lastDayOfFebruary);
        Assert.Equal("农历闰二月初一", firstDayOfLeapFebruary);
        // 闰月之后那些月份被顶后了一个槽位，减 1 之后才是真实月份。
        Assert.Equal("农历三月初一", firstDayOfMarch);
    }

    /// <summary>
    ///     农历不可用时（ICU 被裁掉 / <c>InvariantGlobalization</c>）返回空串而不是抛异常——
    ///     看板那行字靠调用方退回公历，绝不能让时钟崩。
    /// </summary>
    [Fact]
    public void Format_ReturnsEmptyWhenLunarIsNotSupported()
    {
        if (LunarDateFormatter.IsSupported)
            return; // 有 ICU 的机器上这一档测不出来（本机与 CI 都是这种）。

        Assert.Equal(string.Empty, LunarDateFormatter.Format(new DateOnly(2024, 2, 10), Chinese));
        Assert.Equal(string.Empty, LunarDateFormatter.Format(new DateOnly(2024, 2, 10), English));
    }

    /// <summary>
    ///     超出农历支持范围（1901-02-19 之前、2101-01-28 之后）同样是「拿不到」，
    ///     这条在<b>任何</b>机器上都能跑，正好把兜底分支钉住。
    /// </summary>
    [Theory]
    [InlineData(1901, 1, 1)]
    [InlineData(2101, 6, 1)]
    public void Format_ReturnsEmptyOutsideTheSupportedRange(int year, int month, int day)
    {
        if (!LunarDateFormatter.IsSupported)
            return;

        Assert.Equal(string.Empty, LunarDateFormatter.Format(new DateOnly(year, month, day), Chinese));
    }

    /// <summary>
    ///     跨整整一年逐日扫一遍：不抛异常、结果非空、以「农历」开头，而且月名日名都落在
    ///     那两张表里（顺带覆盖「一年 13 个槽位」在年末几个月的错位）。
    ///     <para>
    ///         2024 全年不该出现「闰」字（2024 农历年不闰；公历 1 月那几天还属于农历 2023 年，
    ///         但 2023 年的闰二月在当年 3 月，早过去了）。
    ///     </para>
    /// </summary>
    [Fact]
    public void Format_CoversAWholeYearWithoutThrowingOrFallingBack()
    {
        if (!LunarDateFormatter.IsSupported)
            return;

        var start = new DateOnly(2024, 1, 1);
        var seen = 0;

        for (var offset = 0; offset < 366; offset++)
        {
            var date = start.AddDays(offset);
            if (date.Year != 2024)
                break;

            var text = LunarDateFormatter.Format(date, Chinese);

            Assert.False(string.IsNullOrEmpty(text), $"{date:yyyy-MM-dd} 的农历串是空的");
            Assert.StartsWith("农历", text);
            Assert.DoesNotContain("闰", text);
            AssertEndsWithAnyDayName(text);

            var month = text["农历".Length..];
            Assert.True(
                ExpectedMonths.Any(m => month.StartsWith(m, StringComparison.Ordinal)),
                $"「{text}」的月名不在预期表里");

            seen++;
        }

        Assert.Equal(366, seen);
    }

    /// <summary>文化传 <c>null</c> 时读当前文化——这里临时换成中文，确认走的是中文那条分支。</summary>
    [Fact]
    public void Format_FallsBackToTheCurrentCulture()
    {
        if (!LunarDateFormatter.IsSupported)
            return;

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = Chinese;
            Assert.StartsWith("农历", LunarDateFormatter.Format(new DateOnly(2024, 2, 10)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>汉字判定：真正的断言是「英文输出里一个汉字都没有」，所以按码位查，不靠正则。</summary>
    private static bool IsChineseCharacter(char c) => c is >= '\u4E00' and <= '\u9FFF';

    /// <summary>把断言消息写清楚一点：失败时要看得出拼出来的到底长什么样。</summary>
    private static void AssertEndsWithAnyDayName(string text) =>
        Assert.True(
            ExpectedDays.Any(d => text.EndsWith(d, StringComparison.Ordinal)),
            $"「{text}」的日名不在预期表里");
}
