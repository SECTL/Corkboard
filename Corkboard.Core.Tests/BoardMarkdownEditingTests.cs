using Corkboard.Core.Services.Board;

namespace Corkboard.Core.Tests;

/// <summary>
///     给选段套 Markdown 记号：加粗 / 斜体夹住选段，再点一次去掉。
///     用例里的选区都写成 <c>[start, end)</c>，断言同时看新文本与「该重新选中哪一段」。
/// </summary>
public class BoardMarkdownEditingTests
{
    [Fact]
    public void Toggle_BoldWrapsSelectionAndKeepsContentSelected()
    {
        var result = BoardMarkdownEditing.Toggle("作业内容", 0, 2, BoardMarkdownFormat.Bold);

        Assert.Equal("**作业**内容", result.Text);
        Assert.Equal(2, result.SelectionStart);
        Assert.Equal(2, result.SelectionLength);
    }

    [Fact]
    public void Toggle_BoldAgainRemovesTheMarkers()
    {
        var result = BoardMarkdownEditing.Toggle("**作业**内容", 2, 4, BoardMarkdownFormat.Bold);

        Assert.Equal("作业内容", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(2, result.SelectionLength);
    }

    [Fact]
    public void Toggle_BoldOnSelectionThatIncludesMarkersRemovesThem()
    {
        var result = BoardMarkdownEditing.Toggle("**作业**", 0, 6, BoardMarkdownFormat.Bold);

        Assert.Equal("作业", result.Text);
        Assert.Equal(0, result.SelectionStart);
        Assert.Equal(2, result.SelectionLength);
    }

    [Fact]
    public void Toggle_ItalicUsesItsOwnMarker()
    {
        var result = BoardMarkdownEditing.Toggle("abc", 0, 1, BoardMarkdownFormat.Italic);

        Assert.Equal("*a*bc", result.Text);
        Assert.Equal(1, result.SelectionStart);
        Assert.Equal(1, result.SelectionLength);
    }

    [Fact]
    public void Toggle_BoldWithCaretInsertsEmptyPairAndKeepsCaretInside()
    {
        // 光标（空选区）上加粗：插一对空记号，光标落在中间，接着打字就是加粗内容。
        var result = BoardMarkdownEditing.Toggle("ab", 1, 1, BoardMarkdownFormat.Bold);

        Assert.Equal("a****b", result.Text);
        Assert.Equal(3, result.SelectionStart);
        Assert.Equal(0, result.SelectionLength);
    }

    [Fact]
    public void Toggle_ClampsSelectionOutsideText()
    {
        var result = BoardMarkdownEditing.Toggle("ab", 5, 9, BoardMarkdownFormat.Bold);

        Assert.Equal("ab****", result.Text);
        Assert.Equal(4, result.SelectionStart);
        Assert.Equal(0, result.SelectionLength);
    }

    [Fact]
    public void Toggle_SelectionOrderDoesNotMatter()
    {
        // 从后往前拖出来的选区（start > end）也要当成同一段。
        var result = BoardMarkdownEditing.Toggle("作业内容", 2, 0, BoardMarkdownFormat.Bold);

        Assert.Equal("**作业**内容", result.Text);
        Assert.Equal(2, result.SelectionStart);
        Assert.Equal(2, result.SelectionLength);
    }
}
