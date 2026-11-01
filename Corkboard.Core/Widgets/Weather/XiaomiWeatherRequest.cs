using System.Globalization;

namespace Corkboard.Core.Widgets.Weather;

/// <summary>
///     小米天气（MIUI wtr-v3）的请求串拼装。纯函数、不碰网络，有单测。
///     <para>
///         ⚠️ <b>这个接口没有官方文档</b>，是 MIUI 的非公开契约：<c>/wtr-v3/weather/all</c> 与
///         <c>/wtr-v3/location/city/search</c> 现在能用，而 <c>/weather/condition</c>、<c>/weather/now</c>、
///         <c>/location/city/all</c> 都已经 404。哪天它整体变脸也不奇怪 —— 所以上层把「取天气」
///         做成可替换的一步，别把 UI 直接焊在这个类上。
///     </para>
///     <para>
///         <see cref="AppKey" /> 与 <see cref="Sign" /> 是社区沿用的固定值：实测服务端<b>只校验非空、
///         不校验签名值</b>（随便填也回 200）。所以这里不要试图实现签名算法，也别假设它长期有效——
///         真失效了，表现是 400，上层照旧静默退回上一次的数据。
///     </para>
/// </summary>
public static class XiaomiWeatherRequest
{
    /// <summary>接口根地址。</summary>
    public const string ApiRoot = "https://weatherapi.market.xiaomi.com";

    /// <summary>接口路径：全量天气（当前 + 逐日 + 逐时 + 指数 + 空气 + 预警）。</summary>
    public const string WeatherPath = "/wtr-v3/weather/all";

    /// <summary>接口路径：城市搜索。</summary>
    public const string CitySearchPath = "/wtr-v3/location/city/search";

    /// <summary>社区沿用的 appKey。</summary>
    public const string AppKey = "weather20151024";

    /// <summary>社区沿用的 sign（服务端只校验非空）。</summary>
    public const string Sign = "zUFJoAR2ZVrDy1vF3D07";

    /// <summary>语言区域。</summary>
    public const string Locale = "zh_cn";

    /// <summary>
    ///     拼「全量天气」的地址。
    ///     <para>
    ///         参数顺序照服务端的校验顺序（latitude → isGlobal → appKey → sign）：
    ///         缺哪个就在报错里点名哪个，顺序一致时用户看报错能直接对上自己少了什么。
    ///     </para>
    ///     <para>
    ///         <b>只有经纬度是有效定位方式</b>：单独传 <c>cityId</c> / <c>locationKey</c> 会被服务端
    ///         要求补 latitude。所以城市搜索拿到的是经纬度，存的也是经纬度。
    ///     </para>
    /// </summary>
    /// <param name="latitude">纬度（-90–90）。</param>
    /// <param name="longitude">经度（-180–180）。</param>
    /// <param name="isDaylight">是不是白天（影响图标与「今天」的定义）；不影响主体数据。</param>
    /// <exception cref="ArgumentOutOfRangeException">经纬度超出合法范围或不是有限数。</exception>
    public static Uri BuildWeatherUri(double latitude, double longitude, bool isDaylight = false)
    {
        ThrowIfInvalid(latitude, -90, 90, nameof(latitude));
        ThrowIfInvalid(longitude, -180, 180, nameof(longitude));

        var query = string.Join('&',
            $"latitude={FormatCoordinate(latitude)}",
            $"longitude={FormatCoordinate(longitude)}",
            $"locale={Locale}",
            "isLocationEncoded=0",
            "isGlobal=false",
            $"isDaylight={(isDaylight ? 1 : 0)}",
            $"appKey={AppKey}",
            $"sign={Sign}");

        return new Uri($"{ApiRoot}{WeatherPath}?{query}");
    }

    /// <summary>
    ///     拼「城市搜索」的地址。
    ///     <para>
    ///         返回的是<b>数组</b>，一串同名 / 近名的城市（东城区、东营……），由用户自己选一个，
    ///         选中那一行里带着经纬度，喂给 <see cref="BuildWeatherUri" />。
    ///     </para>
    /// </summary>
    /// <param name="cityName">城市名（中文或拼音都行，服务端自己模糊匹配）。</param>
    /// <exception cref="ArgumentException">城市名为空或全空白。</exception>
    public static Uri BuildCitySearchUri(string cityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cityName);

        var query = string.Join('&',
            $"name={Uri.EscapeDataString(cityName.Trim())}",
            $"locale={Locale}",
            "isGlobal=false",
            $"appKey={AppKey}",
            $"sign={Sign}");

        return new Uri($"{ApiRoot}{CitySearchPath}?{query}");
    }

    /// <summary>
    ///     坐标的字符串形式：最多六位小数、按不变文化格式化。
    ///     <para>
    ///         不能直接用 <c>double.ToString()</c>：某些区域设置会写出 <c>39,9042</c> 这种逗号小数点，
    ///         塞进查询串会被服务端当成两个参数。
    ///     </para>
    /// </summary>
    public static string FormatCoordinate(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static void ThrowIfInvalid(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentOutOfRangeException(name, value, $"坐标必须在 {min} 到 {max} 之间");
    }
}
