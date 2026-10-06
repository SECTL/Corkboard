using System.Text;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Services.Markdown;
using AvaloniaInline = Avalonia.Controls.Documents.Inline;

namespace Corkboard.Core.Tests;

/// <summary>
///     作业内容的 Markdown 渲染：解析出来的是 Avalonia 富文本 Inline 序列。
///     <para>
///         最要紧的一条是**原文里的字一个都不能丢**——用户的作业内容首先是文本，
///         其次才是样式；渲染器把 Markdown 标记吃掉、或把没配对的 <c>*</c> 吞了，都是 bug。
///     </para>
/// </summary>
public class MarkdownInlineRendererTests
{
    private static IReadOnlyList<AvaloniaInline> Render(
        string markdown,
        IReadOnlyList<BoardTextFormatRange>? formats = null,
        double baseFontSize = BoardContentStyle.DefaultFontSize,
        string? baseColor = null)
    {
        return MarkdownInlineRenderer.Render(new BoardRichText(
            markdown,
            formats ?? [],
            baseFontSize,
            baseColor is null ? null : Color.Parse(baseColor)));
    }

    /// <summary>把 Inline 序列还原成纯文本，用来断言「字没丢、顺序没乱」。</summary>
    private static string TextOf(IEnumerable<AvaloniaInline> inlines)
    {
        var builder = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    builder.Append(run.Text);
                    break;
                case LineBreak:
                    builder.Append('\n');
                    break;
                case Avalonia.Controls.Documents.Span span:
                    builder.Append(TextOf(span.Inlines));
                    break;
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<Run> Runs(IEnumerable<AvaloniaInline> inlines) => [.. inlines.OfType<Run>()];

    [Fact]
    public void Render_KeepsPlainTextIntact()
    {
        var inlines = Render("数学 练习册 P12-15");

        Assert.Equal("数学 练习册 P12-15", TextOf(inlines));
        // 没设默认字号时用内容默认字号，并且显式写在 Run 上（作业字号不跟壳的界面字号走）。
        Assert.Equal(BoardContentStyle.DefaultFontSize, Assert.Single(Runs(inlines)).FontSize);
    }

    [Fact]
    public void Render_KeepsSoftLineBreaks()
    {
        // 作业多半是多行清单，用户敲的回车必须看得见。
        Assert.Equal("第一行\n第二行", TextOf(Render("第一行\n第二行")));
    }

    [Fact]
    public void Render_TurnsEmphasisIntoBoldAndItalicAndDropsTheMarkers()
    {
        var inlines = Render("**粗** *斜*");

        Assert.Equal("粗 斜", TextOf(inlines));
        Assert.Equal(FontWeight.Bold, Runs(inlines).First().FontWeight);
        Assert.Contains(Runs(inlines), run => run.FontStyle == FontStyle.Italic);
    }

    [Fact]
    public void Render_HeadingsAreBoldAndLargerThanTheBaseFontSize()
    {
        var inlines = Render("# 标题");

        Assert.Equal("标题", TextOf(inlines));
        var run = Assert.Single(Runs(inlines));
        Assert.Equal(FontWeight.Bold, run.FontWeight);
        Assert.True(run.FontSize > BoardContentStyle.DefaultFontSize);
    }

    [Fact]
    public void Render_AppliesFormatRangeToExactlyTheCoveredCharacters()
    {
        var formats = new List<BoardTextFormatRange>
        {
            new() { Start = 2, Length = 3, Color = Color.Parse("#FFFF0000"), FontSize = 30 }
        };

        var inlines = Render("abcdef", formats);
        var runs = Runs(inlines);

        Assert.Equal("abcdef", TextOf(inlines));
        Assert.Equal(3, runs.Count);
        Assert.Equal("ab", runs[0].Text);
        // 没被标注盖住的字不能带上标注的颜色（Run 自身可能有个默认前景色，所以比对的是「不是红色」）。
        Assert.NotEqual(Color.Parse("#FFFF0000"), (runs[0].Foreground as SolidColorBrush)?.Color);
        Assert.Equal("cde", runs[1].Text);
        Assert.Equal(Color.Parse("#FFFF0000"), ((SolidColorBrush)runs[1].Foreground!).Color);
        Assert.Equal(30, runs[1].FontSize);
        Assert.Equal("f", runs[2].Text);
    }

    [Fact]
    public void Render_UsesTheConfiguredDefaultColorWhenTheRangeHasNone()
    {
        var runs = Runs(Render("数学", baseColor: "#FF112233"));

        var brush = Assert.IsType<SolidColorBrush>(Assert.Single(runs).Foreground);
        Assert.Equal(Color.Parse("#FF112233"), brush.Color);
    }

    [Fact]
    public void Render_KeepsUnmatchedMarkersAsPlainText()
    {
        // 没配对 / 不构成强调的符号必须原样留着，否则用户写的算式会被吃掉。
        Assert.Equal("3*4=12", TextOf(Render("3*4=12")));
        Assert.Equal("a_b_c", TextOf(Render("a_b_c")));
        Assert.Equal("#不是标题", TextOf(Render("#不是标题")));
    }

    [Fact]
    public void Render_ListsGetABulletPerItem()
    {
        Assert.Equal("• 数学\n• 语文", TextOf(Render("- 数学\n- 语文")));
    }

    [Fact]
    public void Render_OrderedListsKeepTheirNumbers()
    {
        Assert.Equal("1. 一\n2. 二", TextOf(Render("1. 一\n2. 二")));
    }

    [Fact]
    public void Render_TaskListsShowCheckState()
    {
        var text = TextOf(Render("- [x] 背单词\n- [ ] 抄写"));

        Assert.Contains("☑ 背单词", text);
        Assert.Contains("☐ 抄写", text);
    }

    [Fact]
    public void Render_QuoteGetsABarPrefix()
    {
        Assert.Equal("▎ 今天没有作业", TextOf(Render("> 今天没有作业")));
    }

    [Fact]
    public void Render_InlineCodeUsesMonospaceAndKeepsTheContent()
    {
        var run = Assert.Single(Runs(Render("`Corkboard`")));

        Assert.Equal("Corkboard", run.Text);
        Assert.NotNull(run.FontFamily);
    }

    [Fact]
    public void Render_LinksKeepTheLabelAndAreUnderlined()
    {
        var run = Assert.Single(Runs(Render("[数学](https://example.com)")));

        Assert.Equal("数学", run.Text);
        Assert.NotNull(run.TextDecorations);
    }

    [Fact]
    public void Render_EmptyContentProducesNothing()
    {
        Assert.Empty(Render(string.Empty));
        Assert.Empty(MarkdownInlineRenderer.Render(null));
    }
}
