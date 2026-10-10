using Corkboard.Core.Services.Board;
using HtmlAgilityPack;

namespace Corkboard.Core.Tests;

/// <summary>
///     内容 HTML 的「默认样式」补摘：字号 / 颜色补在行内 span 上、回写时按声明摘掉，
///     外加空块的<b>颜色种子</b>（零宽空格）—— 它是给输入法预编辑串继承颜色用的，
///     必须永远不进落盘内容。
/// </summary>
public class BoardHtmlStylingTests
{
    private const string FontSizeCss = "font-size:10.5pt";
    private const string ColorCss = "color:#FFFFFF";

    [Fact]
    public void Apply_WrapsTextNodesWithDefaultStyle()
    {
        var html = BoardHtmlStyling.Apply("<p>你好</p>", FontSizeCss, ColorCss);

        Assert.Contains(FontSizeCss, html);
        Assert.Contains(ColorCss, html);
        Assert.Contains(BoardHtmlStyling.MarkerAttribute, html);
        // HtmlAgilityPack 落盘时会把非 ASCII 转成数字实体，比较前先解实体。
        Assert.Contains("你好", HtmlEntity.DeEntitize(html));
    }

    [Fact]
    public void Apply_KeepsUserDeclaredStyle()
    {
        var html = BoardHtmlStyling.Apply(
            "<p><span style=\"color:#FF0000\">红</span></p>",
            FontSizeCss,
            ColorCss);

        Assert.Contains("color:#FF0000", html);
        // 用户自己声明过 color，我们只补字号那一层，不能再盖一层颜色。
        Assert.Equal(0, CountOccurrences(html, ColorCss));
        Assert.Equal(1, CountOccurrences(html, FontSizeCss));
    }

    [Fact]
    public void Apply_SeedsEmptyParagraphAndCell()
    {
        var html = BoardHtmlStyling.Apply(
            "<p></p><table><tbody><tr><td></td></tr></tbody></table>",
            FontSizeCss,
            ColorCss);
        var decoded = HtmlEntity.DeEntitize(html);

        // 空段落与空单元格各长出一个带默认色的零宽 run（否则库那边预编辑串会是写死的黑色）。
        Assert.Equal(2, CountOccurrences(decoded, BoardHtmlStyling.SeedChar));
        Assert.Equal(2, CountOccurrences(html, ColorCss));
    }

    [Fact]
    public void Apply_SeedsBrOnlyParagraph()
    {
        var html = BoardHtmlStyling.Apply("<p><br></p>", FontSizeCss, ColorCss);

        Assert.Equal(1, CountOccurrences(HtmlEntity.DeEntitize(html), BoardHtmlStyling.SeedChar));
    }

    [Fact]
    public void Apply_DoesNotSeedParagraphWithText()
    {
        var html = BoardHtmlStyling.Apply("<p>有字</p>", FontSizeCss, ColorCss);

        // 注意别用 Assert.DoesNotContain：它按区域性比较，零宽字符没有权重、会「出现在任何位置」。
        Assert.Equal(0, CountOccurrences(HtmlEntity.DeEntitize(html), BoardHtmlStyling.SeedChar));
    }

    [Fact]
    public void Strip_RemovesOnlyOurDeclarations()
    {
        var styled = BoardHtmlStyling.Apply("<p>你好</p>", FontSizeCss, ColorCss);

        var stripped = BoardHtmlStyling.Strip(styled, FontSizeCss, ColorCss);

        Assert.DoesNotContain(FontSizeCss, stripped);
        Assert.DoesNotContain(ColorCss, stripped);
        Assert.Contains("你好", HtmlEntity.DeEntitize(stripped));
    }

    [Fact]
    public void StripSeeds_RemovesLiteralAndEntityForm()
    {
        var literal = "<p>" + BoardHtmlStyling.SeedChar + "你好</p>";
        var entity = "<p>&#8203;你好</p>";

        Assert.Equal("<p>你好</p>", HtmlEntity.DeEntitize(BoardHtmlStyling.StripSeeds(literal)));
        Assert.Equal("<p>你好</p>", HtmlEntity.DeEntitize(BoardHtmlStyling.StripSeeds(entity)));
        Assert.Equal(string.Empty, BoardHtmlStyling.StripSeeds(null));
    }

    [Fact]
    public void EmptyDocument_RoundTripsToNothing()
    {
        // 空内容装填时会垫一个种子，回写时要还原成空串：不然「空段落」会跟着内容一起落盘。
        var seeded = BoardHtmlStyling.Apply(
            "<p>" + BoardHtmlStyling.SeedChar + "</p>",
            FontSizeCss,
            ColorCss);

        var published = BoardHtmlStyling.StripSeeds(BoardHtmlStyling.Strip(seeded, FontSizeCss, ColorCss));

        // 落盘判空走 HasVisibleText（吃 HTML 串），运行期判光标处空不空才用 HasVisibleContent。
        Assert.False(BoardHtmlStyling.HasVisibleText(published));
        Assert.Equal(0, CountOccurrences(HtmlEntity.DeEntitize(published), BoardHtmlStyling.SeedChar));
    }

    [Fact]
    public void Strip_KeepsImageOnlyContent()
    {
        // 内容只有一张图片时纯文本是空的；早先按纯文本判空会把整篇清成空串（图片存不住）。
        var image = "<p><img src=\"data:image/png;base64,AAA\" /></p>";
        var styled = BoardHtmlStyling.Apply(image, FontSizeCss, ColorCss);

        var published = BoardHtmlStyling.StripSeeds(BoardHtmlStyling.Strip(styled, FontSizeCss, ColorCss));

        Assert.True(BoardHtmlStyling.HasMeaningfulContent(published));
        Assert.Contains("<img", published);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("<p></p>", false)]
    [InlineData("<p><br></p>", false)]
    [InlineData("<p>&#8203;</p>", false)]
    [InlineData("<p>字</p>", true)]
    [InlineData("<p><img src=\"data:image/png;base64,AAA\" /></p>", true)]
    [InlineData("<table><tbody><tr><td></td></tr></tbody></table>", true)]
    [InlineData("<p><hr /></p>", true)]
    public void HasMeaningfulContent_CountsImagesAndTables(string? html, bool expected)
    {
        Assert.Equal(expected, BoardHtmlStyling.HasMeaningfulContent(html));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("<p></p>", false)]
    [InlineData("<p><br></p>", false)]
    [InlineData("<p>&#8203;</p>", false)]
    [InlineData("<p>字</p>", true)]
    public void HasVisibleText_TellsEmptyFromContent(string? html, bool expected)
    {
        Assert.Equal(expected, BoardHtmlStyling.HasVisibleText(html));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("\u200B", false)]
    [InlineData("\u200B ", false)]
    [InlineData("\u200B字", true)]
    public void HasVisibleContent_IgnoresSeedAndWhitespace(string? text, bool expected)
    {
        Assert.Equal(expected, BoardHtmlStyling.HasVisibleContent(text));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
