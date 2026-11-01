namespace Corkboard.Core.Widgets.Weather;

/// <summary>
///     归并之后的天气现象。小米那边给的是国标天气代码（0 晴、1 多云、2 阴……），
///     这里把它们归一成一组与数据源无关的说法，界面只认这一套。
///     <para>
///         数值冻结：配置与界面都可能引用，<b>只在末尾追加</b>。
///     </para>
///     <para>
///         为什么不留「小雨 / 中雨 / 大雨」这种粒度：一行天气里放不下「中到大雨转小雨」，
///         而决定穿不穿外套的信息就那么几条——晴、阴、下雨、下雪、雾霾、沙尘。
///         真正的细节留着以后做详情页再说。
///     </para>
/// </summary>
public enum WeatherCondition
{
    /// <summary>不知道（代码缺失、认不出来）。</summary>
    Unknown = 0,

    /// <summary>晴。</summary>
    Clear = 1,

    /// <summary>多云（局部有云）。</summary>
    PartlyCloudy = 2,

    /// <summary>多云（云量偏多，国标里「多云」）。</summary>
    Cloudy = 3,

    /// <summary>阴。</summary>
    Overcast = 4,

    /// <summary>阵雨。</summary>
    Shower = 5,

    /// <summary>雷阵雨。</summary>
    Thunderstorm = 6,

    /// <summary>雷阵雨伴有冰雹。</summary>
    ThunderHail = 7,

    /// <summary>雨夹雪 / 冻雨以外的混合降水。</summary>
    Sleet = 8,

    /// <summary>小雨（含小到中雨）。</summary>
    LightRain = 9,

    /// <summary>中雨。</summary>
    ModerateRain = 10,

    /// <summary>大雨。</summary>
    HeavyRain = 11,

    /// <summary>暴雨及以上。</summary>
    Rainstorm = 12,

    /// <summary>小雪（含阵雪）。</summary>
    LightSnow = 13,

    /// <summary>中雪。</summary>
    ModerateSnow = 14,

    /// <summary>大雪。</summary>
    HeavySnow = 15,

    /// <summary>暴雪。</summary>
    Blizzard = 16,

    /// <summary>雾 / 浓雾 / 大雾。</summary>
    Fog = 17,

    /// <summary>霾（中度 / 重度 / 严重）。</summary>
    Haze = 18,

    /// <summary>冻雨。</summary>
    FreezingRain = 19,

    /// <summary>浮尘 / 扬沙。</summary>
    Dust = 20,

    /// <summary>沙尘暴（含强沙尘暴）。</summary>
    Sandstorm = 21
}
