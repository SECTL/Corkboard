using System.Globalization;
using Corkboard.Core.Langs.Common;

namespace Corkboard.Core.Widgets.Weather;

/// <summary>
///     把 <see cref="WeatherSnapshot" /> 拆成屏幕上那几块各自的文字（本地化文案也在这里）。
///     <para>
///         <b>不再拼成一整行</b>：早先那版把所有字段用「·」串起来（城市 · 多云 9° · 湿度 81%），
///         用户看着嫌碎，改成了「图标 + 大温度 + 几块小标签」的分块排版。所以这里只负责
///         「每一块怎么写」，谁跟谁挨着由 XAML 决定。
///     </para>
///     <para>
///         缺失的字段一律返回空串（界面据此把那一块藏起来），<b>不要</b>写成 0 或「--」：
///         0 度的阴天和「没有数据」在屏幕上必须能区分开。
///     </para>
/// </summary>
public static class WeatherText
{
    /// <summary>天气现象的中文说法。</summary>
    public static string Describe(WeatherCondition condition) => condition switch
    {
        WeatherCondition.Clear => Resources.Weather_Condition_Clear,
        WeatherCondition.PartlyCloudy => Resources.Weather_Condition_PartlyCloudy,
        WeatherCondition.Cloudy => Resources.Weather_Condition_Cloudy,
        WeatherCondition.Overcast => Resources.Weather_Condition_Overcast,
        WeatherCondition.Shower => Resources.Weather_Condition_Shower,
        WeatherCondition.Thunderstorm => Resources.Weather_Condition_Thunderstorm,
        WeatherCondition.ThunderHail => Resources.Weather_Condition_ThunderHail,
        WeatherCondition.Sleet => Resources.Weather_Condition_Sleet,
        WeatherCondition.LightRain => Resources.Weather_Condition_LightRain,
        WeatherCondition.ModerateRain => Resources.Weather_Condition_ModerateRain,
        WeatherCondition.HeavyRain => Resources.Weather_Condition_HeavyRain,
        WeatherCondition.Rainstorm => Resources.Weather_Condition_Rainstorm,
        WeatherCondition.LightSnow => Resources.Weather_Condition_LightSnow,
        WeatherCondition.ModerateSnow => Resources.Weather_Condition_ModerateSnow,
        WeatherCondition.HeavySnow => Resources.Weather_Condition_HeavySnow,
        WeatherCondition.Blizzard => Resources.Weather_Condition_Blizzard,
        WeatherCondition.Fog => Resources.Weather_Condition_Fog,
        WeatherCondition.Haze => Resources.Weather_Condition_Haze,
        WeatherCondition.FreezingRain => Resources.Weather_Condition_FreezingRain,
        WeatherCondition.Dust => Resources.Weather_Condition_Dust,
        WeatherCondition.Sandstorm => Resources.Weather_Condition_Sandstorm,
        _ => Resources.Weather_Condition_Unknown
    };

    /// <summary>当前温度那一个大字，例如 <c>9°</c>；没有数据时是空串。</summary>
    public static string Temperature(int? value) => value is { } temperature ? $"{Format(temperature)}°" : string.Empty;

    /// <summary>今天最高 / 最低，例如 <c>今天 3–17°</c>。</summary>
    public static string Range(int? high, int? low) =>
        high is { } highValue && low is { } lowValue
            ? string.Format(CultureInfo.CurrentCulture, Resources.Widgets_WeatherRange,
                Format(lowValue), Format(highValue))
            : string.Empty;

    /// <summary>湿度，例如 <c>湿度 81%</c>。</summary>
    public static string Humidity(int? humidity) =>
        humidity is { } value
            ? string.Format(CultureInfo.CurrentCulture, Resources.Widgets_WeatherHumidity, Format(value))
            : string.Empty;

    /// <summary>体感温度，例如 <c>体感 7°</c>。</summary>
    public static string FeelsLike(int? feelsLike) =>
        feelsLike is { } value
            ? string.Format(CultureInfo.CurrentCulture, Resources.Widgets_WeatherFeelsLike, Format(value))
            : string.Empty;

    /// <summary>
    ///     空气质量，例如 <c>空气 优</c>。<b>只写等级不写数字</b>：屏幕在远处看，
    ///     「良 / 轻度污染」一眼就懂，「45」还得让人回想一遍标准。
    /// </summary>
    public static string AirQuality(int? aqi) =>
        aqi is { } value
            ? string.Format(CultureInfo.CurrentCulture, Resources.Widgets_WeatherAqi, DescribeAirQuality(value))
            : string.Empty;

    /// <summary>明天那一块，例如 <c>明天 小雨 4–12°</c>；现象与温度都没有时是空串。</summary>
    public static string Tomorrow(WeatherCondition condition, int? high, int? low)
    {
        var hasTemperature = high is not null && low is not null;
        if (condition == WeatherCondition.Unknown && !hasTemperature)
            return string.Empty;

        // 现象缺了就写「未知」，不要留个空位拼出「明天  4–12°」（两个空格比缺字更难看）。
        var name = Describe(condition);
        if (!hasTemperature)
            return string.Format(CultureInfo.CurrentCulture, Resources.Widgets_WeatherTomorrowNoTemperature, name);

        return string.Format(CultureInfo.CurrentCulture, Resources.Widgets_WeatherTomorrow,
            name, Format(low!.Value), Format(high!.Value));
    }

    /// <summary>
    ///     空气质量等级（中国 AQI 分级）：<c>0–50 优 / 51–100 良 / 101–150 轻度污染 /
    ///     151–200 中度污染 / 201–300 重度污染 / 300 以上严重污染</c>。负数按「优」算。
    /// </summary>
    public static string DescribeAirQuality(int aqi) => aqi switch
    {
        <= 50 => Resources.Weather_Aqi_Excellent,
        <= 100 => Resources.Weather_Aqi_Good,
        <= 150 => Resources.Weather_Aqi_Light,
        <= 200 => Resources.Weather_Aqi_Moderate,
        <= 300 => Resources.Weather_Aqi_Heavy,
        _ => Resources.Weather_Aqi_Severe
    };

    private static string Format(int value) => value.ToString(CultureInfo.CurrentCulture);
}
