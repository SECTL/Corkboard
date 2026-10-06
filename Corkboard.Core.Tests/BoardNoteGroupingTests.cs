using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Services.Board;

namespace Corkboard.Core.Tests;

/// <summary>同一科目的作业必须合并成一个区块，区块顺序由排序方式决定。</summary>
public class BoardNoteGroupingTests
{
    private static BoardNote Note(string subject, string content, int createdOffsetMinutes = 0)
    {
        return new BoardNote
        {
            Subject = subject,
            Content = content,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                .AddMinutes(createdOffsetMinutes)
        };
    }

    [Fact]
    public void Group_MergesNotesOfTheSameSubjectIntoSingleGroup()
    {
        var notes = new[] { Note("数学", "一"), Note("语文", "二"), Note("数学", "三") };

        var groups = BoardNoteGrouping.Group(notes, BoardSortMode.CreatedDescending);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups.Single(group => group.Key == "数学").Count());
        Assert.Single(groups.Single(group => group.Key == "语文"));
    }

    [Fact]
    public void Group_KeepsOrderInsideGroupAccordingToSortMode()
    {
        var notes = new[] { Note("数学", "旧", 0), Note("数学", "新", 10) };

        var ascending = BoardNoteGrouping.Group(notes, BoardSortMode.CreatedAscending).Single();
        var descending = BoardNoteGrouping.Group(notes, BoardSortMode.CreatedDescending).Single();

        Assert.Equal(["旧", "新"], ascending.Select(note => note.Content));
        Assert.Equal(["新", "旧"], descending.Select(note => note.Content));
    }

    [Fact]
    public void Group_TrimsSubjectAndLumpsBlankSubjectTogether()
    {
        var notes = new[] { Note(" 数学 ", "一"), Note("数学", "二"), Note("   ", "三") };

        var groups = BoardNoteGrouping.Group(notes, BoardSortMode.CreatedDescending);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups.Single(group => group.Key == "数学").Count());
        Assert.Single(groups.Single(group => group.Key.Length == 0));
    }

    [Fact]
    public void Group_OrdersGroupsBySubjectWhenSortModeIsSubject()
    {
        var notes = new[] { Note("语文", "一"), Note("数学", "二") };

        var groups = BoardNoteGrouping.Group(notes, BoardSortMode.Subject);

        Assert.Equal(["数学", "语文"], groups.Select(group => group.Key));
    }

    [Fact]
    public void Group_ManualModeOrdersGroupsByStoredSubjectOrder()
    {
        var notes = new[] { Note("语文", "一"), Note("数学", "二"), Note("英语", "三") };

        var groups = BoardNoteGrouping.Group(notes, BoardSortMode.Manual, ["英语", "语文", "数学"]);

        Assert.Equal(["英语", "语文", "数学"], groups.Select(group => group.Key));
    }

    [Fact]
    public void Group_ManualModePutsSubjectsMissingFromOrderAtTheEnd()
    {
        var notes = new[] { Note("语文", "一"), Note("数学", "二"), Note("体育", "三") };

        // 「体育」没记进顺序表：整块落到后面，而且不能因此丢作业。
        var groups = BoardNoteGrouping.Group(notes, BoardSortMode.Manual, ["数学", "语文"]);

        Assert.Equal(["数学", "语文", "体育"], groups.Select(group => group.Key));
        Assert.Equal(3, groups.Sum(group => group.Count()));
    }

    [Fact]
    public void Group_ManualModeKeepsInsertionOrderInsideGroup()
    {
        var older = Note("数学", "先加的");
        var newer = Note("数学", "后加的", 10);
        older.Order = 0;
        newer.Order = 1;

        var groups = BoardNoteGrouping.Group([newer, older], BoardSortMode.Manual, ["数学"]);

        // 手动模式只管区块先后，区块内仍按加入顺序，不会因为创建时间新就窜到前面。
        Assert.Equal(["先加的", "后加的"], groups.Single().Select(note => note.Content));
    }

    [Fact]
    public void Group_IgnoresSubjectOrderOutsideManualMode()
    {
        var notes = new[] { Note("语文", "一"), Note("数学", "二") };

        var groups = BoardNoteGrouping.Group(notes, BoardSortMode.CreatedDescending, ["数学", "语文"]);

        // 非手动模式下顺序表不参与，否则设置页选「按科目」还会被手动顺序牵着走。
        Assert.Equal(["语文", "数学"], groups.Select(group => group.Key));
    }
}
