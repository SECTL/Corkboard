using Corkboard.Core.Display;

namespace Corkboard.Core.Tests;

/// <summary>
///     看板模式里每一处文字的字号与它怎么跟着主时钟字号缩放。
///     <para>
///         这一套数字是全屏窗口与设置页滑块共用的，改错了不会编译不过，只会「看上去不对」：
///         秒数又缩回小字、天气那块长成一整块海报、图标比温度还小。
///         所以比例、上下限、夹取各钉一条。
///     </para>
/// </summary>
public class DisplayTextStyleTests
{
    [Fact]
    public void ClockSeconds_SameSize_MatchesTheClockExactly()
    {
        var seconds = DisplayTextStyle.ClockSecondsFontSize(96, sameSize: true);

        Assert.Equal(96, seconds);
        Assert.Equal(0, DisplayTextStyle.ClockSecondsBaselineOffset(96, sameSize: true));
    }

    [Fact]
    public void ClockSeconds_SmallSize_ShrinksAndDropsTheBaseline()
    {
        var seconds = DisplayTextStyle.ClockSecondsFontSize(100, sameSize: false);

        Assert.Equal(42, seconds, 3);
        Assert.Equal(18, DisplayTextStyle.ClockSecondsBaselineOffset(100, sameSize: false), 3);
    }

    [Fact]
    public void ClockSeconds_NoArgument_DefaultsToSameSize()
    {
        Assert.Equal(
            DisplayTextStyle.ClockSecondsFontSize(120, sameSize: true),
            DisplayTextStyle.ClockSecondsFontSize(120));
    }

    [Fact]
    public void ClockSeconds_BrokenClockSize_FallsBackToTheDefault()
    {
        Assert.Equal(
            DisplayTextStyle.DefaultClockFontSize,
            DisplayTextStyle.ClockSecondsFontSize(double.NaN, sameSize: true));
        Assert.Equal(
            DisplayTextStyle.ClockSecondsFontSize(DisplayTextStyle.DefaultClockFontSize, sameSize: false),
            DisplayTextStyle.ClockSecondsFontSize(-12, sameSize: false));
    }

    /// <summary>
    ///     天气字号与一言字号各有自己那一档区间：合法值原样返回、越界夹住，
    ///     坏值（NaN / 无穷）退回默认值——它们是用户自己拖出来的数，不该被夹成边上。
    /// </summary>
    [Fact]
    public void CaptionFontSizes_ClampAndFallBackToTheDefaults()
    {
        Assert.Equal(40, DisplayTextStyle.WeatherFontSize(40));
        Assert.Equal(DisplayTextStyle.MaxWeatherFontSize, DisplayTextStyle.WeatherFontSize(480));
        Assert.Equal(DisplayTextStyle.MinWeatherFontSize, DisplayTextStyle.WeatherFontSize(2));
        Assert.Equal(DisplayTextStyle.DefaultWeatherFontSize, DisplayTextStyle.WeatherFontSize(double.NaN));

        Assert.Equal(30, DisplayTextStyle.QuoteFontSize(30));
        Assert.Equal(DisplayTextStyle.MaxQuoteFontSize, DisplayTextStyle.QuoteFontSize(480));
        Assert.Equal(DisplayTextStyle.MinQuoteFontSize, DisplayTextStyle.QuoteFontSize(2));
        Assert.Equal(DisplayTextStyle.DefaultQuoteFontSize, DisplayTextStyle.QuoteFontSize(double.PositiveInfinity));
    }

    [Fact]
    public void WeatherParts_ScaleTogetherAndStayOrdered()
    {
        // 三档必须「图标 > 温度 > 现象 > 小字」，且从最小号到最大号一路成立：
        // 早先三档共用一个下限，小字号下现象与小字缩到同一档、看着像一行。
        foreach (var configured in new double[] { 18, 22, 40, 72, 96 })
        {
            var icon = DisplayTextStyle.WeatherIconSize(configured);
            var temperature = DisplayTextStyle.WeatherFontSize(configured);
            var condition = DisplayTextStyle.WeatherConditionFontSize(configured);
            var fact = DisplayTextStyle.WeatherFactFontSize(configured);

            Assert.True(icon > temperature, $"{configured}：图标 {icon} 应大于温度 {temperature}");
            Assert.True(temperature > condition, $"{configured}：温度 {temperature} 应大于现象 {condition}");
            Assert.True(condition > fact, $"{configured}：现象 {condition} 应大于小字 {fact}");
            Assert.True(fact >= DisplayTextStyle.MinWeatherFactFontSize);
        }
    }

    [Fact]
    public void WeatherIcon_GrowsWithTheTemperatureAndStopsAtTheTop()
    {
        // 图标跟温度同比例长，但顶到上限就不该再涨（否则天气块会变成半屏海报）。
        Assert.Equal(
            DisplayTextStyle.WeatherFontSize(40) * DisplayTextStyle.WeatherIconToTemperatureRatio,
            DisplayTextStyle.WeatherIconSize(40),
            3);
        Assert.Equal(
            DisplayTextStyle.MaxWeatherIconSize,
            DisplayTextStyle.WeatherIconSize(DisplayTextStyle.MaxWeatherFontSize));
        Assert.Equal(
            DisplayTextStyle.WeatherIconSize(DisplayTextStyle.DefaultWeatherFontSize),
            DisplayTextStyle.WeatherIconSize(double.NaN),
            3);
    }
}
