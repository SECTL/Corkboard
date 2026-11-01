using System.Text.Json;

namespace Corkboard.Core.Widgets.Quote;

/// <summary>
///     把一言 API 的响应 JSON 读成 <see cref="HitokotoQuote" /> —— 纯函数、不碰网络，有单测。
///     <para>
///         直接用 <see cref="JsonDocument" /> 手读，不定义 DTO：字段只有六个，而 DTO 会因为
///         「服务端多给了一个字段」或「<c>length</c> 变成了字符串」这类事把整个响应读崩；
///         手读可以逐字段决定「没有就用兜底」，更适合一个随时可能改的小接口。
///     </para>
///     <para>
///         ⚠️ <b>不抛异常</b>：响应可能是被代理截断的半截 JSON、可能是一页 HTML 错误页、
///         也可能是个数组（有人把 <c>?encode=js</c> 改了回去）。认不出来就返回 <see langword="false" />、
///         <c>quote</c> 留 <see langword="null" />，调用方照旧显示 <see cref="HitokotoQuote.Empty" />。
///     </para>
/// </summary>
public static class HitokotoQuoteParser
{
    /// <summary>正文的字段名。</summary>
    private const string TextName = "hitokoto";

    /// <summary>
    ///     试把一个 JSON 响应读成一句话。
    ///     <para>
    ///         只有 <c>hitokoto</c> 是硬要求：它缺失或者是纯空白时整条响应就没有意义（返回
    ///         <see langword="false" />）；<c>type</c> / <c>from</c> / <c>from_who</c> / <c>uuid</c> /
    ///         <c>length</c> 都是「读不出来就兜底」，不影响成败 —— 少一个字段仍然是一句能显示的话。
    ///     </para>
    /// </summary>
    /// <param name="json">响应正文；可以是 <see langword="null" /> 或任意脏串。</param>
    /// <param name="quote">读出来的句子；失败时为 <see langword="null" />。</param>
    /// <returns>是不是读到了一句话。</returns>
    public static bool TryParse(string? json, out HitokotoQuote? quote)
    {
        quote = null;

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // 顶层必须是对象：数组（批量查询）与裸字符串都不认。
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!TryReadString(root, TextName, out var text) || string.IsNullOrWhiteSpace(text))
                return false;

            TryReadString(root, "from", out var source);
            TryReadString(root, "from_who", out var author);
            TryReadString(root, "uuid", out var uuid);
            TryReadString(root, "type", out var type);

            // 单字母才去查表：多字符 / 空白 / 缺失都算「认不出来」，兜底成「其他」。
            var category = type.Length == 1
                ? QuoteCategoryExtensions.FromCategoryChar(type[0])
                : QuoteCategory.None;
            if (category == QuoteCategory.None)
                category = QuoteCategory.Other;

            // 服务端的 length 是算好的字符数；没有或者读不成就按正文字数顶一下，
            // 这样界面上的「共 N 字」不会出现 0 字。
            var length = TryReadLength(root, "length") ?? text.Length;

            quote = new HitokotoQuote(text, OrNull(source), OrNull(author), category, OrNull(uuid), length);
            return true;
        }
        catch (JsonException)
        {
            // 不合法的 JSON（截断、HTML 错误页、编码不对）走这里，安静地当作「没有拿到」。
            return false;
        }
    }

    /// <summary>读一个字符串字段：缺失或不是字符串都返回 <see langword="false" />（不抛异常）。</summary>
    private static bool TryReadString(JsonElement root, string name, out string value)
    {
        value = string.Empty;

        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;

        value = element.GetString() ?? string.Empty;
        return true;
    }

    /// <summary>
    ///     读 <c>length</c>：缺失、不是数字、是小数、或者非正数都返回 <see langword="null" />
    ///     （交给调用方用正文字数兜底）。负数与 0 都不可能是真实字数，留着只会让界面显示「共 0 字」。
    /// </summary>
    private static int? TryReadLength(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Number)
            return null;

        if (!element.TryGetInt32(out var value) || value <= 0)
            return null;

        return value;
    }

    /// <summary>
    ///     空白串按「没有」处理，顺带去掉首尾空白：卡片上要贴边显示，服务端偶尔在
    ///     <c>from_who</c> 里留个尾空格，那一行就会多出一个洞。
    /// </summary>
    private static string? OrNull(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
