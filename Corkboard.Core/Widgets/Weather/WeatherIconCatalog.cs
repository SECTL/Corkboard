namespace Corkboard.Core.Widgets.Weather;

/// <summary>
///     「天气现象」到图标文件名的映射（图标是随包分发的 PNG，在
///     <c>Corkboard/Assets/Weather/</c>，见 <c>THIRD-PARTY-NOTICES.md</c>）。
///     <para>
///         映射放 Core 里是有意的：它是一张纯表，能单测、能让「界面上画的是哪个图标」
///         与解析层分开改。文件名的实现在 App 层拼（<c>avares://</c> 在这里不出现）。
///     </para>
///     <para>
///         昼夜只影响少数几个现象：晴、晴间多云、阵雨、雷阵雨、小雪、雾、霾、浮尘。
///         其余的（阴天、雨、雪的大多数）本来就没有月亮或太阳那一角，白天黑夜长一个样。
///     </para>
/// </summary>
public static class WeatherIconCatalog
{
    /// <summary>认不出来时用的兜底图标（一朵云），保证任何情况下都有东西可画。</summary>
    public const string Fallback = "cloudy";

    /// <summary>
    ///     取这个现象对应的图标文件名（不带扩展名）。
    /// </summary>
    /// <param name="condition">天气现象。</param>
    /// <param name="isDaytime">此刻是不是白天（决定用日间版还是夜间版）。</param>
    public static string FileName(WeatherCondition condition, bool isDaytime) => condition switch
    {
        WeatherCondition.Clear => isDaytime ? "clear-day" : "clear-night",
        WeatherCondition.PartlyCloudy => isDaytime ? "partly-cloudy-day" : "partly-cloudy-night",
        WeatherCondition.Cloudy => "cloudy",
        WeatherCondition.Overcast => "overcast",
        WeatherCondition.Shower => isDaytime ? "partly-cloudy-day-rain" : "partly-cloudy-night-rain",
        WeatherCondition.Thunderstorm => isDaytime ? "thunderstorms-day" : "thunderstorms-night",
        WeatherCondition.ThunderHail => "hail",
        WeatherCondition.Sleet => "sleet",
        WeatherCondition.LightRain => "drizzle",
        WeatherCondition.ModerateRain => "rain",
        WeatherCondition.HeavyRain => "rain",
        WeatherCondition.Rainstorm => "extreme-rain",
        WeatherCondition.LightSnow => isDaytime ? "partly-cloudy-day-snow" : "partly-cloudy-night-snow",
        WeatherCondition.ModerateSnow => "overcast-day-snow",
        WeatherCondition.HeavySnow => "extreme-day-snow",
        WeatherCondition.Blizzard => "extreme-snow",
        WeatherCondition.Fog => isDaytime ? "fog-day" : "fog-night",
        WeatherCondition.Haze => isDaytime ? "haze" : "haze-night",
        WeatherCondition.FreezingRain => "sleet",
        WeatherCondition.Dust => isDaytime ? "dust-day" : "dust-night",
        WeatherCondition.Sandstorm => "dust-day",
        _ => Fallback
    };
}
