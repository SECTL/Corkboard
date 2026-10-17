using Corkboard.Core.Abstraction;
using Corkboard.Core.Board;
using Corkboard.Core.Board.History;
using Corkboard.Core.Homework;
using Corkboard.Core.Notes;
using Corkboard.Core.Notes.Legacy;
using Corkboard.Core.RichText;
using Corkboard.Shared.Abstraction;
using Microsoft.Extensions.Logging.Abstractions;

namespace Corkboard.Core.Tests;

/// <summary>
///     作业板的每次变更都要落到历史日志里（时空回放的数据源），
///     「还原到此刻」还要能把整条删掉的作业按快照重新插回来。
///     这里用真的 <see cref="NoteHistoryStore" />（临时目录）+ 内存里的作业存储。
/// </summary>
public class BoardServiceHistoryTests : IDisposable
{
    private readonly string _historyRoot = Path.Combine(Path.GetTempPath(), "corkboard-board-history-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_historyRoot))
                Directory.Delete(_historyRoot, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清不掉不影响断言。
        }
    }

    private sealed class InMemoryConfigService : ConfigServiceBase
    {
        private readonly Dictionary<string, ConfigBase> _saved = [];

        public override bool IsConfigExists<T>(T fallback) => _saved.ContainsKey(fallback.ConfigFilePath);

        public override T LoadConfig<T>(T fallback)
            => _saved.TryGetValue(fallback.ConfigFilePath, out var stored) ? (T)stored : fallback;

        public override void SaveConfig<T>(T config) => _saved[config.ConfigFilePath] = config;

        public override void DeleteConfig<T>(T config) => _saved.Remove(config.ConfigFilePath);
    }

    private sealed class InMemoryNoteStore : INoteStore
    {
        private List<Note> _notes = [];

        public IReadOnlyList<Note> LoadAll() => [.. _notes];

        public void SaveAll(IReadOnlyCollection<Note> notes) => _notes = [.. notes];

        public LegacyNoteFile? TryLoadLegacy() => null;

        public void TryDeleteLegacy()
        {
        }

        public void Seed(IEnumerable<Note> notes) => _notes = [.. notes];
    }

    private static Note MakeNote(string content, DateTimeOffset? createdAt = null)
    {
        var moment = createdAt ?? DateTimeOffset.Now;
        return new Note
        {
            Id = Guid.NewGuid(),
            Content = content,
            ContentKind = RichTextContentKind.Html,
            Subject = "数学",
            CreatedAt = moment,
            UpdatedAt = moment
        };
    }

    private (BoardService Service, InMemoryNoteStore Notes, NoteHistoryStore History) CreateService(
        params Note[] seeded)
    {
        var noteStore = new InMemoryNoteStore();
        if (seeded.Length > 0)
            noteStore.Seed(seeded);

        var history = new NoteHistoryStore(NullLogger<NoteHistoryStore>.Instance, _historyRoot);
        var service = new BoardService(noteStore, new HomeworkSectionCatalog(new InMemoryConfigService()),
            NullLogger<BoardService>.Instance, history);

        return (service, noteStore, history);
    }

    [Fact]
    public void Add_RecordsCreateWithSnapshot()
    {
        var (service, _, history) = CreateService();

        var note = service.Add(MakeNote("新作业"));

        var entry = Assert.Single(history.LoadAll());
        Assert.Equal(note.Id, entry.NoteId);
        Assert.Equal(NoteHistoryAction.Create, entry.Action);
        Assert.Equal(note.CreatedAt, entry.At);
        Assert.Equal("新作业", history.LoadSnapshot(entry.Snapshot!)!.Content);
    }

    [Fact]
    public void Update_RecordsOneUpdateEntryPerBurst()
    {
        var (service, _, history) = CreateService();
        var note = service.Add(MakeNote("初稿"));

        var edited = MakeNote("改过的稿");
        edited.Id = note.Id;
        service.Update(edited);

        var second = MakeNote("再改一次");
        second.Id = note.Id;
        service.Update(second);

        var entries = history.LoadAll();
        Assert.Equal(NoteHistoryAction.Create, entries[0].Action);

        // 连续两次保存落在同一个合并窗口里：只留最后那一版。
        var update = Assert.Single(entries.Skip(1));
        Assert.Equal(NoteHistoryAction.Update, update.Action);
        Assert.Equal("再改一次", history.LoadSnapshot(update.Snapshot!)!.Content);
    }

    [Fact]
    public void Remove_RecordsDeleteWithSnapshotOfDeletedNote()
    {
        var (service, _, history) = CreateService();
        var note = service.Add(MakeNote("要删掉的"));

        Assert.True(service.Remove(note.Id));

        var delete = history.LoadAll().Single(entry => entry.Action == NoteHistoryAction.Delete);
        Assert.Equal(note.Id, delete.NoteId);
        Assert.Equal("要删掉的", history.LoadSnapshot(delete.Snapshot!)!.Content);
    }

    [Fact]
    public void CleanMany_RecordsCleanEntry()
    {
        var (service, _, history) = CreateService();
        var note = service.Add(MakeNote("要清理的"));

        service.CleanMany([note.Id], DateTimeOffset.Now);

        var clean = history.LoadAll().Single(entry => entry.Action == NoteHistoryAction.Clean);
        Assert.Equal(note.Id, clean.NoteId);
        Assert.Null(clean.Snapshot);
    }

    [Fact]
    public void RestoreMany_RecordsRestoreEntry()
    {
        var (service, _, history) = CreateService();
        var note = service.Add(MakeNote("收起来又放开"));
        service.CleanMany([note.Id], DateTimeOffset.Now);

        service.RestoreMany([note.Id]);

        var restore = history.LoadAll().Single(entry => entry.Action == NoteHistoryAction.Restore);
        Assert.Equal(note.Id, restore.NoteId);
    }

    [Fact]
    public void RestoreSnapshot_BringsBackADeletedNoteWithItsIdentity()
    {
        var (service, notes, history) = CreateService();
        var created = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(8));
        var note = service.Add(MakeNote("被删掉的作业", created));
        service.Remove(note.Id);

        // 按历史快照把它整份放回来（就是「还原到此刻」做的事）。
        var deleted = history.LoadAll().Single(entry => entry.Action == NoteHistoryAction.Delete);
        var snapshot = history.LoadSnapshot(deleted.Snapshot!)!;

        var changed = service.RestoreSnapshot([snapshot]);

        Assert.Equal(1, changed);
        var restored = Assert.Single(service.Notes);
        Assert.Equal(note.Id, restored.Id);
        Assert.Equal(created, restored.CreatedAt);
        Assert.Equal("被删掉的作业", restored.Content);
        Assert.Equal(NoteHistoryAction.Create, history.LoadAll()[^1].Action);
        Assert.Contains(service.Notes, item => item.Id == note.Id);
        Assert.Single(notes.LoadAll());
    }

    [Fact]
    public void RestoreSnapshot_HidesNotesThatAreNotInTheSnapshot()
    {
        var (service, _, _) = CreateService();
        var kept = service.Add(MakeNote("那一刻在板上"));
        var later = service.Add(MakeNote("那一刻还没出现"));

        var changed = service.RestoreSnapshot([kept]);

        Assert.Equal(1, changed);
        Assert.Null(service.Notes.Single(note => note.Id == kept.Id).CleanedAt);
        Assert.NotNull(service.Notes.Single(note => note.Id == later.Id).CleanedAt);
    }

    [Fact]
    public void Constructor_SeedsBaselineEntriesForExistingNotes()
    {
        var created = new DateTimeOffset(2026, 9, 30, 7, 30, 0, TimeSpan.FromHours(8));
        var (_, _, history) = CreateService(MakeNote("本来就有的作业", created));

        var entry = Assert.Single(history.LoadAll());
        Assert.Equal(NoteHistoryAction.Create, entry.Action);
        Assert.Equal(created, entry.At);
    }

    [Fact]
    public void Constructor_DoesNotSeedAgainWhenHistoryExists()
    {
        var (_, _, history) = CreateService(MakeNote("老作业"));
        Assert.Single(history.LoadAll());

        // 第二次启动：历史已经有内容，不能再播种一遍（否则回放里会出现重复的「新增」）。
        var (_, _, secondHistory) = CreateService(MakeNote("老作业"));
        Assert.Equal(history.LoadAll().Count, secondHistory.LoadAll().Count);
    }

    [Fact]
    public void Reload_SeedsHistoryForNotesThatArrivedFromDisk()
    {
        var (service, notes, history) = CreateService();
        var created = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(8));
        var arrived = MakeNote("云同步带进来的作业", created);

        // 云同步恢复会先把作业写到磁盘、再让服务 Reload：历史里没有它，要补一条基线。
        notes.Seed([arrived]);
        service.Reload();

        var entry = Assert.Single(history.LoadAll());
        Assert.Equal(NoteHistoryAction.Create, entry.Action);
        Assert.Equal(arrived.Id, entry.NoteId);
        Assert.Equal(created, entry.At);
        Assert.NotNull(history.LoadSnapshot(entry.Snapshot!));
        Assert.Contains(BoardHistory.StateAt(history.LoadAll(), DateTimeOffset.Now, history.LoadSnapshot),
            note => note.Id == arrived.Id);
    }

    [Fact]
    public void Reload_RecordsAnUpdateWhenARestoredNoteChanged()
    {
        var created = new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.FromHours(8));
        var original = MakeNote("旧内容", created);
        var (service, notes, history) = CreateService(original);

        var replacement = MakeNote("云端改过的内容", created);
        replacement.Id = original.Id;
        notes.Seed([replacement]);
        service.Reload();

        Assert.Contains(history.LoadAll(),
            entry => entry.NoteId == original.Id && entry.Action == NoteHistoryAction.Update);
        Assert.Equal("云端改过的内容", service.Notes.Single(note => note.Id == original.Id).Content);
    }
}
