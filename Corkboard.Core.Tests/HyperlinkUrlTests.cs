using Corkboard.Core.RichText;

namespace Corkboard.Core.Tests;

/// <summary>
///     超链接地址的规范化与 &lt;a&gt; 片段拼装。
///     用户在「插入链接」浮层里能敲出各种东西（漏写协议、粘进空白、复制半截地址），
///     这里负责把它收成一个能交给浏览器、也能安全写进富文本 HTML 的绝对地址。
/// </summary>
public class HyperlinkUrlTests
{
    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("www.example.com/x", "https://www.example.com/x")]
    [InlineData("https://example.com/a?b=1", "https://example.com/a?b=1")]
    [InlineData("  https://example.com/a  ", "https://example.com/a")]
    [InlineData("HTTP://Example.COM", "http://example.com/")]
    public void TryNormalize_AcceptsAndCompletesWebAddresses(string input, string expected)
    {
        Assert.True(HyperlinkUrl.TryNormalize(input, out var url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://")]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("javascript:alert(1)")]
    public void TryNormalize_RejectsEmptyAndNonWebSchemes(string? input)
    {
        Assert.False(HyperlinkUrl.TryNormalize(input, out var url));
        Assert.Equal(string.Empty, url);
    }

    [Fact]
    public void BuildAnchorHtml_UsesTheUrlWhenTextIsEmpty()
    {
        var html = HyperlinkUrl.BuildAnchorHtml("https://example.com/", null);

        Assert.Equal("<a href=\"https://example.com/\">https://example.com/</a>", html);
    }

    [Fact]
    public void BuildAnchorHtml_EncodesUrlAndDisplayText()
    {
        var html = HyperlinkUrl.BuildAnchorHtml("https://example.com/?a=1&b=2", "<b>\"作业\"</b>");

        Assert.Equal(
            "<a href=\"https://example.com/?a=1&amp;b=2\">&lt;b&gt;&quot;作业&quot;&lt;/b&gt;</a>",
            html);
    }
}
