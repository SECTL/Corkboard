using System.Globalization;
using System.Text;

namespace Corkboard.Core.Widgets.Quote;

/// <summary>
///     把 <see cref="HitokotoQueryOptions" /> 拼成一言 API 的查询串 —— 纯函数、不碰网络，有单测。
///     <para>
///         端点与参数照官方文档：<c>https://v1.hitokoto.cn/</c>（国内，QPS 2）、
///         <c>https://international.v1.hitokoto.cn/</c>（海外，QPS 20），无 key / token。
///         两处都是 HTTPS，不要为了省一次握手退回 http。
///     </para>
/// </summary>
public static class HitokotoQuery
{
    /// <summary>国内端点：QPS 2，够一块桌面小组件用。</summary>
    public const string DomesticEndpoint = "https://v1.hitokoto.cn/";

    /// <summary>海外端点：QPS 20，机房在境外时用这个，免得绕回来一圈。</summary>
    public const string InternationalEndpoint = "https://international.v1.hitokoto.cn/";

    /// <summary>服务端默认的最长字数（含）。选项里的默认值也取它，两边不要各写一个 30。</summary>
    public const int DefaultMaxLength = 30;

    /// <summary>服务端能接受的最长字数上限；超过没意义（接口会按 100 处理），夹到这儿。</summary>
    public const int MaxAllowedLength = 100;

    /// <summary>
    ///     内建参数名（小写）。用户附加串里出现这些键时<b>一律忽略</b>：
    ///     让人把 <c>encode</c> 改成 <c>js</c> 我们这边就解析不出响应了，<c>c</c>/<c>min_length</c> 之类
    ///     则与界面上那几个控件打架（两处写着不同的值，谁赢取决于解析顺序）。
    /// </summary>
    private static readonly string[] BuiltInKeys =
        ["c", "encode", "charset", "callback", "select", "min_length", "max_length"];

    /// <summary>
    ///     按固定顺序拼出查询键值对：<c>encode</c>、<c>charset</c>、若干 <c>c</c>、
    ///     <c>min_length</c>、<c>max_length</c>，最后才是用户附加串里活下来的那些。
    ///     <para>
    ///         值已经做过 URL 编码（<see cref="Uri.EscapeDataString" />），调用方直接拼进 URL 即可，
    ///         <b>不要再编一次</b>，否则中文会变成 <c>%25E4…</c> 这种双份编码。
    ///     </para>
    ///     <para>
    ///         <c>select</c> 与 <c>callback</c> 我们自己不传：前者是服务端的随机策略（默认就够用），
    ///         后者会把响应包成 JSONP —— 我们固定要 JSON。
    ///     </para>
    /// </summary>
    /// <param name="options">请求选项；内部的脏值会先经 <see cref="HitokotoQueryOptions.Normalize" /> 夹一次。</param>
    /// <returns>按顺序排好的键值对（键是参数名，值是已编码的值）。</returns>
    public static IReadOnlyList<KeyValuePair<string, string>> BuildQueryPairs(HitokotoQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalized = options.Normalize();

        // 上限 12 = encode + charset + 11 个类别（j 已停用），给附加串留的余量由 List 自己长。
        var pairs = new List<KeyValuePair<string, string>>(12)
        {
            new("encode", "json"),
            new("charset", "utf-8")
        };

        foreach (var letter in QuoteCategoryExtensions.ToCategoryChars(normalized.Categories))
            pairs.Add(new KeyValuePair<string, string>("c", letter));

        // 0 表示「不限」，而服务端默认就是 0，所以带不带都一样 —— 不带，查询串更短。
        if (normalized.MinLength > 0)
            pairs.Add(new KeyValuePair<string, string>("min_length", ToInvariant(normalized.MinLength)));

        // 0 表示「由服务端决定」，别把我们这边的 0 传过去（那才是真的「不限长度」）。
        if (normalized.MaxLength > 0)
            pairs.Add(new KeyValuePair<string, string>("max_length", ToInvariant(normalized.MaxLength)));

        AppendExtraQuery(normalized.ExtraQuery, pairs, ignoredKeys: null);

        return pairs;
    }

    /// <summary>
    ///     拼出可以直接请求的地址：按 <see cref="HitokotoQueryOptions.UseInternationalEndpoint" />
    ///     选端点，后面接 <see cref="BuildQueryPairs" /> 的结果。
    /// </summary>
    /// <param name="options">请求选项。</param>
    /// <returns>绝对 URI。</returns>
    public static Uri BuildUri(HitokotoQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var endpoint = options.UseInternationalEndpoint ? InternationalEndpoint : DomesticEndpoint;
        var pairs = BuildQueryPairs(options);

        var builder = new StringBuilder(endpoint.Length + pairs.Count * 16);
        builder.Append(endpoint).Append('?');
        for (var index = 0; index < pairs.Count; index++)
        {
            if (index > 0)
                builder.Append('&');

            builder.Append(pairs[index].Key).Append('=').Append(pairs[index].Value);
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    /// <summary>
    ///     用户附加串里被忽略掉的键名（去重、保持出现顺序），给设置页做「这些参数不支持」的提示用。
    ///     <para>
    ///         只报<b>内建参数名</b>这一种情况：格式不对的片段（没有等号、空键、空值）用户多半是随手粘错了，
    ///         逐个念出来只会更乱；而「你填的 encode 我们不用」必须说出来，否则用户会以为改动生效了。
    ///     </para>
    ///     <para>
    ///         比对不分大小写（<c>Encode</c> 也算内建），返回的是用户原来写的那个写法。
    ///     </para>
    /// </summary>
    /// <param name="extraQuery">用户手填的附加查询串。</param>
    /// <returns>被忽略的键名；没有时为空的表。</returns>
    public static IReadOnlyList<string> FindIgnoredQueryKeys(string? extraQuery)
    {
        var ignored = new List<string>();
        AppendExtraQuery(extraQuery, acceptedKeys: null, ignoredKeys: ignored);
        return ignored;
    }

    /// <summary>
    ///     把 <c>a=1&amp;b=2</c> 这样的附加串拆开，一份走「收下」、一份走「忽略」，两个列表按需给。
    ///     <para>
    ///         值按<b>字面</b>处理再编码，不先解码：用户多半是照文档抄 <c>select=max</c> 这种原样参数，
    ///         先解码会把字面的 <c>%</c> 吃掉，反而更难解释。
    ///     </para>
    /// </summary>
    private static void AppendExtraQuery(string? extraQuery,
        List<KeyValuePair<string, string>>? acceptedKeys, List<string>? ignoredKeys)
    {
        if (string.IsNullOrWhiteSpace(extraQuery))
            return;

        // 去重只看「忽略」那一份，保持用户写的顺序与大小写。
        var seenIgnored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fragment in extraQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            // 整串带前导 ? 时，第一段会以 ? 开头，剥掉再解析。
            var pair = fragment.Trim().TrimStart('?');
            var separator = pair.IndexOf('=');

            // 没有等号 = 没有值（服务端对无值参数的处理各不相同，不如丢掉）；等号在开头 = 空键。
            if (separator <= 0)
                continue;

            var key = pair[..separator].Trim();
            var value = pair[(separator + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
                continue;

            if (IsBuiltInKey(key))
            {
                if (seenIgnored.Add(key))
                    ignoredKeys?.Add(key);

                continue;
            }

            acceptedKeys?.Add(new KeyValuePair<string, string>(
                Uri.EscapeDataString(key), Uri.EscapeDataString(value)));
        }
    }

    /// <summary>是不是内建参数名（不分大小写）。</summary>
    private static bool IsBuiltInKey(string key)
    {
        foreach (var builtIn in BuiltInKeys)
        {
            if (key.Equals(builtIn, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>数字一律按不变文化格式化，别让某些区域设置写出非 ASCII 数字。</summary>
    private static string ToInvariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
