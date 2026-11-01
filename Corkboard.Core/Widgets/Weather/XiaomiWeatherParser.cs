using System.Globalization;
using System.Text.Json;

namespace Corkboard.Core.Widgets.Weather;

/// <summary>
///     把小米全量天气的 JSON 读成 <see cref="WeatherSnapshot" />。纯函数、不碰网络，有单测。
///     <para>
///         ⚠️ <b>极其宽容</b>：这个接口没有文档、也没承诺过字段稳定，所以每个字段都当「可能没有、
///         可能是意外的类型」处理 —— 数值全是字符串（<c>"9"</c>）、<c>forecastDaily</c> 的每个数组
///         是 15 个元素、<c>alerts</c> 常常是空数组。任何一处读不出来都只让那一个字段留空，
///         <b>绝不抛异常</b>。
///     </para>
///     <para>
///         怎么判断「整份响应没用」：顶层不是对象、或者 <c>current</c> 与 <c>forecastDaily</c> 都不在。
///         只看 <c>current</c> 太严（实测有只回逐日预报的变体），只看某个字段又太松。
///     </para>
/// </summary>
public static class XiaomiWeatherParser
{
    /// <summary>
    ///     试把一份响应读成 <see cref="WeatherSnapshot" />。
    /// </summary>
    /// <param name="json">响应正文；可以是 <see langword="null" /> 或任意脏串。</param>
    /// <param name="snapshot">读出来的天气；失败时为 <see langword="null" />。</param>
    /// <returns>是不是读到了可用的天气。</returns>
    public static bool TryParse(string? json, out WeatherSnapshot? snapshot)
    {
        snapshot = null;

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var current = GetObject(root, "current");
            var daily = GetObject(root, "forecastDaily");
            if (current is null && daily is null)
                return false;

            var temperature = ReadInt(GetPath(current, "temperature", "value"));
            var condition = ReadCondition(GetPath(current, "weather"));
            var todayWeather = ReadDayCondition(daily, 0);
            var tomorrowWeather = ReadDayCondition(daily, 1);

            snapshot = new WeatherSnapshot(
                CityName: null,
                Temperature: temperature,
                FeelsLike: ReadInt(GetPath(current, "feelsLike", "value")),
                Humidity: ReadInt(GetPath(current, "humidity", "value")),
                Aqi: ReadInt(GetPath(GetObject(root, "aqi"), "aqi")),
                // 当前天气读不出来就退回今天的预报现象：总比「未知」强，屏幕上不至于少一个字。
                Condition: condition != WeatherCondition.Unknown ? condition : todayWeather,
                HighTemperature: ReadDayTemperature(daily, 0, "from"),
                LowTemperature: ReadDayTemperature(daily, 0, "to"),
                TomorrowCondition: tomorrowWeather,
                TomorrowHigh: ReadDayTemperature(daily, 1, "from"),
                TomorrowLow: ReadDayTemperature(daily, 1, "to"),
                PrimaryAlert: ReadAlert(root),
                UpdatedAt: ReadUpdatedAt(root));
            return true;
        }
        catch (JsonException)
        {
            // 截断的 JSON、HTML 错误页、编码不对都走这里：安静地当作「没拿到」。
            return false;
        }
    }

    /// <summary>读一个子对象；缺失或不是对象时给 <see langword="null" />（后续的取值全走空安全）。</summary>
    private static JsonElement? GetObject(JsonElement? parent, string name)
    {
        if (parent is not { ValueKind: JsonValueKind.Object } element)
            return null;

        return element.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.Object
            ? child
            : null;
    }

    /// <summary>读一串嵌套属性，任一层缺失就给 <see langword="null" />。</summary>
    private static JsonElement? GetPath(JsonElement? parent, params string[] path)
    {
        var current = parent;
        foreach (var name in path)
        {
            if (current is not { ValueKind: JsonValueKind.Object } element)
                return null;

            if (!element.TryGetProperty(name, out var child))
                return null;

            current = child;
        }

        return current;
    }

    /// <summary>
    ///     读一个整数：响应里既可能是字符串 <c>"9"</c>，也可能是数字 <c>9</c>，两种都认。
    ///     小数按四舍五入取整（温度见过 <c>"17.5"</c>）。
    /// </summary>
    private static int? ReadInt(JsonElement? element)
    {
        if (element is not { } value)
            return null;

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetInt32(out var number)
                    ? number
                    : value.TryGetDouble(out var real) ? (int)Math.Round(real) : null;

            case JsonValueKind.String:
            {
                var text = value.GetString()?.Trim();
                if (string.IsNullOrEmpty(text))
                    return null;

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;

                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var realValue)
                    ? (int)Math.Round(realValue)
                    : null;
            }

            default:
                return null;
        }
    }

    /// <summary>读一个天气代码字段（字符串 "7" 或数字 7）。</summary>
    private static WeatherCondition ReadCondition(JsonElement? element)
    {
        if (element is not { } value)
            return WeatherCondition.Unknown;

        return value.ValueKind switch
        {
            JsonValueKind.String => XiaomiWeatherCode.ToCondition(value.GetString()),
            JsonValueKind.Number => ReadInt(value) is { } code
                ? XiaomiWeatherCode.ToCondition(code)
                : WeatherCondition.Unknown,
            _ => WeatherCondition.Unknown
        };
    }

    /// <summary>
    ///     读逐日预报里第 <paramref name="index" /> 天的一个字段：
    ///     <c>forecastDaily.weather.value[]</c> 这类节点是<b>两层</b>结构
    ///     （<c>{ "unit": …, "value": [ … ] }</c>），数组里每一项是 <c>{ "from": …, "to": … }</c>。
    /// </summary>
    private static JsonElement? ReadDayEntry(JsonElement? daily, string field, int index)
    {
        var array = GetPath(daily, field, "value");
        if (array is not { ValueKind: JsonValueKind.Array } list || index >= list.GetArrayLength())
            return null;

        var entry = list[index];
        return entry.ValueKind == JsonValueKind.Object ? entry : null;
    }

    /// <summary>读第 N 天的天气现象（<c>from</c> 那一档，即「白天」）。</summary>
    private static WeatherCondition ReadDayCondition(JsonElement? daily, int index) =>
        ReadCondition(GetPath(ReadDayEntry(daily, "weather", index), "from"));

    /// <summary>读第 N 天的最高 / 最低温度。</summary>
    private static int? ReadDayTemperature(JsonElement? daily, int index, string key) =>
        ReadInt(GetPath(ReadDayEntry(daily, "temperature", index), key));

    /// <summary>读第一条预警的标题；没有预警（空数组 / 缺字段）给 <see langword="null" />。</summary>
    private static string? ReadAlert(JsonElement root)
    {
        if (!root.TryGetProperty("alerts", out var alerts) || alerts.ValueKind != JsonValueKind.Array)
            return null;

        if (alerts.GetArrayLength() == 0)
            return null;

        var first = alerts[0];
        if (first.ValueKind != JsonValueKind.Object)
            return null;

        var title = GetPath(first, "title");
        if (title is not { ValueKind: JsonValueKind.String } element)
            return null;

        var text = element.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    ///     读更新时刻：优先 <c>updateTime</c>（毫秒时间戳），退回 <c>current.pubTime</c>（ISO 串）。
    ///     越界的时间戳（比如 0）直接当没有——0 会显示成 1970 年。
    /// </summary>
    private static DateTimeOffset? ReadUpdatedAt(JsonElement root)
    {
        if (root.TryGetProperty("updateTime", out var updateTime))
        {
            var milliseconds = ReadInt(updateTime);
            if (milliseconds is { } value && value > 0)
            {
                try
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(value);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // 明显不是毫秒时间戳的脏值，落到下面的 pubTime 分支。
                }
            }
        }

        var pubTime = GetPath(GetObject(root, "current"), "pubTime");
        if (pubTime is { ValueKind: JsonValueKind.String } element &&
            DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        return null;
    }
}
