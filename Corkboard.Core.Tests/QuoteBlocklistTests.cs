using Corkboard.Core.Widgets.Quote;

namespace Corkboard.Core.Tests;

/// <summary>
///     一言的屏蔽词：怎么把那一段「每行一个词」的文本拆成词表，以及一条句子算不算命中。
///     <para>
///     钉住这条契约的原因：官方 v1 没有「排除关键词」的参数，屏蔽只能在拿到句子之后自己判，
///     判错了的下场是「用户明明屏蔽了某个词，屏幕上还是出现了它」或者反过来「一句话都显示不出来」。
///     所以这里把换行形态（<c>\n</c> / <c>\r\n</c>）、大小写、三处字段（正文 / 出处 / 作者）
///     与条数上限全部写死。
///     </para>
/// </summary>
public class QuoteBlocklistTests
{
    private static HitokotoQuote Quote(string text, string? source = null, string? author = null) =>
        new(text, source, author, QuoteCategory.Other, null, text.Length);

    /// <summary>一行一个词：去空白、去空行，顺序保持用户写的顺序。</summary>
    [Fact]
    public void Parse_SplitsLinesAndTrims()
    {
        var words = QuoteBlocklist.Parse("  广告 \r\n\r\n 灌水\n\n");

        Assert.Equal(["广告", "灌水"], words);
    }

    /// <summary>空文本与纯空白都不算「屏蔽了东西」——别把 null 当成一个词。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\r\n")]
    public void Parse_EmptyInputGivesEmptyList(string? raw)
    {
        Assert.Empty(QuoteBlocklist.Parse(raw));
    }

    /// <summary>同一个词写两遍只留一份，且大小写不同也算重复（匹配本来就不区分大小写）。</summary>
    [Fact]
    public void Parse_DeduplicatesIgnoringCase()
    {
        var words = QuoteBlocklist.Parse("spoiler\nSpoiler\n剧透");

        Assert.Equal(["spoiler", "剧透"], words);
    }

    /// <summary>条数有上限：超出的部分直接丢，不能让一个巨大的词表把每次判定的开销拉爆。</summary>
    [Fact]
    public void Parse_StopsAtMaxWords()
    {
        var raw = string.Join('\n', Enumerable.Range(0, QuoteBlocklist.MaxWords + 50).Select(i => $"word{i}"));

        Assert.Equal(QuoteBlocklist.MaxWords, QuoteBlocklist.Parse(raw).Count);
    }

    /// <summary><see cref="QuoteBlocklist.Normalize" /> 是落盘形态：一行一个词、没有空行、没有重复。</summary>
    [Fact]
    public void Normalize_ProducesOneWordPerLine()
    {
        Assert.Equal("广告\n剧透", QuoteBlocklist.Normalize("广告\r\n\r\n剧透\n广告\n"));
    }

    /// <summary>正文命中就整句不要。</summary>
    [Fact]
    public void IsBlocked_MatchesText()
    {
        Assert.True(QuoteBlocklist.IsBlocked(Quote("这是一条广告"), "广告"));
        Assert.False(QuoteBlocklist.IsBlocked(Quote("这是一条好句子"), "广告"));
    }

    /// <summary>出处与作者也一起判：用户屏蔽的是词，不是「只屏蔽正文里的词」。</summary>
    [Fact]
    public void IsBlocked_MatchesSourceAndAuthor()
    {
        Assert.True(QuoteBlocklist.IsBlocked(Quote("正文", source: "某广告位"), "广告"));
        Assert.True(QuoteBlocklist.IsBlocked(Quote("正文", author: "广告君"), "广告"));
    }

    /// <summary>英文词不区分大小写，否则屏蔽 Spoiler 时小写的 spoiler 会漏过去。</summary>
    [Fact]
    public void IsBlocked_IgnoresCase()
    {
        Assert.True(QuoteBlocklist.IsBlocked(Quote("Big SPOILER ahead"), "spoiler"));
    }

    /// <summary>没有屏蔽词时任何句子都放行（这是默认配置的状态）。</summary>
    [Fact]
    public void IsBlocked_EmptyWordListAllowsEverything()
    {
        Assert.False(QuoteBlocklist.IsBlocked(Quote("随便什么"), Array.Empty<string>()));
        Assert.False(QuoteBlocklist.IsBlocked(Quote("随便什么"), ""));
    }

    /// <summary>出处 / 作者缺失（<see langword="null" />）时不能因为「拿 null 去 Contains」炸掉。</summary>
    [Fact]
    public void IsBlocked_NullFieldsAreSafe()
    {
        Assert.False(QuoteBlocklist.IsBlocked(Quote("正文"), "广告"));
    }
}
