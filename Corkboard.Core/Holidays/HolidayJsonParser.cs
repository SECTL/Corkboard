using System.Globalization;
using System.Text.Json;

namespace Corkboard.Core.Holidays;

/// <summary>节假日数据的可信度阈值：越过就当成拉到了错误页面（登录页、404、限流提示）。</summary>
public static class HolidayDataRules
{
    public const int MinDays = 10;

    public const int MaxDays = 80;

    public const int MaxMakeupDays = 15;

    /// <summary>数据看起来是否像一份正常年份表。</summary>
    public static bool IsPlausible(HolidayYearData? data, out string reason)
    {
        if (data is null)
        {
            reason = "没有解析出任何数据";
            return false;
        }

        if (data.Days.Count < MinDays || data.Days.Count > MaxDays)
        {
            reason = $"条目数 {data.Days.Count} 不在 {MinDays}–{MaxDays} 之间";
            return false;
        }

        if (data.MakeupDayCount > MaxMakeupDays)
        {
            reason = $"调休补班 {data.MakeupDayCount} 条过多";
            return false;
        }

        if (data.OffDayCount == 0)
        {
            reason = "一条放假数据都没有";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    ///     数据里的日期是否都落在「请求年份 ±1」之内。
    ///     <para>
    ///         上游可能把跨年那几天写进相邻年份文件（12 月的元旦假期、1 月的春节调休），
    ///         所以允许前后各一年；再远就说明拉到了错误年份的文件（或被 CDN 缓存串了）。
    ///     </para>
    /// </summary>
    public static bool IsYearConsistent(HolidayYearData? data, int requestedYear)
    {
        if (data is null || data.Days.Count == 0)
            return false;

        return data.Days.All(day => Math.Abs(day.Date.Year - requestedYear) <= 1);
    }
}

/// <summary>
///     解析节假日 JSON，兼容两套来源：
///     <list type="bullet">
///         <item>holiday-cn：<c>{"year":2026,"papers":[…],"days":[{"date":"2026-01-01","name":"元旦","isOffDay":true}]}</c></item>
///         <item>chinese-days：<c>{"holidays":{"2026-01-01":"元旦"},"workdays":{"2026-02-14":"…"}}</c></item>
///     </list>
///     解析永远是宽容的：坏日期跳过、缺字段取默认，不抛异常。
/// </summary>
public static class HolidayJsonParser
{
    public static bool TryParse(string? json, int fallbackYear, out HolidayYearData? data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("days", out var days) && days.ValueKind == JsonValueKind.Array)
                return TryParseHolidayCn(root, days, fallbackYear, out data);

            if (root.TryGetProperty("holidays", out var holidays) && holidays.ValueKind == JsonValueKind.Object)
                return TryParseChineseDays(root, holidays, fallbackYear, out data);

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>只解析 holiday-cn 的逐年格式。</summary>
    public static bool TryParseHolidayCn(string? json, int fallbackYear, out HolidayYearData? data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("days", out var days)
                || days.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return TryParseHolidayCn(root, days, fallbackYear, out data);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>只解析 chinese-days 的逐年格式。</summary>
    public static bool TryParseChineseDays(string? json, int fallbackYear, out HolidayYearData? data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("holidays", out var holidays)
                || holidays.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return TryParseChineseDays(root, holidays, fallbackYear, out data);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseHolidayCn(
        JsonElement root,
        JsonElement days,
        int fallbackYear,
        out HolidayYearData? data)
    {
        data = null;
        var result = new HolidayYearData
        {
            Year = ReadYear(root) ?? fallbackYear,
            SourceName = "holiday-cn",
            SourceUrl = "https://github.com/NateScarlet/holiday-cn",
            License = "MIT",
            FetchedAt = DateTimeOffset.Now
        };

        if (root.TryGetProperty("papers", out var papers) && papers.ValueKind == JsonValueKind.Array)
        {
            foreach (var paper in papers.EnumerateArray())
            {
                if (paper.ValueKind == JsonValueKind.String && paper.GetString() is { Length: > 0 } url)
                    result.Papers.Add(url);
            }
        }

        var seen = new HashSet<DateOnly>();
        foreach (var item in days.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("date", out var dateElement)
                || dateElement.ValueKind != JsonValueKind.String
                || !TryParseDate(dateElement.GetString(), out var date)
                || !seen.Add(date))
            {
                continue;
            }

            var name = item.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;

            var isOffDay = !item.TryGetProperty("isOffDay", out var offElement)
                           || offElement.ValueKind == JsonValueKind.True;

            result.Days.Add(new HolidayDay { Date = date, Name = name.Trim(), IsOffDay = isOffDay });
        }

        result.Days.Sort((left, right) => left.Date.CompareTo(right.Date));
        data = result;
        return result.Days.Count > 0;
    }

    private static bool TryParseChineseDays(
        JsonElement root,
        JsonElement holidays,
        int fallbackYear,
        out HolidayYearData? data)
    {
        data = null;
        var result = new HolidayYearData
        {
            Year = ReadYear(root) ?? fallbackYear,
            SourceName = "chinese-days",
            SourceUrl = "https://github.com/vsme/chinese-days",
            License = "MIT",
            FetchedAt = DateTimeOffset.Now
        };

        var seen = new HashSet<DateOnly>();
        AddDictionary(holidays, isOffDay: true, result, seen);

        if (root.TryGetProperty("workdays", out var workdays) && workdays.ValueKind == JsonValueKind.Object)
            AddDictionary(workdays, isOffDay: false, result, seen);

        result.Days.Sort((left, right) => left.Date.CompareTo(right.Date));
        data = result;
        return result.Days.Count > 0;
    }

    private static void AddDictionary(
        JsonElement element,
        bool isOffDay,
        HolidayYearData result,
        HashSet<DateOnly> seen)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!TryParseDate(property.Name, out var date) || !seen.Add(date))
                continue;

            var name = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : string.Empty;

            result.Days.Add(new HolidayDay { Date = date, Name = name.Trim(), IsOffDay = isOffDay });
        }
    }

    private static int? ReadYear(JsonElement root)
    {
        if (!root.TryGetProperty("year", out var year))
            return null;

        return year.ValueKind switch
        {
            JsonValueKind.Number when year.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(year.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>接受 <c>yyyy-MM-dd</c> 与 <c>yyyy/MM/dd</c> 两种写法。</summary>
    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
        || DateOnly.TryParseExact(text, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
