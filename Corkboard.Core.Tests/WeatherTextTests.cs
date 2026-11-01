using System.Globalization;
using Corkboard.Core.Widgets.Weather;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Tests;

/// <summary>
///     天气那块里每一小格的字怎么写：温度、今天高低、湿度、体感、空气、明天、现象名。
///     <para>
///         两条规矩钉在这里：一是<b>缺数据一律空串</b>（界面靠空串把那一格藏起来，写 0 或「--」
///         会让「0 度的阴天」与「没有数据」混成一个样子）；二是文案全部走资源，不在这里写死中文。
///     </para>
/// </summary>
public class WeatherTextTests
{
    [Fact]
    public void Temperature_AppendsTheDegreeSign()
    {
        Assert.Equal("9°", WeatherText.Temperature(9));
        Assert.Equal("-3°", WeatherText.Temperature(-3));
    }

    [Fact]
    public void Temperature_WithoutValue_IsEmpty()
    {
        Assert.Equal(string.Empty, WeatherText.Temperature(null));
    }

    [Fact]
    public void Range_WritesLowBeforeHigh()
    {
        var expected = string.Format(CultureInfo.CurrentCulture, CR.Widgets_WeatherRange, "3", "17");

        Assert.Equal(expected, WeatherText.Range(17, 3));
    }

    [Fact]
    public void Range_WithOnlyOneSide_IsEmpty()
    {
        Assert.Equal(string.Empty, WeatherText.Range(null, 3));
        Assert.Equal(string.Empty, WeatherText.Range(17, null));
    }

    [Fact]
    public void Humidity_And_FeelsLike_UseTheirOwnKeys()
    {
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Widgets_WeatherHumidity, "81"),
            WeatherText.Humidity(81));
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Widgets_WeatherFeelsLike, "7"),
            WeatherText.FeelsLike(7));
        Assert.Equal(string.Empty, WeatherText.Humidity(null));
        Assert.Equal(string.Empty, WeatherText.FeelsLike(null));
    }

    [Fact]
    public void AirQuality_FollowsTheSixLevelScale()
    {
        AssertLevel(45, CR.Weather_Aqi_Excellent);
        AssertLevel(50, CR.Weather_Aqi_Excellent);
        AssertLevel(51, CR.Weather_Aqi_Good);
        AssertLevel(100, CR.Weather_Aqi_Good);
        AssertLevel(101, CR.Weather_Aqi_Light);
        AssertLevel(150, CR.Weather_Aqi_Light);
        AssertLevel(151, CR.Weather_Aqi_Moderate);
        AssertLevel(200, CR.Weather_Aqi_Moderate);
        AssertLevel(201, CR.Weather_Aqi_Heavy);
        AssertLevel(300, CR.Weather_Aqi_Heavy);
        AssertLevel(301, CR.Weather_Aqi_Severe);
    }

    private static void AssertLevel(int aqi, string level) =>
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Widgets_WeatherAqi, level),
            WeatherText.AirQuality(aqi));

    [Fact]
    public void AirQuality_Negative_CountsAsExcellent()
    {
        Assert.Equal(CR.Weather_Aqi_Excellent, WeatherText.DescribeAirQuality(-5));
    }

    [Fact]
    public void AirQuality_WithoutValue_IsEmpty()
    {
        Assert.Equal(string.Empty, WeatherText.AirQuality(null));
    }

    [Fact]
    public void AirQuality_NeverShowsTheRawNumber()
    {
        var text = WeatherText.AirQuality(600);

        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, CR.Widgets_WeatherAqi, CR.Weather_Aqi_Severe),
            text);
        Assert.DoesNotContain("600", text);
    }

    [Fact]
    public void Tomorrow_WritesConditionAndRange()
    {
        var expected = string.Format(
            CultureInfo.CurrentCulture, CR.Widgets_WeatherTomorrow, CR.Weather_Condition_LightRain, "4", "12");

        Assert.Equal(expected, WeatherText.Tomorrow(WeatherCondition.LightRain, 12, 4));
    }

    [Fact]
    public void Tomorrow_WithOnlyCondition_DropsTheTemperaturePart()
    {
        var expected = string.Format(
            CultureInfo.CurrentCulture, CR.Widgets_WeatherTomorrowNoTemperature, CR.Weather_Condition_ModerateSnow);

        Assert.Equal(expected, WeatherText.Tomorrow(WeatherCondition.ModerateSnow, null, null));
    }

    [Fact]
    public void Tomorrow_WithOnlyTemperature_FillsInTheUnknownWord()
    {
        var text = WeatherText.Tomorrow(WeatherCondition.Unknown, 12, 4);

        Assert.Contains(CR.Weather_Condition_Unknown, text);
        Assert.DoesNotContain("  ", text);
    }

    [Fact]
    public void Tomorrow_WithoutAnything_IsEmpty()
    {
        Assert.Equal(string.Empty, WeatherText.Tomorrow(WeatherCondition.Unknown, null, 4));
        Assert.Equal(string.Empty, WeatherText.Tomorrow(WeatherCondition.Unknown, null, null));
    }

    [Fact]
    public void Describe_CoversEveryKnownCondition()
    {
        foreach (var condition in Enum.GetValues<WeatherCondition>())
        {
            var text = WeatherText.Describe(condition);

            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }
}
