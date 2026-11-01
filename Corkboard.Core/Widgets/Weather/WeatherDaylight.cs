namespace Corkboard.Core.Widgets.Weather;

/// <summary>
///     「现在算不算白天」的唯一判据：6 点到 18 点之间。
///     <para>
///         它同时决定两件事，所以只写一处：一是向服务端要天气时带的 <c>isDaylight</c>（影响
///         返回的图标与「今天」的划分），二是我们自己选白天版还是夜间版图标。
///         粗糙一点没关系——真按日出日落算要城市经纬度+天文算法，对一个角落里的小图标不值当。
///     </para>
/// </summary>
public static class WeatherDaylight
{
    /// <summary>白天从这里开始（含）。</summary>
    public const int DayStartHour = 6;

    /// <summary>白天到这里结束（不含）。</summary>
    public const int DayEndHour = 18;

    /// <summary>这一刻是不是白天。</summary>
    /// <param name="localTime">本机本地时间（调用方一律给 <c>NtpTimeService.NowLocal</c>）。</param>
    public static bool IsDay(DateTimeOffset localTime) => localTime.Hour is >= DayStartHour and < DayEndHour;
}
