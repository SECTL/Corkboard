using Corkboard.Core.Board.History;
using Corkboard.Core.Notes;
using Corkboard.Core.RichText;

namespace Corkboard.Core.Tests;

/// <summary>
///     历史重放（回放的数据源）：停靠点由「每一次真实变更」生成，停在某一秒时看到的是那一刻的板面。
///     这些用例专门盯住旧实现看不懂的情况：之后被删掉、被改过的作业，在它还在的那一刻照样要画出来。
/// </summary>
public class BoardHistoryTests
{
    private static DateTimeOffset At(int year, int month, int day, int hour, int minute, int second)
        => new(year, month, day, hour, minute, second, TimeSpan.FromHours(8));

    private static Note MakeNote(string content, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        Content = content,
        ContentKind = RichTextContentKind.Html,
        Subject = "数学",
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };

    /// <summary>把快照按哈希放进字典：<see cref="BoardHistory" /> 只通过委托取内容，测试不必落盘。</summary>
    private sealed class SnapshotBook
    {
        public Dictionary<string, Note> Notes { get; } = new(StringComparer.Ordinal);

        public string Add(Note note)
        {
            var hash = Notes.Count.ToString("x64");
            Notes[hash] = note;
            return hash;
        }

        public Note? Load(string hash) => Notes.GetValueOrDefault(hash);
    }

    [Fact]
    public void BuildStops_CountsWhatIsVisibleAtEachMoment()
    {
        var book = new SnapshotBook();
        var first = MakeNote("第一条", At(2026, 3, 4, 8, 30, 0));
        var second = MakeNote("第二条", At(2026, 3, 4, 9, 0, 0));

        var entries = new List<NoteHistoryEntry>
        {
            new(first.CreatedAt, first.Id, NoteHistoryAction.Create, book.Add(first)),
            new(second.CreatedAt, second.Id, NoteHistoryAction.Create, book.Add(second))
        };

        var stops = BoardHistory.BuildStops(entries, book.Load);

        Assert.Equal(2, stops.Count);
        Assert.Equal(1, stops[0].NoteCount);
        Assert.Equal(2, stops[1].NoteCount);
    }

    [Fact]
    public void StateAt_ShowsNotesThatWereDeletedLater()
    {
        var book = new SnapshotBook();
        var morning = MakeNote("早上布置的", At(2026, 3, 4, 8, 0, 0));

        var entries = new List<NoteHistoryEntry>
        {
            new(morning.CreatedAt, morning.Id, NoteHistoryAction.Create, book.Add(morning)),
            new(At(2026, 3, 4, 12, 0, 0), morning.Id, NoteHistoryAction.Delete)
        };

        var atMorning = BoardHistory.StateAt(entries, At(2026, 3, 4, 9, 0, 0), book.Load);
        var atNoon = BoardHistory.StateAt(entries, At(2026, 3, 4, 12, 0, 0), book.Load);

        Assert.Single(atMorning);
        Assert.Equal("早上布置的", atMorning[0].Content);
        Assert.Empty(atNoon);
    }

    [Fact]
    public void StateAt_ShowsTheOldContentOfAnEditedNote()
    {
        var book = new SnapshotBook();
        var note = MakeNote("改之前", At(2026, 3, 4, 8, 0, 0));

        var edited = MakeNote("改之后", note.CreatedAt);
        edited.Id = note.Id;

        var entries = new List<NoteHistoryEntry>
        {
            new(note.CreatedAt, note.Id, NoteHistoryAction.Create, book.Add(note)),
            new(At(2026, 3, 4, 15, 30, 0), note.Id, NoteHistoryAction.Update, book.Add(edited))
        };

        var before = BoardHistory.StateAt(entries, At(2026, 3, 4, 15, 0, 0), book.Load);
        var after = BoardHistory.StateAt(entries, At(2026, 3, 4, 16, 0, 0), book.Load);

        Assert.Equal("改之前", Assert.Single(before).Content);
        Assert.Equal("改之后", Assert.Single(after).Content);
    }

    [Fact]
    public void StateAt_HidesCleanedNotesAndBringsThemBackOnRestore()
    {
        var book = new SnapshotBook();
        var note = MakeNote("写完了", At(2026, 3, 4, 8, 0, 0));

        var entries = new List<NoteHistoryEntry>
        {
            new(note.CreatedAt, note.Id, NoteHistoryAction.Create, book.Add(note)),
            new(At(2026, 3, 5, 4, 0, 0), note.Id, NoteHistoryAction.Clean),
            new(At(2026, 3, 6, 10, 0, 0), note.Id, NoteHistoryAction.Restore)
        };

        Assert.Single(BoardHistory.StateAt(entries, At(2026, 3, 4, 9, 0, 0), book.Load));
        Assert.Empty(BoardHistory.StateAt(entries, At(2026, 3, 5, 8, 0, 0), book.Load));
        Assert.Single(BoardHistory.StateAt(entries, At(2026, 3, 6, 12, 0, 0), book.Load));
    }

    [Fact]
    public void BuildStops_CountsAfterAllEventsOfTheSameSecond()
    {
        var book = new SnapshotBook();
        var note = MakeNote("同一秒里先建后删", At(2026, 3, 4, 8, 0, 0));

        var entries = new List<NoteHistoryEntry>
        {
            new(At(2026, 3, 4, 8, 0, 0), note.Id, NoteHistoryAction.Create, book.Add(note)),
            new(At(2026, 3, 4, 8, 0, 0), note.Id, NoteHistoryAction.Delete)
        };

        var stop = Assert.Single(BoardHistory.BuildStops(entries, book.Load));

        Assert.Equal(0, stop.NoteCount);
    }

    [Fact]
    public void BuildStops_OnEmptyHistory_ProducesNoStops()
        => Assert.Empty(BoardHistory.BuildStops([], _ => null));

    [Fact]
    public void StateAt_WhenSnapshotIsMissing_KeepsThePreviousState()
    {
        var book = new SnapshotBook();
        var note = MakeNote("还在", At(2026, 3, 4, 8, 0, 0));

        var entries = new List<NoteHistoryEntry>
        {
            new(note.CreatedAt, note.Id, NoteHistoryAction.Create, book.Add(note)),

            // 内容缺失（快照丢了 / 日志被手工改过）：不能把这条作业整个抹掉，保留上一次的样子。
            new(At(2026, 3, 4, 9, 0, 0), note.Id, NoteHistoryAction.Update, "missing")
        };

        var state = BoardHistory.StateAt(entries, At(2026, 3, 4, 10, 0, 0), book.Load);

        Assert.Equal("还在", Assert.Single(state).Content);
    }
}
