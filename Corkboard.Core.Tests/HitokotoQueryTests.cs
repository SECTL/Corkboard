using Corkboard.Core.Widgets.Quote;

namespace Corkboard.Core.Tests;

/// <summary>
///     一言查询串的拼法：端点常量、类别字母的翻译（含已停用的 <c>j</c>）、长度夹取，
///     以及用户附加查询串里哪些留着、哪些必须丢掉。
///     <para>
///     这里钉的是「拼出来的网址能被服务端正确理解」这一条契约。最要紧的两条：
///     一、<c>encode</c> 之类内建参数<b>不能被用户附加串覆盖</b>（改了我们就解析不了响应）；
///     二、<c>max_length</c> 超过 100 必须夹住，否则服务端会回 400 而不是把上限收到 100。
///     参数顺序也一起钉住 —— 手写测试数据、排查日志时顺序变来变去会很难比对。
///     </para>
/// </summary>
public class HitokotoQueryTests
{
    /// <summary>两个端点都必须是 HTTPS 且带结尾斜杠（拼查询串时直接接 <c>?</c>，少一条斜杠就成了 <c>.cn?x</c>）。</summary>
    [Fact]
    public void Constants_DescribeTheOfficialEndpoints()
    {
        Assert.Equal("https://v1.hitokoto.cn/", HitokotoQuery.DomesticEndpoint);
        Assert.Equal("https://international.v1.hitokoto.cn/", HitokotoQuery.InternationalEndpoint);
        Assert.Equal(30, HitokotoQuery.DefaultMaxLength);
        Assert.Equal(100, HitokotoQuery.MaxAllowedLength);

        Assert.StartsWith("https://", HitokotoQuery.DomesticEndpoint, StringComparison.Ordinal);
        Assert.EndsWith("/", HitokotoQuery.DomesticEndpoint, StringComparison.Ordinal);
        Assert.StartsWith("https://", HitokotoQuery.InternationalEndpoint, StringComparison.Ordinal);
        Assert.EndsWith("/", HitokotoQuery.InternationalEndpoint, StringComparison.Ordinal);
    }

    /// <summary>
    ///     位值落盘，所以数值本身就是契约：a=1、b=2、c=4…… 依次左移，已停用的 j=512 占着位不放。
    ///     <see cref="QuoteCategory.All" /> 是除 j 之外的全部位（3583），漏算一位就会少请求一类。
    /// </summary>
    [Fact]
    public void CategoryValues_KeepTheDocumentedBitLayout()
    {
        Assert.Equal(0, (int)QuoteCategory.None);
        Assert.Equal(1, (int)QuoteCategory.Animation);
        Assert.Equal(2, (int)QuoteCategory.Comic);
        Assert.Equal(4, (int)QuoteCategory.Game);
        Assert.Equal(8, (int)QuoteCategory.Literature);
        Assert.Equal(16, (int)QuoteCategory.Original);
        Assert.Equal(32, (int)QuoteCategory.Web);
        Assert.Equal(64, (int)QuoteCategory.Other);
        Assert.Equal(128, (int)QuoteCategory.Movie);
        Assert.Equal(256, (int)QuoteCategory.Poetry);
        Assert.Equal(512, (int)QuoteCategory.NeteaseMusic);
        Assert.Equal(1024, (int)QuoteCategory.Philosophy);
        Assert.Equal(2048, (int)QuoteCategory.Funny);

        Assert.Equal(3583, (int)QuoteCategory.All);
        Assert.Equal(0, (int)(QuoteCategory.All & QuoteCategory.NeteaseMusic));
    }

    /// <summary>
    ///     勾了「全部」时要发出 11 个 <c>c</c>（a→l 里除了已停用的 j）：
    ///     少发一个等于悄悄把那一类从结果里排除了。
    /// </summary>
    [Fact]
    public void ToCategoryChars_EmitsEveryUsableLetterInOrder()
    {
        Assert.Equal(["a", "b", "c", "d", "e", "f", "g", "h", "i", "k", "l"],
            QuoteCategoryExtensions.ToCategoryChars(QuoteCategory.All));
    }

    /// <summary>
    ///     j 已停用：请求侧永远不出现它，哪怕配置里存着它、或者把它和「全部」叠在一起。
    /// </summary>
    [Theory]
    [InlineData(QuoteCategory.NeteaseMusic)]
    [InlineData(QuoteCategory.All | QuoteCategory.NeteaseMusic)]
    [InlineData(QuoteCategory.NeteaseMusic | QuoteCategory.Animation)]
    public void ToCategoryChars_SkipsTheRetiredNeteaseMusic(QuoteCategory categories)
    {
        Assert.DoesNotContain("j", QuoteCategoryExtensions.ToCategoryChars(categories));
    }

    /// <summary>
    ///     空类别 = 不限类别（一个 <c>c</c> 都不带），而不是「一类都不要」——
    ///     后者服务端做不到，我们要的也只是「随便来一句」。
    /// </summary>
    [Fact]
    public void ToCategoryChars_ReturnsEmptyForNoSelection()
    {
        Assert.Empty(QuoteCategoryExtensions.ToCategoryChars(QuoteCategory.None));

        // 只勾了已停用的那一类，退化成「不限」。
        Assert.Empty(QuoteCategoryExtensions.ToCategoryChars(QuoteCategory.NeteaseMusic));
    }

    /// <summary>老配置被手改过、或者以后加了新位时的脏位：忽略，不抛异常。</summary>
    [Fact]
    public void ToCategoryChars_IgnoresUnknownBits()
    {
        Assert.Empty(QuoteCategoryExtensions.ToCategoryChars((QuoteCategory)(1 << 20)));
        Assert.Equal(["a"], QuoteCategoryExtensions.ToCategoryChars(QuoteCategory.Animation | (QuoteCategory)(1 << 20)));
    }

    /// <summary>响应里的 <c>type</c> 单字母 → 类别位，a→l 全都要能翻（含已停用的 j —— 老句子还是这个类别）。</summary>
    [Theory]
    [InlineData('a', QuoteCategory.Animation)]
    [InlineData('b', QuoteCategory.Comic)]
    [InlineData('c', QuoteCategory.Game)]
    [InlineData('d', QuoteCategory.Literature)]
    [InlineData('e', QuoteCategory.Original)]
    [InlineData('f', QuoteCategory.Web)]
    [InlineData('g', QuoteCategory.Other)]
    [InlineData('h', QuoteCategory.Movie)]
    [InlineData('i', QuoteCategory.Poetry)]
    [InlineData('j', QuoteCategory.NeteaseMusic)]
    [InlineData('k', QuoteCategory.Philosophy)]
    [InlineData('l', QuoteCategory.Funny)]
    public void FromCategoryChar_MapsEveryLetter(char letter, QuoteCategory expected) =>
        Assert.Equal(expected, QuoteCategoryExtensions.FromCategoryChar(letter));

    /// <summary>大小写都认：中间隔着代理、或者测试数据手写成大写时不该报错。</summary>
    [Theory]
    [InlineData('A')]
    [InlineData('K')]
    [InlineData('J')]
    public void FromCategoryChar_IsCaseInsensitive(char letter) =>
        Assert.NotEqual(QuoteCategory.None, QuoteCategoryExtensions.FromCategoryChar(letter));

    /// <summary>认不出来的字母返回 <see cref="QuoteCategory.None" />，由调用方兜底（解析器兜成「其他」）。</summary>
    [Theory]
    [InlineData('z')]
    [InlineData('0')]
    [InlineData(' ')]
    [InlineData('\0')]
    public void FromCategoryChar_ReturnsNoneForUnknownLetters(char letter) =>
        Assert.Equal(QuoteCategory.None, QuoteCategoryExtensions.FromCategoryChar(letter));

    /// <summary>负数归零：界面上滑杆拖不出负数，但配置对象可能来自手改的 JSON。</summary>
    [Fact]
    public void Normalize_ClampsNegativesToZero()
    {
        var normalized = new HitokotoQueryOptions { MinLength = -5, MaxLength = -1 }.Normalize();

        Assert.Equal(0, normalized.MinLength);
        Assert.Equal(0, normalized.MaxLength);
    }

    /// <summary>上限超过 100 就压到 100（服务端只认到这个数）。</summary>
    [Fact]
    public void Normalize_CapsMaxLengthAtTheServerLimit()
    {
        Assert.Equal(100, new HitokotoQueryOptions { MaxLength = 500 }.Normalize().MaxLength);
        Assert.Equal(100, new HitokotoQueryOptions { MaxLength = 101 }.Normalize().MaxLength);
    }

    /// <summary>下限反超上限时把下限拉回上限：min 比 max 长是自相矛盾的条件，服务端只会回空。</summary>
    [Fact]
    public void Normalize_PullsMinLengthDownToMaxLength()
    {
        var normalized = new HitokotoQueryOptions { MinLength = 40, MaxLength = 20 }.Normalize();

        Assert.Equal(20, normalized.MinLength);
        Assert.Equal(20, normalized.MaxLength);
    }

    /// <summary>
    ///     <c>MaxLength = 0</c> 的意思是「别带这个参数」（长度交给服务端），
    ///     所以不能拿它去把用户填的下限抹成 0。
    /// </summary>
    [Fact]
    public void Normalize_KeepsMinLengthWhenMaxLengthIsZero()
    {
        var normalized = new HitokotoQueryOptions { MinLength = 40, MaxLength = 0 }.Normalize();

        Assert.Equal(40, normalized.MinLength);
        Assert.Equal(0, normalized.MaxLength);
    }

    /// <summary>
    ///     先压上限、再对齐下限：<c>min=400 / max=500</c> 应该变成 <c>100 / 100</c>，
    ///     而不是留下 <c>400 / 100</c> 这种仍然反着来的组合。
    /// </summary>
    [Fact]
    public void Normalize_CapsMaxBeforePullingMinDown()
    {
        var normalized = new HitokotoQueryOptions { MinLength = 400, MaxLength = 500 }.Normalize();

        Assert.Equal(100, normalized.MinLength);
        Assert.Equal(100, normalized.MaxLength);
    }

    /// <summary>合法值原样返回，并且不修改调用方手上那一份（不可变 record 的意义就在这儿）。</summary>
    [Fact]
    public void Normalize_LeavesValidOptionsAloneAndDoesNotMutateTheSource()
    {
        var original = new HitokotoQueryOptions
        {
            Categories = QuoteCategory.Poetry,
            MinLength = 5,
            MaxLength = 25,
            ExtraQuery = "x=1",
            UseInternationalEndpoint = true
        };

        var normalized = original.Normalize();

        Assert.Equal(original, normalized);
        Assert.NotSame(original, normalized);
        Assert.Equal(25, original.MaxLength);
    }

    /// <summary>
    ///     默认选项的完整查询串：先是固定的 <c>encode</c> / <c>charset</c>，再按 a→l 发 11 个 <c>c</c>，
    ///     最后是 <c>max_length=30</c>。<c>min_length</c> 为 0 时不出现。
    /// </summary>
    [Fact]
    public void BuildQueryPairs_DefaultOptions_SpellsOutEveryUsableCategory()
    {
        Assert.Equal(
        [
            "encode=json", "charset=utf-8",
            "c=a", "c=b", "c=c", "c=d", "c=e", "c=f", "c=g", "c=h", "c=i", "c=k", "c=l",
            "max_length=30"
        ], Flatten(HitokotoQuery.BuildQueryPairs(new HitokotoQueryOptions())));
    }

    /// <summary><c>encode</c> 固定 json、<c>charset</c> 固定 utf-8：解析器只认这一种响应。</summary>
    [Fact]
    public void BuildQueryPairs_AlwaysAsksForJsonInUtf8()
    {
        var pairs = HitokotoQuery.BuildQueryPairs(new HitokotoQueryOptions { Categories = QuoteCategory.None });

        Assert.Equal(new KeyValuePair<string, string>("encode", "json"), pairs[0]);
        Assert.Equal(new KeyValuePair<string, string>("charset", "utf-8"), pairs[1]);
    }

    /// <summary>
    ///     0 一律不出现在查询串里：<c>min_length=0</c> 本来就是服务端默认，
    ///     而 <c>max_length=0</c> 在服务端是「不限长度」—— 与这里的「别带这个参数」不是一回事。
    /// </summary>
    [Fact]
    public void BuildQueryPairs_OmitsZeroLengths()
    {
        var pairs = Flatten(HitokotoQuery.BuildQueryPairs(new HitokotoQueryOptions
        {
            Categories = QuoteCategory.Poetry,
            MinLength = 0,
            MaxLength = 0
        }));

        Assert.Equal(["encode=json", "charset=utf-8", "c=i"], pairs);
    }

    /// <summary>每个类别各发一条 <c>c=</c>（服务端按重复参数收），并且保持 a→l 的顺序。</summary>
    [Fact]
    public void BuildQueryPairs_EmitsOnePairPerCategory()
    {
        var pairs = Flatten(HitokotoQuery.BuildQueryPairs(new HitokotoQueryOptions
        {
            Categories = QuoteCategory.Funny | QuoteCategory.Game | QuoteCategory.Animation,
            MinLength = 3,
            MaxLength = 12
        }));

        Assert.Equal(["encode=json", "charset=utf-8", "c=a", "c=c", "c=l", "min_length=3", "max_length=12"], pairs);
    }

    /// <summary>拼之前先夹一次脏值：不夹的话 <c>max_length=999</c> 会被服务端拒掉。</summary>
    [Fact]
    public void BuildQueryPairs_NormalizesDirtyLengths()
    {
        var pairs = Flatten(HitokotoQuery.BuildQueryPairs(new HitokotoQueryOptions
        {
            Categories = QuoteCategory.None,
            MinLength = -3,
            MaxLength = 999
        }));

        Assert.Equal(["encode=json", "charset=utf-8", "max_length=100"], pairs);
    }

    /// <summary>
    ///     附加串排在内建参数之后，并且保持用户写的顺序：前面那几项的顺序永远不受用户输入影响。
    ///     （这里刻意不用 <c>select</c> —— 它也是内建参数名，会被丢掉，用例见下一个。）
    /// </summary>
    [Fact]
    public void BuildQueryPairs_AppendsExtraQueryLast()
    {
        var pairs = Flatten(HitokotoQuery.BuildQueryPairs(new HitokotoQueryOptions
        {
            Categories = QuoteCategory.None,
            MaxLength = 0,
            ExtraQuery = "foo=bar&baz=qux"
        }));

        Assert.Equal(["encode=json", "charset=utf-8", "foo=bar", "baz=qux"], pairs);
    }

    /// <summary>
    ///     内建参数名出现在附加串里时一律丢掉：让用户把 <c>encode</c> 改成 <c>js</c>，
    ///     或者让 <c>c</c> 与界面上勾的类别同时存在，都会让结果变得没法解释。
    /// </summary>
    [Theory]
    [InlineData("encode=js")]
    [InlineData("charset=gbk")]
    [InlineData("callback=cb")]
    [InlineData("select=random")]
    [InlineData("min_length=1")]
    [InlineData("max_length=60")]
    [InlineData("c=a")]
    [InlineData("ENCODE=js")] // 大小写也不放过
    public void BuildQueryPairs_IgnoresBuiltInKeysFromExtraQuery(string extraQuery)
    {
        var pairs = Flatten(HitokotoQuery.BuildQueryPairs(new HitokotoQueryOptions
        {
            Categories = QuoteCategory.None,
            MaxLength = 0,
            ExtraQuery = extraQuery
        }));

        Assert.Equal(["encode=json", "charset=utf-8"], pairs);
    }

    /// <summary>格式不对的片段直接跳过，不能让用户粘错一个字就整条请求失效。</summary>
    [Theory]
    [InlineData("&&")]
    [InlineData("justakey")]
    [InlineData("=value")]
    [InlineData("foo=")]
    [InlineData("foo=   ")]
    [InlineData("   ")]
    [InlineData("=")]
    public void BuildQueryPairs_IgnoresMalformedFragments(string extraQuery)
    {
        var pairs = Flatten(HitokotoQuery.BuildQueryPairs(new HitokotoQueryOptions
        {
            Categories = QuoteCategory.None,
            MaxLength = 0,
            ExtraQuery = extraQuery
        }));

        Assert.Equal(["encode=json", "charset=utf-8"], pairs);
    }

    /// <summary>键与值都要 URL 编码：中文、空格、<c>&amp;</c> 原样拼进 URL 会把这个参数截成两半。</summary>
    [Fact]
    public void BuildQueryPairs_UrlEncodesExtraKeysAndValues()
    {
        var pairs = HitokotoQuery.BuildQueryPairs(new HitokotoQueryOptions
        {
            Categories = QuoteCategory.None,
            MaxLength = 0,
            ExtraQuery = "note=你好 世界&a b=c"
        });

        Assert.Equal("note", pairs[2].Key);
        Assert.Equal(Uri.EscapeDataString("你好 世界"), pairs[2].Value);
        Assert.Equal("%E4%BD%A0%E5%A5%BD%20%E4%B8%96%E7%95%8C", pairs[2].Value);
        Assert.Equal("a%20b", pairs[3].Key);
    }

    /// <summary>
    ///     用户粘的整串可能带着前导 <c>?</c>（从地址栏复制就是这个样子），剥掉照常解析；
    ///     重复的键原样保留（服务端对重复参数是按顺序收的）。
    /// </summary>
    [Fact]
    public void BuildQueryPairs_ToleratesLeadingQuestionMarkAndRepeatedKeys()
    {
        var pairs = Flatten(HitokotoQuery.BuildQueryPairs(new HitokotoQueryOptions
        {
            Categories = QuoteCategory.None,
            MaxLength = 0,
            ExtraQuery = "?foo=1&foo=2"
        }));

        Assert.Equal(["encode=json", "charset=utf-8", "foo=1", "foo=2"], pairs);
    }

    /// <summary>设置页要照着这个提示「这些参数不支持」，所以顺序与写法都跟着用户输入走。</summary>
    [Fact]
    public void FindIgnoredQueryKeys_ReportsBuiltInKeysInOrder()
    {
        Assert.Equal(["encode", "c", "charset"],
            HitokotoQuery.FindIgnoredQueryKeys("encode=js&c=a&charset=gbk&foo=1"));
    }

    /// <summary>同一个键写两遍只报一次，保留第一次那个写法（用户看着自己的原文容易对上）。</summary>
    [Fact]
    public void FindIgnoredQueryKeys_DeduplicatesAndKeepsTheFirstSpelling()
    {
        Assert.Equal(["encode"],
            HitokotoQuery.FindIgnoredQueryKeys("encode=js&Encode=js2&ENCODE=js3"));
    }

    /// <summary>没有附加串、或者一个内建参数都没写：空的表（界面据此不显示提示）。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("foo=1&bar=2")]
    [InlineData("justakey&=value&foo=")]
    public void FindIgnoredQueryKeys_ReturnsEmptyWhenNothingIsIgnored(string? extraQuery) =>
        Assert.Empty(HitokotoQuery.FindIgnoredQueryKeys(extraQuery));

    /// <summary>默认走国内端点，参数顺序与 <see cref="HitokotoQuery.BuildQueryPairs" /> 完全一致。</summary>
    [Fact]
    public void BuildUri_UsesTheDomesticEndpointByDefault()
    {
        var uri = HitokotoQuery.BuildUri(new HitokotoQueryOptions
        {
            Categories = QuoteCategory.Animation | QuoteCategory.Game,
            MinLength = 5,
            MaxLength = 20
        });

        Assert.Equal("https://v1.hitokoto.cn/?encode=json&charset=utf-8&c=a&c=c&min_length=5&max_length=20",
            uri.AbsoluteUri);
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        Assert.Equal("v1.hitokoto.cn", uri.Host);
    }

    /// <summary>海外端点同理，只是主机名换一个（机房在境外时用它，QPS 从 2 变成 20）。</summary>
    [Fact]
    public void BuildUri_UsesTheInternationalEndpointWhenAsked()
    {
        var uri = HitokotoQuery.BuildUri(new HitokotoQueryOptions
        {
            Categories = QuoteCategory.Poetry,
            MaxLength = 0,
            UseInternationalEndpoint = true
        });

        Assert.Equal("https://international.v1.hitokoto.cn/?encode=json&charset=utf-8&c=i", uri.AbsoluteUri);
        Assert.Equal("international.v1.hitokoto.cn", uri.Host);
    }

    /// <summary>
    ///     编码过的值进 URL 之后仍然是<b>一个</b>参数：中文不转义的话，URL 会被下游按字节截断或拒绝。
    /// </summary>
    [Fact]
    public void BuildUri_KeepsEncodedExtraQueryAsOneParameter()
    {
        var uri = HitokotoQuery.BuildUri(new HitokotoQueryOptions
        {
            Categories = QuoteCategory.None,
            MaxLength = 0,
            ExtraQuery = "note=你好"
        });

        Assert.Equal("?encode=json&charset=utf-8&note=%E4%BD%A0%E5%A5%BD", uri.Query);
        Assert.DoesNotContain("你好", uri.Query, StringComparison.Ordinal);
    }

    /// <summary>
    ///     已知限制，一起钉住：附加串是按<b>字面</b>处理再编码的（不先解码），所以
    ///     一、值里写裸 <c>&amp;</c> 会被当成分段符（后半截成了没等号的碎片、被丢掉）；
    ///     二、提前转义好的 <c>%26</c> 会被再编码一次变成 <c>%2526</c>。
    ///     <para>
    ///     对一言来说这两种情况都用不上（附加串能放的只有少量服务端自定义参数），
    ///     但改语义之前得先有人知道现在是这个行为。
    ///     </para>
    /// </summary>
    [Fact]
    public void BuildQueryPairs_TreatsExtraValuesLiterally()
    {
        var pairs = Flatten(HitokotoQuery.BuildQueryPairs(new HitokotoQueryOptions
        {
            Categories = QuoteCategory.None,
            MaxLength = 0,
            ExtraQuery = "note=a&b&escaped=a%26b"
        }));

        // 裸 & 截断：「b」没有等号 → 丢掉。
        Assert.Equal(["encode=json", "charset=utf-8", "note=a", "escaped=a%2526b"], pairs);
    }

    /// <summary>把键值对摊成 <c>键=值</c> 的字符串，断言里比对顺序与取值都靠它。</summary>
    private static string[] Flatten(IEnumerable<KeyValuePair<string, string>> pairs) =>
        pairs.Select(pair => $"{pair.Key}={pair.Value}").ToArray();
}
