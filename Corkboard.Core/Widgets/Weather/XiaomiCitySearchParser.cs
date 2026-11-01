using System.Globalization;
using System.Text.Json;

namespace Corkboard.Core.Widgets.Weather;

/// <summary>
///     把城市搜索的响应读成 <see cref="XiaomiCity" /> 列表。纯函数、不碰网络，有单测。
///     <para>
///         响应是<b>数组</b>：每项 <c>{ name, affiliation, latitude, longitude, locationKey, … }</c>，
///         其中经纬度是<b>字符串</b>。读不出来的条目直接跳过（少一条总比整张列表空着好），
///         顶层不是数组才返回 <see langword="false" />。
///     </para>
/// </summary>
public static class XiaomiCitySearchParser
{
    /// <summary>
    ///     试把一份城市搜索响应读成列表。
    /// </summary>
    /// <param name="json">响应正文；可以是 <see langword="null" /> 或任意脏串。</param>
    /// <param name="cities">读出来的城市（失败时为空列表）。</param>
    /// <returns>顶层是不是一个合法的数组。</returns>
    public static bool TryParse(string? json, out IReadOnlyList<XiaomiCity> cities)
    {
        cities = [];

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
                return false;

            var result = new List<XiaomiCity>(root.GetArrayLength());
            foreach (var entry in root.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    continue;

                var name = ReadString(entry, "name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var latitude = ReadDouble(entry, "latitude");
                var longitude = ReadDouble(entry, "longitude");
                if (latitude is null || longitude is null)
                    continue;

                result.Add(new XiaomiCity(
                    name.Trim(),
                    TrimmedOrNull(ReadString(entry, "affiliation")),
                    latitude.Value,
                    longitude.Value,
                    TrimmedOrNull(ReadString(entry, "locationKey"))));
            }

            cities = result;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>读一个字符串字段（数字也顺手转成字符串，免得类型一变整条就丢）。</summary>
    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
            return null;

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
    }

    /// <summary>读一个经纬度：字符串或数字都认，按不变文化解析（别让区域设置把小数点当逗号）。</summary>
    private static double? ReadDouble(JsonElement root, string name)
    {
        var text = ReadString(root, name)?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
               double.IsFinite(value)
            ? value
            : null;
    }

    private static string? TrimmedOrNull(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
