namespace Corkboard.Core.Widgets.Weather;

/// <summary>
///     某一刻的天气「一句话」：屏幕上那行字要用的全部字段，别的一概不要。
///     <para>
///         每个字段都可能为空——小米那个接口里 <c>aqi</c> / <c>alerts</c> / <c>forecastDaily</c>
///         都可能整块不给（新城市、海外坐标尤其明显）。界面按「有就显示、没有就省掉」处理，
///         <b>不要</b>把它们显示成 0 或「--」：0 度的阴天和「没有数据」看起来一样，会误导人。
///     </para>
///     <para>
///         温度一律 <see langword="int" />：响应里是「9」「17」这种整数字符串，小数位没有意义。
///     </para>
/// </summary>
/// <param name="CityName">城市名（由调用方填：解析结果里本来不带）。</param>
/// <param name="Temperature">当前温度（摄氏度）。</param>
/// <param name="FeelsLike">体感温度。</param>
/// <param name="Humidity">相对湿度（百分比整数）。</param>
/// <param name="Aqi">空气质量指数。</param>
/// <param name="Condition">当前天气现象。</param>
/// <param name="HighTemperature">今天最高温度。</param>
/// <param name="LowTemperature">今天最低温度。</param>
/// <param name="TomorrowCondition">明天的天气现象。</param>
/// <param name="TomorrowHigh">明天最高温度。</param>
/// <param name="TomorrowLow">明天最低温度。</param>
/// <param name="PrimaryAlert">第一条预警的标题（没有预警时为空）。</param>
/// <param name="UpdatedAt">数据更新的时刻（服务端给的是毫秒时间戳或 ISO 串）。</param>
public sealed record WeatherSnapshot(
    string? CityName = null,
    int? Temperature = null,
    int? FeelsLike = null,
    int? Humidity = null,
    int? Aqi = null,
    WeatherCondition Condition = WeatherCondition.Unknown,
    int? HighTemperature = null,
    int? LowTemperature = null,
    WeatherCondition TomorrowCondition = WeatherCondition.Unknown,
    int? TomorrowHigh = null,
    int? TomorrowLow = null,
    string? PrimaryAlert = null,
    DateTimeOffset? UpdatedAt = null)
{
    /// <summary>一个字段都没有——「还没拿到」。界面据此整行隐藏。</summary>
    public static WeatherSnapshot Empty { get; } = new();

    /// <summary>这条数据里有没有能显示的内容（温度和天气现象至少有一个）。</summary>
    public bool HasContent => Temperature is not null || Condition != WeatherCondition.Unknown;
}
