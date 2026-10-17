using Corkboard.Core.Board.History;
using Corkboard.Core.Notes;
using Corkboard.Core.RichText;
using Microsoft.Extensions.Logging.Abstractions;

namespace Corkboard.Core.Tests;

/// <summary>
///     历史存储：追加日志 + 按内容哈希去重的快照库。
///     这里真写临时目录（存储层的行为就是文件系统行为，内存假件验证不了截断/去重/清理）。
/// </summary>
public class NoteHistoryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "corkboard-history-tests",
        Guid.NewGuid().ToString("N"));

    private NoteHistoryStore CreateStore() => new(NullLogger<NoteHistoryStore>.Instance, _root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清不掉不影响断言，交给系统回收。
        }
    }

    private static Note MakeNote(string content, DateTimeOffset? createdAt = null)
    {
        var moment = createdAt ?? DateTimeOffset.Now;
        return new Note
        {
            Id = Guid.NewGuid(),
            Content = content,
            ContentKind = RichTextContentKind.Html,
            Handwriting = null,
            Subject = "数学",
            Section = "第一单元",
            Tags = ["背书"],
            DueDate = new DateOnly(2026, 10, 20),
            CreatedAt = moment,
            UpdatedAt = moment
        };
    }

    [Fact]
    public void AppendAndLoadAll_RoundTripsTheEntry()
    {
        var store = CreateStore();
        var note = MakeNote("写历史");
        var snapshot = store.SaveSnapshot(note);
        var at = new DateTimeOffset(2026, 10, 18, 8, 30, 15, TimeSpan.FromHours(8));

        store.Append(new NoteHistoryEntry(at, note.Id, NoteHistoryAction.Create, snapshot));

        var entry = Assert.Single(store.LoadAll());
        Assert.Equal(note.Id, entry.NoteId);
        Assert.Equal(NoteHistoryAction.Create, entry.Action);
        Assert.Equal(snapshot, entry.Snapshot);
        Assert.Equal(at, entry.At);
    }

    [Fact]
    public void SaveSnapshot_WritesOneBlobPerDistinctContent()
    {
        var store = CreateStore();
        var moment = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.FromHours(8));
        var first = MakeNote("同一个内容", moment);
        var second = MakeNote("同一个内容", moment);
        second.Id = first.Id;

        var firstHash = store.SaveSnapshot(first);
        var secondHash = store.SaveSnapshot(second);

        Assert.Equal(firstHash, secondHash);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "blobs"), "*.br", SearchOption.AllDirectories));
    }

    [Fact]
    public void LoadSnapshot_RestoresEveryContentField()
    {
        var store = CreateStore();
        var note = MakeNote("内容");
        note.SetHandwriting("{\"strokes\":[]}");
        note.ContentKind = RichTextContentKind.Handwriting;

        var restored = store.LoadSnapshot(store.SaveSnapshot(note));

        Assert.NotNull(restored);
        Assert.Equal(note.Id, restored.Id);
        Assert.Equal(note.Content, restored.Content);
        Assert.Equal(ContentKindOf(note), ContentKindOf(restored));
        Assert.Equal(note.Handwriting, restored.Handwriting);
        Assert.Equal(note.Subject, restored.Subject);
        Assert.Equal(note.Section, restored.Section);
        Assert.Equal(note.DueDate, restored.DueDate);
        Assert.Equal(note.CreatedAt, restored.CreatedAt);
        Assert.Equal(["背书"], restored.Tags);
    }

    [Fact]
    public void LoadSnapshot_RejectsUnknownOrMalformedHashes()
    {
        var store = CreateStore();

        Assert.Null(store.LoadSnapshot("not-a-hash"));
        Assert.Null(store.LoadSnapshot(new string('a', 64)));
    }

    [Fact]
    public void ReplaceLast_RewritesTheLastLineOfTheSameNote()
    {
        var store = CreateStore();
        var note = MakeNote("第一版");
        var at = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.FromHours(8));

        store.Append(new NoteHistoryEntry(at, note.Id, NoteHistoryAction.Create, store.SaveSnapshot(note)));

        var edited = MakeNote("第二版");
        edited.Id = note.Id;
        var later = at.AddSeconds(30);

        Assert.True(store.ReplaceLast(new NoteHistoryEntry(later, note.Id, NoteHistoryAction.Update,
            store.SaveSnapshot(edited))));

        // 合并只改最后一行：文件里仍然只有一条（创建事件），内容是新的那一版。
        var entries = store.LoadAll();
        Assert.Single(entries);
        Assert.Equal(NoteHistoryAction.Update, entries[0].Action);
        Assert.Equal("第二版", store.LoadSnapshot(entries[0].Snapshot!)!.Content);
    }

    [Fact]
    public void ReplaceLast_RefusesWhenAnotherNoteWasWritten()
    {
        var store = CreateStore();
        var first = MakeNote("先写这条");
        var second = MakeNote("后写这条");
        var at = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.FromHours(8));

        store.Append(new NoteHistoryEntry(at, first.Id, NoteHistoryAction.Create, store.SaveSnapshot(first)));
        store.Append(new NoteHistoryEntry(at.AddSeconds(5), second.Id, NoteHistoryAction.Create,
            store.SaveSnapshot(second)));

        Assert.False(store.ReplaceLast(new NoteHistoryEntry(at.AddSeconds(10), first.Id, NoteHistoryAction.Update,
            store.SaveSnapshot(first))));

        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void LoadAll_SortsByMomentAndIgnoresCorruptLines()
    {
        var store = CreateStore();
        var note = MakeNote("排序");
        var at = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.FromHours(8));

        store.Append(new NoteHistoryEntry(at.AddMinutes(1), note.Id, NoteHistoryAction.Update,
            store.SaveSnapshot(note)));
        store.Append(new NoteHistoryEntry(at, note.Id, NoteHistoryAction.Create, store.SaveSnapshot(note)));

        // 手工塞一行坏数据：整段历史不能因为一行坏了就读不出来。
        File.AppendAllText(Path.Combine(_root, "2026-10-18.jsonl"), "not json\n");

        var entries = store.LoadAll();
        Assert.Equal(2, entries.Count);
        Assert.Equal(NoteHistoryAction.Create, entries[0].Action);
        Assert.Equal(NoteHistoryAction.Update, entries[1].Action);
    }

    [Fact]
    public void Prune_DeletesLogsOlderThanRetentionAndUnusedBlobs()
    {
        var store = CreateStore();
        store.RetentionDays = BoardHistoryPolicy.MinRetentionDays;

        var old = MakeNote("很旧的作业");
        var oldDay = DateTime.Today.AddDays(-(BoardHistoryPolicy.MinRetentionDays + 5));
        var oldLocal = oldDay.AddHours(8);
        // 用本地时区当天的真实偏移：写死 +08:00 在 UTC 的 CI runner 上会抛
        //「The UTC Offset of the local dateTime parameter does not match the offset argument」。
        var oldAt = new DateTimeOffset(oldLocal, TimeZoneInfo.Local.GetUtcOffset(oldLocal));
        store.Append(new NoteHistoryEntry(oldAt, old.Id, NoteHistoryAction.Create, store.SaveSnapshot(old)));

        var fresh = MakeNote("今天的作业");
        store.Append(new NoteHistoryEntry(DateTimeOffset.Now, fresh.Id, NoteHistoryAction.Create,
            store.SaveSnapshot(fresh)));

        var removed = store.Prune();

        Assert.Equal(1, removed);
        Assert.Single(store.LoadAll());
        Assert.Equal(fresh.Id, store.LoadAll()[0].NoteId);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "blobs"), "*.br", SearchOption.AllDirectories));
    }

    [Fact]
    public void GetStatistics_CountsEntriesSnapshotsAndBytes()
    {
        var store = CreateStore();
        var note = MakeNote("统计");
        store.Append(new NoteHistoryEntry(DateTimeOffset.Now, note.Id, NoteHistoryAction.Create,
            store.SaveSnapshot(note)));

        var statistics = store.GetStatistics();

        Assert.Equal(1, statistics.EntryCount);
        Assert.Equal(1, statistics.SnapshotCount);
        Assert.True(statistics.Bytes > 0);
    }

    private static RichTextContentKind ContentKindOf(Note note) => note.ContentKind;
}
