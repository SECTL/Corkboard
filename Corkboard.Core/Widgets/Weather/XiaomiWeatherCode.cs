namespace Corkboard.Core.Widgets.Weather;

/// <summary>
///     国标天气现象代码（小米 <c>current.weather</c> / <c>forecastDaily.weather</c> 里那个数字串）
///     到 <see cref="WeatherCondition" /> 的换算。纯函数、有单测。
///     <para>
///         代码是<b>字符串</b>（响应里带引号），所以要先 Trim 再解析：手改过的数据里出现过 <c>" 7"</c>。
///         认不出来的代码（新增的、被截断的、空串）一律给 <see cref="WeatherCondition.Unknown" />，
///         <b>不抛异常</b>——一块看不懂的天气代码不该让整块屏空着。
///     </para>
/// </summary>
public static class XiaomiWeatherCode
{
    /// <summary>把代码换成归并后的天气现象。</summary>
    /// <param name="code">国标天气代码（字符串形式，允许前后空白）。</param>
    /// <returns>对应的天气现象；认不出来时是 <see cref="WeatherCondition.Unknown" />。</returns>
    public static WeatherCondition ToCondition(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return WeatherCondition.Unknown;

        return int.TryParse(code.Trim(), out var value)
            ? ToCondition(value)
            : WeatherCondition.Unknown;
    }

    /// <summary>把代码换成归并后的天气现象（数字形式）。</summary>
    public static WeatherCondition ToCondition(int code) => code switch
    {
        0 => WeatherCondition.Clear,
        1 => WeatherCondition.Cloudy,
        2 => WeatherCondition.Overcast,
        3 => WeatherCondition.Shower,
        4 => WeatherCondition.Thunderstorm,
        5 => WeatherCondition.ThunderHail,
        6 => WeatherCondition.Sleet,
        7 => WeatherCondition.LightRain,
        8 => WeatherCondition.ModerateRain,
        9 => WeatherCondition.HeavyRain,
        10 or 11 or 12 or 38 => WeatherCondition.Rainstorm,
        13 or 14 => WeatherCondition.LightSnow,
        15 => WeatherCondition.ModerateSnow,
        16 => WeatherCondition.HeavySnow,
        17 => WeatherCondition.Blizzard,
        18 or 32 or 33 or 37 => WeatherCondition.Fog,
        19 => WeatherCondition.FreezingRain,
        20 or 31 => WeatherCondition.Sandstorm,
        21 => WeatherCondition.ModerateRain,
        22 => WeatherCondition.HeavyRain,
        23 or 24 or 25 => WeatherCondition.Rainstorm,
        26 => WeatherCondition.ModerateSnow,
        27 => WeatherCondition.HeavySnow,
        28 => WeatherCondition.Blizzard,
        29 or 30 => WeatherCondition.Dust,
        34 or 35 or 36 => WeatherCondition.Haze,
        _ => WeatherCondition.Unknown
    };
}
