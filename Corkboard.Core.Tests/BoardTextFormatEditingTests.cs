using Avalonia.Media;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Services.Board;

namespace Corkboard.Core.Tests;

/// <summary>
///     作业内容格式标注的编辑逻辑：文本变了标注怎么挪、套格式/清格式怎么裁、
///     没选中时该落到哪一段。
///     <para>
///         这里错了的表现是「只改了一个字，颜色跑到别的字上」，所以逐条钉死。
///     </para>
/// </summary>
public class BoardTextFormatEditingTests
{
    private static BoardTextFormatRange Range(int start, int length, string? color = null, double? size = null)
    {
        return new BoardTextFormatRange
        {
            Start = start,
            Length = length,
            Color = color is null ? null : Color.Parse(color),
            FontSize = size
        };
    }

    private static (int Start, int Length, Color? Color, double? Size)[] Snapshot(
        IEnumerable<BoardTextFormatRange> ranges)
    {
        return [.. ranges.Select(range => (range.Start, range.Length, range.Color, range.FontSize))];
    }

    [Fact]
    public void Shift_KeepsRangesWhenTextIsUnchanged()
    {
        var ranges = new List<BoardTextFormatRange> { Range(1, 2, "#FFFF0000") };

        var shifted = BoardTextFormatEditing.Shift(ranges, "abcd", "abcd");

        Assert.Equal(Snapshot(ranges), Snapshot(shifted));
        // 交回来的是副本：原对象是编辑表单的草稿，不能被落盘数据牵着走。
        Assert.NotSame(ranges[0], shifted[0]);
    }

    [Fact]
    public void Shift_MovesLaterRangesWhenTextIsInsertedBeforeThem()
    {
        // "abcd" 里的 "cd" 带格式，在它前面插入 "XX"，标注要跟着挪到 4 开始。
        var shifted = BoardTextFormatEditing.Shift([Range(2, 2)], "abcd", "abXXcd");

        Assert.Equal([(4, 2, (Color?)null, (double?)null)], Snapshot(shifted));
    }

    [Fact]
    public void Shift_ExtendsRangeWhenTypingInsideIt()
    {
        // 在带格式的文字中间接着打字，新字要留在格式里。
        var shifted = BoardTextFormatEditing.Shift([Range(0, 4, "#FF0000FF")], "abcd", "abXcd");

        Assert.Equal([(0, 5, Color.Parse("#FF0000FF"), (double?)null)], Snapshot(shifted));
    }

    [Fact]
    public void Shift_ExtendsRangeWhenTypingAtItsEnd()
    {
        // 在带格式的文字末尾接着打字，同样要留在格式里，不然每敲一个字就掉一次颜色。
        var shifted = BoardTextFormatEditing.Shift([Range(0, 3, "#FF0000FF")], "abc", "abcX");

        Assert.Equal([(0, 4, Color.Parse("#FF0000FF"), (double?)null)], Snapshot(shifted));
    }

    [Fact]
    public void Shift_ShrinksRangeWhenTextIsDeletedInsideIt()
    {
        var shifted = BoardTextFormatEditing.Shift([Range(0, 5, "#FF0000FF")], "abcdef", "abde");

        Assert.Equal([(0, 4, Color.Parse("#FF0000FF"), (double?)null)], Snapshot(shifted));
    }

    [Fact]
    public void Shift_DropsRangeWhoseTextWasDeletedEntirely()
    {
        // "cd" 整段被删掉，标注也没有存在的意义了。
        var shifted = BoardTextFormatEditing.Shift([Range(2, 2, "#FF0000FF")], "abcdef", "abef");

        Assert.Empty(shifted);
    }

    [Fact]
    public void Shift_CoversReplacementTextWithTheRangeItReplaced()
    {
        // 整段被替换（例如全选重打）：标注铺到新内容上，而不是缩成空壳。
        var shifted = BoardTextFormatEditing.Shift([Range(0, 3, "#FF0000FF")], "abc", "XYZW");

        Assert.Equal([(0, 4, Color.Parse("#FF0000FF"), (double?)null)], Snapshot(shifted));
    }

    [Fact]
    public void ApplyFontSize_KeepsTheColorOfTheRangeItTouches()
    {
        // 只改字号不能把颜色洗掉：这里把 [1,2) 的字号改掉，[0,1) 那段仍是原来的颜色。
        var ranges = new List<BoardTextFormatRange> { Range(0, 2, "#FFFF0000") };

        BoardTextFormatEditing.ApplyFontSize(ranges, 1, 1, 24);

        Assert.Equal(
            [(0, 1, Color.Parse("#FFFF0000"), (double?)null), (1, 1, Color.Parse("#FFFF0000"), (double?)24)],
            Snapshot(ranges));
    }

    [Fact]
    public void ApplyColor_KeepsTheFontSizeOfTheRangeItTouches()
    {
        var ranges = new List<BoardTextFormatRange> { Range(0, 3, size: 30) };

        BoardTextFormatEditing.ApplyColor(ranges, 1, 1, Color.Parse("#FF00FF00"));

        Assert.Equal(
            [(0, 1, (Color?)null, (double?)30), (1, 1, Color.Parse("#FF00FF00"), (double?)30), (2, 1, (Color?)null, (double?)30)],
            Snapshot(ranges));
    }

    [Fact]
    public void Clear_RemovesColorAndSizeFromTheTargetSpanOnly()
    {
        var ranges = new List<BoardTextFormatRange> { Range(0, 4, "#FFFF0000", 30) };

        BoardTextFormatEditing.Clear(ranges, 1, 2);

        Assert.Equal(
            [(0, 1, Color.Parse("#FFFF0000"), (double?)30), (3, 1, Color.Parse("#FFFF0000"), (double?)30)],
            Snapshot(ranges));
    }

    [Fact]
    public void ApplyFontSize_ClampsIntoTheAllowedRange()
    {
        var ranges = new List<BoardTextFormatRange>();

        BoardTextFormatEditing.ApplyFontSize(ranges, 0, 2, 9999);

        Assert.Equal(BoardContentStyle.MaxFontSize, ranges.Single().FontSize);
    }

    [Fact]
    public void Apply_IgnoresAnEmptySpan()
    {
        var ranges = new List<BoardTextFormatRange>();

        // 空行上点格式：什么都不该发生（界面上也不会给出反馈）。
        BoardTextFormatEditing.ApplyFontSize(ranges, 3, 0, 24);
        BoardTextFormatEditing.ApplyColor(ranges, 3, 0, Color.Parse("#FFFF0000"));

        Assert.Empty(ranges);
    }

    [Fact]
    public void ResolveTargetSpan_UsesTheSelectionWhenThereIsOne()
    {
        var (start, length) = BoardTextFormatEditing.ResolveTargetSpan("第一行\n第二行", 4, 7);

        Assert.Equal(4, start);
        Assert.Equal(3, length);
    }

    [Fact]
    public void ResolveTargetSpan_FallsBackToTheLineAtTheCaret()
    {
        // 没选中就改「光标所在的那一段」——用户心里的「这一段」就是这一行。
        var (start, length) = BoardTextFormatEditing.ResolveTargetSpan("第一行\n第二行", 5, 5);

        Assert.Equal(4, start);
        Assert.Equal(3, length);
    }

    [Fact]
    public void ResolveTargetSpan_ReturnsAnEmptySpanOnAnEmptyLine()
    {
        var (_, length) = BoardTextFormatEditing.ResolveTargetSpan("第一行\n\n第三行", 4, 4);

        Assert.Equal(0, length);
    }

    [Fact]
    public void Normalize_MergesNeighboursWithTheSameStyleAndDropsEmptyOnes()
    {
        var ranges = new List<BoardTextFormatRange>
        {
            Range(2, 2, "#FFFF0000"),
            Range(0, 2, "#FFFF0000"),
            Range(8, 0, "#FF00FF00"),   // 长度为 0：丢掉
            Range(9, 1),                // 颜色字号都没有：丢掉
            Range(6, 2, size: 20)
        };

        BoardTextFormatEditing.Normalize(ranges);

        // 相邻且同样式的并成一条；空标注与「颜色字号都没有」的标注直接丢掉。
        Assert.Equal(
            [(0, 4, Color.Parse("#FFFF0000"), (double?)null), (6, 2, (Color?)null, (double?)20)],
            Snapshot(ranges));
    }

    [Fact]
    public void FromNote_TrimsTheContentAndKeepsTheFormatsAligned()
    {
        var note = new BoardNote
        {
            Content = "  你好  ",
            Formats = [Range(2, 2, "#FFFF0000")],
            ContentFontSize = 22
        };

        var document = BoardRichText.FromNote(note, BoardContentStyle.DefaultFontSize, null);

        Assert.Equal("你好", document.Text);
        // 标注跟着同一次裁剪挪到 0，不然颜色会落到别的字上。
        Assert.Equal([(0, 2, Color.Parse("#FFFF0000"), (double?)null)], Snapshot(document.Formats));
        Assert.Equal(22, document.BaseFontSize);
    }

    [Fact]
    public void FromNote_FallsBackToTheConfiguredDefaultFontSize()
    {
        var document = BoardRichText.FromNote(new BoardNote { Content = "数学" }, 18, Color.Parse("#FF123456"));

        Assert.Equal(18, document.BaseFontSize);
        Assert.Equal(Color.Parse("#FF123456"), document.BaseColor);
    }

    [Fact]
    public void ResolveEffectiveStyle_TakesTheLastRangeCoveringTheIndex()
    {
        // 界面靠它把浮窗上的字号显示成「选段当前的值」，所以要跟渲染一致：靠后的那条生效。
        var ranges = new List<BoardTextFormatRange>
        {
            Range(0, 4, "#FFFF0000", 14),
            Range(2, 2, "#FF00FF00", 20)
        };

        Assert.Equal((Color.Parse("#FFFF0000"), (double?)14), BoardTextFormatEditing.ResolveEffectiveStyle(ranges, 1));
        Assert.Equal((Color.Parse("#FF00FF00"), (double?)20), BoardTextFormatEditing.ResolveEffectiveStyle(ranges, 2));
        Assert.Equal((Color.Parse("#FF00FF00"), (double?)20), BoardTextFormatEditing.ResolveEffectiveStyle(ranges, 3));

        // 标注是按 [start, end) 盖住的：end 那个位置不归它，4 之外没有任何标注。
        Assert.Equal(((Color?)null, (double?)null), BoardTextFormatEditing.ResolveEffectiveStyle(ranges, 4));
        Assert.Equal(((Color?)null, (double?)null), BoardTextFormatEditing.ResolveEffectiveStyle(ranges, 99));
    }
}
