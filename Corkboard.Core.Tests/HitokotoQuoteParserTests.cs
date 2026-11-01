using Corkboard.Core.Widgets.Quote;

namespace Corkboard.Core.Tests;

/// <summary>
///     一言响应 JSON 的读法：哪些字段是硬要求、哪些读不出来就兜底，以及「什么样的脏输入都不许抛异常」。
///     <para>
///     钉住这条契约的原因：这个方法的输入来自网络，中途可能经过代理、可能被截断、可能是一页
///     500 错误的 HTML，而它跑在界面线程拉取结果的那一步上 —— 抛一次异常就是一整个小组件崩掉。
///     所以 <c>hitokoto</c> 缺失或空白一律 <see langword="false" /> + <c>null</c>，其余字段全部有兜底；
///     类别认不出来记「其他」、字数算不出来用正文字数，绝不给出半条「有文字但没有长度」的句子。
///     </para>
/// </summary>
public class HitokotoQuoteParserTests
{
    /// <summary>官方响应里我们关心的六个字段全都搬进来：正文、出处、作者、类别、uuid、字数。</summary>
    [Fact]
    public void TryParse_ReadsEveryFieldWeKeep()
    {
        const string json = """
                             {
                               "id": 7624,
                               "uuid": "d5f1e9a0-1c1f-4f5f-8f0f-2b6f4b1d2c11",
                               "hitokoto": "人生如逆旅，我亦是行人。",
                               "type": "d",
                               "from": "临江仙·送钱穆父",
                               "from_who": "苏轼",
                               "creator": "hitokoto",
                               "creator_uid": 1,
                               "reviewer": 0,
                               "commit_from": "web",
                               "created_at": "1468605909",
                               "length": 12
                             }
                             """;

        Assert.True(HitokotoQuoteParser.TryParse(json, out var quote));

        Assert.NotNull(quote);
        Assert.Equal("人生如逆旅，我亦是行人。", quote!.Text);
        Assert.Equal("临江仙·送钱穆父", quote.Source);
        Assert.Equal("苏轼", quote.Author);
        Assert.Equal(QuoteCategory.Literature, quote.Category);
        Assert.Equal("d5f1e9a0-1c1f-4f5f-8f0f-2b6f4b1d2c11", quote.Uuid);
        Assert.Equal(12, quote.Length);
    }

    /// <summary>
    ///     正文是唯一的硬要求：缺失、空串、纯空白、以及「给了个数字」都算没拿到，
    ///     返回 <see langword="false" /> 且 <c>quote</c> 为 <see langword="null" />
    ///     （调用方不会拿到一条「空句子」当真的显示出去）。
    /// </summary>
    [Theory]
    [InlineData("""{"uuid":"x","type":"a"}""")]
    [InlineData("""{"hitokoto":""}""")]
    [InlineData("""{"hitokoto":"   "}""")]
    [InlineData("""{"hitokoto":"\t\n "}""")]
    [InlineData("""{"hitokoto":123}""")]
    [InlineData("""{"hitokoto":null}""")]
    [InlineData("{}")]
    public void TryParse_RequiresNonBlankHitokoto(string json)
    {
        Assert.False(HitokotoQuoteParser.TryParse(json, out var quote));
        Assert.Null(quote);
    }

    /// <summary>
    ///     不合法的 JSON、顶层不是对象、以及空输入：一律安静地返回 <see langword="false" />。
    ///     数组那一条尤其重要 —— 有人把 <c>encode=js</c> 改回去之后拿到的是 JSONP 或数组，不能崩。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("""{"hitokoto":"没写完""")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("[]")]
    [InlineData("""[{"hitokoto":"批量查询"}]""")]
    [InlineData("""  "一串裸字符串"  """)]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    public void TryParse_RejectsAnythingThatIsNotAJsonObject(string? json)
    {
        Assert.False(HitokotoQuoteParser.TryParse(json, out var quote));
        Assert.Null(quote);
    }

    /// <summary>响应里的 <c>type</c> 是单个小写字母，a→l 都要翻对（含已停用的 j —— 老句子仍是这个类别）。</summary>
    [Theory]
    [InlineData("a", QuoteCategory.Animation)]
    [InlineData("b", QuoteCategory.Comic)]
    [InlineData("c", QuoteCategory.Game)]
    [InlineData("d", QuoteCategory.Literature)]
    [InlineData("e", QuoteCategory.Original)]
    [InlineData("f", QuoteCategory.Web)]
    [InlineData("g", QuoteCategory.Other)]
    [InlineData("h", QuoteCategory.Movie)]
    [InlineData("i", QuoteCategory.Poetry)]
    [InlineData("j", QuoteCategory.NeteaseMusic)]
    [InlineData("k", QuoteCategory.Philosophy)]
    [InlineData("l", QuoteCategory.Funny)]
    public void TryParse_MapsTheTypeLetter(string type, QuoteCategory expected)
    {
        var quote = Parse($$"""{"hitokoto":"一句话","type":"{{type}}"}""");

        Assert.Equal(expected, quote.Category);
    }

    /// <summary>大小写不敏感：中间隔了层代理、或者上游把 type 写成大写时不该整条变成「其他」。</summary>
    [Theory]
    [InlineData("A")]
    [InlineData("K")]
    public void TryParse_AcceptsUppercaseType(string type)
    {
        var quote = Parse($$"""{"hitokoto":"一句话","type":"{{type}}"}""");

        Assert.NotEqual(QuoteCategory.Other, quote.Category);
    }

    /// <summary>
    ///     认不出来的 <c>type</c>（未知字母、多字符、空串、根本不是字符串）一律记「其他」。
    ///     这是官方对非法 <c>c</c> 的兜底策略的同款：宁可归到「其他」，也不让类别变成「无」。
    /// </summary>
    [Theory]
    [InlineData("""{"hitokoto":"一句话","type":"z"}""")]
    [InlineData("""{"hitokoto":"一句话","type":"ab"}""")]
    [InlineData("""{"hitokoto":"一句话","type":""}""")]
    [InlineData("""{"hitokoto":"一句话","type":7}""")]
    [InlineData("""{"hitokoto":"一句话","type":null}""")]
    [InlineData("""{"hitokoto":"一句话"}""")]
    public void TryParse_FallsBackToOtherForUnreadableType(string json)
    {
        Assert.Equal(QuoteCategory.Other, Parse(json).Category);
    }

    /// <summary>
    ///     <c>from</c> / <c>from_who</c> 缺失、<c>null</c>、空串、纯空白都按「没有」处理（<see langword="null" />）：
    ///     界面据此隐藏那一行，空串则会留下一个空白行。
    /// </summary>
    [Theory]
    [InlineData("""{"hitokoto":"一句话"}""")]
    [InlineData("""{"hitokoto":"一句话","from":null,"from_who":null}""")]
    [InlineData("""{"hitokoto":"一句话","from":"","from_who":""}""")]
    [InlineData("""{"hitokoto":"一句话","from":"  ","from_who":"\t"}""")]
    [InlineData("""{"hitokoto":"一句话","from":1,"from_who":2}""")]
    public void TryParse_TreatsMissingSourceAndAuthorAsNull(string json)
    {
        var quote = Parse(json);

        Assert.Null(quote.Source);
        Assert.Null(quote.Author);
    }

    /// <summary>有出处时有值，并且首尾空白被去掉（服务端偶尔在 <c>from_who</c> 里留个尾空格）。</summary>
    [Fact]
    public void TryParse_TrimsSurroundingWhitespaceInSourceAndAuthor()
    {
        var quote = Parse("""{"hitokoto":"一句话","from":" 红楼梦 ","from_who":" 曹雪芹  "}""");

        Assert.Equal("红楼梦", quote.Source);
        Assert.Equal("曹雪芹", quote.Author);
    }

    /// <summary>正文原样保留，一个字符都不动 —— 引文里的标点与空白属于原文，去空白等于改字。</summary>
    [Fact]
    public void TryParse_KeepsTheQuoteTextVerbatim()
    {
        var quote = Parse("""{"hitokoto":"「 你好，世界 」"}""");

        Assert.Equal("「 你好，世界 」", quote.Text);
    }

    /// <summary>服务端给了字数就用它（与正文字数可能不同：它算的是字符数，我们这边是 UTF-16 长度）。</summary>
    [Fact]
    public void TryParse_UsesTheServerLengthWhenPresent()
    {
        var quote = Parse("""{"hitokoto":"四个字","length":42}""");

        Assert.Equal(42, quote.Length);
    }

    /// <summary>
    ///     <c>length</c> 缺失、是字符串、是小数、是 0 或负数时都用正文字数兜底：
    ///     界面上那句「共 N 字」不该出现 0 字，也不该因为多了个引号就把整条响应判死。
    /// </summary>
    [Theory]
    [InlineData("""{"hitokoto":"一二三四五" }""")]
    [InlineData("""{"hitokoto":"一二三四五","length":"5"}""")]
    [InlineData("""{"hitokoto":"一二三四五","length":5.5}""")]
    [InlineData("""{"hitokoto":"一二三四五","length":0}""")]
    [InlineData("""{"hitokoto":"一二三四五","length":-3}""")]
    [InlineData("""{"hitokoto":"一二三四五","length":null}""")]
    public void TryParse_FallsBackToTextLengthWhenLengthIsUnusable(string json)
    {
        var quote = Parse(json);

        Assert.Equal(5, quote.Length);
    }

    /// <summary>运营字段（creator / reviewer / commit_from / created_at …）不进模型，多了也不影响读取。</summary>
    [Fact]
    public void TryParse_IgnoresUnknownAndOperationalFields()
    {
        var quote = Parse("""
                          {"hitokoto":"一句话","type":"e","future_field":{"nested":[1,2,3]},
                           "creator":"某人","reviewer":0,"commit_from":"api","created_at":"1468605909"}
                          """);

        Assert.Equal("一句话", quote.Text);
        Assert.Equal(QuoteCategory.Original, quote.Category);
        Assert.Null(quote.Source);
    }

    /// <summary>没拿到句子时界面绑的是 <see cref="HitokotoQuote.Empty" />：一条什么都空、类别也是「无」的记录。</summary>
    [Fact]
    public void Empty_IsAnEmptyQuoteWithNoCategory()
    {
        Assert.Equal(string.Empty, HitokotoQuote.Empty.Text);
        Assert.Null(HitokotoQuote.Empty.Source);
        Assert.Null(HitokotoQuote.Empty.Author);
        Assert.Equal(QuoteCategory.None, HitokotoQuote.Empty.Category);
        Assert.Null(HitokotoQuote.Empty.Uuid);
        Assert.Equal(0, HitokotoQuote.Empty.Length);
    }

    /// <summary>读得出来是前提：这几个用例只关心读出来的内容，读不出来就让断言直接失败（而不是抛空引用）。</summary>
    private static HitokotoQuote Parse(string json)
    {
        Assert.True(HitokotoQuoteParser.TryParse(json, out var quote));
        Assert.NotNull(quote);
        return quote!;
    }
}
