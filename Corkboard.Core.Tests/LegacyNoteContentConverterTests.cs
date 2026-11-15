using Corkboard.Core.Notes;
using Corkboard.Core.Notes.Legacy;
using Corkboard.Core.RichText;

namespace Corkboard.Core.Tests;

/// <summary>
///     旧内容迁移只搬「Markdown 正文」，手写作业不能碰：转换会走 <see cref="Note.SetHtmlContent" />，
///     那会把手写笔迹清掉。存储层虽然拦得住「空内容覆盖笔迹」，但文件会被原样重写一遍，
///     下次加载又转一次——文件监视打开后就成了「重写 → 监视 → 重读 → 重写」的自激循环。
/// </summary>
public class LegacyNoteContentConverterTests
{
    [Fact]
    public void Convert_MarkdownNote_BecomesHtml()
    {
        var note = new Note { Content = "**粗**" };

        Assert.True(LegacyNoteContentConverter.Convert(note));
        Assert.Equal(RichTextContentKind.Html, note.ContentKind);
        Assert.Contains("粗", note.Content);
    }

    [Fact]
    public void Convert_HandwritingNote_KeepsTheInk()
    {
        var note = new Note();
        note.SetHandwriting("ink-data");

        Assert.False(LegacyNoteContentConverter.Convert(note));
        Assert.Equal(RichTextContentKind.Handwriting, note.ContentKind);
        Assert.Equal("ink-data", note.Handwriting);
        Assert.Equal(string.Empty, note.Content);
    }

    [Fact]
    public void Convert_MarkdownNoteWithInk_KeepsTheInk()
    {
        // 旧数据里可能出现「正文还在、笔迹也在」的一条：笔迹比正文难恢复，宁可先不转换。
        var note = new Note { Content = "旧文本", Handwriting = "ink-data" };

        Assert.False(LegacyNoteContentConverter.Convert(note));
        Assert.Equal("ink-data", note.Handwriting);
        Assert.Equal("旧文本", note.Content);
    }

    [Fact]
    public void Convert_HtmlNote_IsLeftAlone()
    {
        var note = new Note();
        note.SetHtmlContent("<p>新内容</p>");
        var content = note.Content;

        Assert.False(LegacyNoteContentConverter.Convert(note));
        Assert.Equal(RichTextContentKind.Html, note.ContentKind);
        Assert.Equal(content, note.Content);
    }
}