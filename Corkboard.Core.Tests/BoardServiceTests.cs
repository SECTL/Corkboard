using System.Text.Json;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Enums;
using Corkboard.Core.Models;
using Corkboard.Shared.Abstraction;
using Microsoft.Extensions.Logging.Abstractions;
using CR = Corkboard.Core.Langs.Common.Resources;

using Corkboard.Core.Board;
using Corkboard.Core.Homework;
using Corkboard.Core.Notes;
using Corkboard.Core.Notes.Legacy;
using Corkboard.Core.RichText;

namespace Corkboard.Core.Tests;

/// <summary>
///     作业板领域服务：作业走增删改方法、按创建日期分文件落盘，类型与科目是「直接改集合 + Save」的用法。
///     这里用内存配置服务替换磁盘实现，验证每次变更都会落盘一次。
///     <para>
///         种子科目（<see cref="HomeworkConfig.Subjects" />）是从本地化资源里取的，界面语言一变名字就变，
///         而 <see cref="BoardService.SetSubjectOrder" /> 会把「不在科目清单里」的条目清掉。
///         所以凡是拿种子科目当预期值的地方都写成 <c>CR.Board_Seed_Subject_*</c>，绝不写死中文：
///         CI 跑在 en-US 的机器上，写死「数学」只会被当成已删除的科目而被过滤干净。
///     </para>
/// </summary>
public class BoardServiceTests
{
    private sealed class InMemoryConfigService : ConfigServiceBase
    {
        public Dictionary<string, ConfigBase> Saved { get; } = [];

        public int SaveCount { get; private set; }

        public override bool IsConfigExists<T>(T fallback) => Saved.ContainsKey(fallback.ConfigFilePath);

        public override T LoadConfig<T>(T fallback)
        {
            return Saved.TryGetValue(fallback.ConfigFilePath, out var stored) ? (T)stored : fallback;
        }

        public override void SaveConfig<T>(T config)
        {
            SaveCount++;
            Saved[config.ConfigFilePath] = config;
        }

        public override void DeleteConfig<T>(T config)
        {
            Saved.Remove(config.ConfigFilePath);
        }
    }

    /// <summary>内存里的作业存储：记住每次落盘的快照，不碰磁盘。</summary>
    private sealed class InMemoryNoteStore : INoteStore
    {
        private List<Note> _notes = [];

        public LegacyNoteFile? LegacyFile { get; set; }

        public int SaveCount { get; private set; }

        public int LegacyDeleteCount { get; private set; }

        public IReadOnlyList<Note> LoadAll() => [.. _notes];

        public void SaveAll(IReadOnlyCollection<Note> notes)
        {
            SaveCount++;
            _notes = [.. notes];
        }

        public LegacyNoteFile? TryLoadLegacy() => LegacyFile;

        public void TryDeleteLegacy() => LegacyDeleteCount++;

        public void Seed(IEnumerable<Note> notes) => _notes = [.. notes];
    }

    private static (BoardService Service, InMemoryConfigService Config, InMemoryNoteStore Store) CreateService(
        InMemoryConfigService? config = null,
        InMemoryNoteStore? store = null)
    {
        config ??= new InMemoryConfigService();
        store ??= new InMemoryNoteStore();
        var service = new BoardService(store, new HomeworkSectionCatalog(config), NullLogger<BoardService>.Instance);
        return (service, config, store);
    }

    [Fact]
    public void Add_PersistsContentAndRaisesChanged()
    {
        var (service, config, store) = CreateService();
        var changed = 0;
        service.Changed += (_, _) => changed++;

        var note = service.Add(new Note { Content = "数学", Subject = "数学" });

        Assert.Single(service.Notes);
        Assert.Equal("数学", note.Content);
        Assert.Equal(1, config.SaveCount);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Add_AssignsIncrementingOrder()
    {
        var (service, _, _) = CreateService();

        var first = service.Add(new Note { Content = "一" });
        var second = service.Add(new Note { Content = "二" });

        Assert.Equal(0, first.Order);
        Assert.Equal(1, second.Order);
    }

    [Fact]
    public void Remove_UnknownId_ReturnsFalseWithoutSaving()
    {
        var (service, config, store) = CreateService();

        Assert.False(service.Remove(Guid.NewGuid()));
        Assert.Equal(0, config.SaveCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Remove_ExistingNote_PersistsAndRaisesChanged()
    {
        var (service, config, _) = CreateService();
        var note = service.Add(new Note { Content = "数学" });
        var changed = 0;
        service.Changed += (_, _) => changed++;

        Assert.True(service.Remove(note.Id));
        Assert.Empty(service.Notes);
        Assert.Equal(2, config.SaveCount);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Update_OverwritesEditableFieldsAndRefreshesUpdatedAt()
    {
        var (service, config, store) = CreateService();
        var note = service.Add(new Note
        {
            Content = "旧内容",
            Subject = "语文",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        });
        var savesBefore = config.SaveCount;
        var changed = 0;
        service.Changed += (_, _) => changed++;

        // 编辑表单交上来的是一份新对象：只带 Id 与改后的值。
        var edited = new Note
        {
            Id = note.Id,
            Content = "新内容",
            Subject = "数学"
        };

        Assert.True(service.Update(edited));

        var stored = service.Notes.Single();
        Assert.Equal("新内容", stored.Content);
        Assert.Equal("数学", stored.Subject);
        Assert.True(stored.UpdatedAt > new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(savesBefore + 1, config.SaveCount);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Update_KeepsCreatedAtAndOrderSoTheNoteStaysInItsDatedFile()
    {
        var (service, _, store) = CreateService();
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var note = service.Add(new Note { Content = "一", CreatedAt = createdAt });
        service.Add(new Note { Content = "二" });

        service.Update(new Note { Id = note.Id, Content = "一改" });

        var stored = service.Notes.Single(candidate => candidate.Id == note.Id);
        // 创建时间决定作业归档在哪一天的目录里，编辑绝不能碰它；Order 决定区块内顺序，同理。
        Assert.Equal(createdAt, stored.CreatedAt);
        Assert.Equal(note.Order, stored.Order);
        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void Update_UnknownId_ReturnsFalseWithoutSaving()
    {
        var (service, config, store) = CreateService();
        service.Add(new Note { Content = "一" });
        var savesBefore = config.SaveCount;

        Assert.False(service.Update(new Note { Content = "谁也不认识我" }));
        Assert.Equal(savesBefore, config.SaveCount);
        Assert.Single(service.Notes);
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void SetSubjectOrder_PersistsOrderAndDropsSubjectsThatNoLongerExist()
    {
        var (service, config, _) = CreateService();
        var savesBefore = config.SaveCount;

        service.SetSubjectOrder([CR.Board_Seed_Subject_Math, CR.Board_Seed_Subject_Chinese, "已经删掉的科目"]);

        // 记顺序就是一次落盘（和增删作业一样，每次都写）。
        Assert.Equal(savesBefore + 1, config.SaveCount);
        // 科目清单里没有的条目直接清掉，不留脏数据。
        Assert.Equal([CR.Board_Seed_Subject_Math, CR.Board_Seed_Subject_Chinese], service.SubjectOrder);

        var stored = (HomeworkConfig)config.Saved[new HomeworkConfig().ConfigFilePath];
        Assert.Equal([CR.Board_Seed_Subject_Math, CR.Board_Seed_Subject_Chinese], stored.SubjectOrder);
    }

    [Fact]
    public void SetSubjectOrder_TrimsAndDeduplicatesKeepingTheFirstOccurrence()
    {
        var (service, _, _) = CreateService();

        service.SetSubjectOrder(
        [
            $" {CR.Board_Seed_Subject_Math} ", CR.Board_Seed_Subject_Math, CR.Board_Seed_Subject_Chinese, "  ", CR.Board_Seed_Subject_Chinese
        ]);

        // 「未分类」（空科目）也是板子上真实存在的区块，要能记住位置；重复项只留第一次。
        Assert.Equal([CR.Board_Seed_Subject_Math, CR.Board_Seed_Subject_Chinese, ""], service.SubjectOrder);
    }

    [Fact]
    public void SetSubjectOrder_KeepsUncategorizedInThePositionItWasDropped()
    {
        var (service, _, _) = CreateService();

        service.SetSubjectOrder(["", CR.Board_Seed_Subject_Math]);

        Assert.Equal(["", CR.Board_Seed_Subject_Math], service.SubjectOrder);
    }

    [Fact]
    public void SubjectOrder_RoundTripsThroughReload()
    {
        var (service, config, store) = CreateService();
        service.SetSubjectOrder([CR.Board_Seed_Subject_Chinese, CR.Board_Seed_Subject_Math]);

        var reloaded = new BoardService(store, new HomeworkSectionCatalog(config), NullLogger<BoardService>.Instance);

        Assert.Equal([CR.Board_Seed_Subject_Chinese, CR.Board_Seed_Subject_Math], reloaded.SubjectOrder);
    }

    [Fact]
    public void Save_PersistsEditedSubjects()
    {
        var (service, config, _) = CreateService();
        var before = config.SaveCount;

        service.Subjects.Add("物理");
        service.Save();

        // 集合本身不会自动落盘，必须显式 Save——这是领域服务而不是配置管道的用法。
        Assert.Equal(before + 1, config.SaveCount);
        Assert.Contains("物理", service.Subjects);
    }

    [Fact]
    public void Notes_ComeFromTheNoteStoreNotTheConfigFile()
    {
        // 作业的定义（科目）与作业本身是两份存储：作业只认归档目录。
        var store = new InMemoryNoteStore();
        var note = new Note();
        note.SetHtmlContent("<p>来自归档目录</p>");
        store.Seed([note]);

        var (service, config, _) = CreateService(store: store);

        Assert.Single(service.Notes);
        Assert.Equal("<p>来自归档目录</p>", service.Notes[0].Content);
        Assert.Equal(RichTextContentKind.Html, service.Notes[0].ContentKind);

        // 落盘后配置对象上只有定义，不含作业。
        service.Save();
        var stored = (HomeworkConfig)config.Saved[new HomeworkConfig().ConfigFilePath];
        var json = JsonSerializer.Serialize(stored, ConfigServiceBase.JsonOptions);
        Assert.Contains("\"subjects\"", json);
        Assert.DoesNotContain("\"notes\"", json);
    }

    [Fact]
    public void Reload_ReadsBackWhatWasPersisted()
    {
        var (service, config, store) = CreateService();
        var draft = new Note();
        draft.SetHtmlContent("<p>数学</p>");
        service.Add(draft);
        service.Save();

        var reloaded = new BoardService(store, new HomeworkSectionCatalog(config), NullLogger<BoardService>.Instance);

        Assert.Single(reloaded.Notes);
        Assert.Equal("<p>数学</p>", reloaded.Notes[0].Content);
        Assert.Equal(RichTextContentKind.Html, reloaded.Notes[0].ContentKind);
    }

    [Fact]
    public void FreshConfig_ComesWithSeedSubjects()
    {
        // 首次运行（没有任何作业文件）时不应该是一张白纸：科目得先有几个常见的。
        var (service, _, _) = CreateService();

        Assert.NotEmpty(service.Subjects);
    }

    [Fact]
    public void LegacyNotes_AreMigratedIntoDatedStorageWhenArchiveIsEmpty()
    {
        // 旧版作业全在 data/config/board.json：归档目录还空着时必须读进来并摊到新的日期目录下。
        var legacy = new LegacyNoteFile();
        legacy.Notes.Add(new Note { Content = "旧作业", Subject = "数学" });
        var store = new InMemoryNoteStore { LegacyFile = legacy };

        var (service, _, _) = CreateService(store: store);

        Assert.Single(service.Notes);
        // 旧内容是 Markdown 原文，读进来时会一次性转成富文本片段。
        Assert.Equal("<p>旧作业</p>", service.Notes[0].Content);
        Assert.Equal(RichTextContentKind.Html, service.Notes[0].ContentKind);
        // 搬迁会把读到的旧作业写进归档存储，并删掉旧文件。
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, store.LegacyDeleteCount);
    }

    [Fact]
    public void LegacyDefinition_IsTakenOverWhenNoDefinitionFileExistedYet()
    {
        // 全新安装的第一次迁移：定义文件还没建过，旧文件里的科目就是用户原来那份，应当接管。
        var legacy = new LegacyNoteFile();
        legacy.Notes.Add(new Note { Content = "旧作业" });
        legacy.Subjects.Clear();
        legacy.Subjects.Add("旧科目");
        var store = new InMemoryNoteStore { LegacyFile = legacy };

        var (service, _, _) = CreateService(store: store);

        Assert.Contains("旧科目", service.Subjects);
    }

    [Fact]
    public void LegacyDefinition_IsNotTakenOverWhenCurrentDefinitionExists()
    {
        // 定义文件已经存在（用户管过自己的科目，哪怕清空了）：旧文件里的定义不该回灌，只搬作业。
        var legacy = new LegacyNoteFile();
        legacy.Notes.Add(new Note { Content = "旧作业" });
        legacy.Subjects.Add("旧科目");
        var store = new InMemoryNoteStore { LegacyFile = legacy };

        var config = new InMemoryConfigService();
        config.Saved[new HomeworkConfig().ConfigFilePath] = new HomeworkConfig();

        var (service, _, _) = CreateService(config, store);

        // 作业照搬，定义保持用户自己的。
        Assert.Single(service.Notes);
        Assert.DoesNotContain("旧科目", service.Subjects);
    }

    [Fact]
    public void LegacyNotes_AreIgnoredWhenArchiveAlreadyHasNotes()
    {
        var legacy = new LegacyNoteFile();
        legacy.Notes.Add(new Note { Content = "旧作业" });
        var store = new InMemoryNoteStore { LegacyFile = legacy };
        var archived = new Note();
        archived.SetHtmlContent("<p>新作业</p>");
        store.Seed([archived]);

        var (service, _, _) = CreateService(store: store);

        Assert.Single(service.Notes);
        Assert.Equal("<p>新作业</p>", service.Notes[0].Content);
        // 已经有归档数据就不再搬迁，更不该覆盖。
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, store.LegacyDeleteCount);
    }

    [Fact]
    public void BoardDefinition_IsStoredApartFromTheSettingsFile()
    {
        // 作业数据必须单独一份，不和 settings.json 挤在同一个目录里。
        var boardPath = new HomeworkConfig().ConfigFilePath;

        Assert.EndsWith(Path.Combine("board", "board.json"), boardPath);
        Assert.NotEqual(new MainConfigModel().ConfigFilePath, boardPath);
        Assert.NotEqual(Path.GetDirectoryName(new MainConfigModel().ConfigFilePath),
            Path.GetDirectoryName(boardPath));
    }
}

/// <summary>
///     按年/月/日归档的作业存储：真正跑一遍磁盘，验证目录形状、合并与清理。
/// </summary>
public class NoteStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "corkboard-tests", Guid.NewGuid().ToString("N"));

    private NoteStore CreateStore() => new(NullLogger<NoteStore>.Instance, _root, LegacyPath);

    private string LegacyPath => Path.Combine(_root, "..", "legacy-board.json");

    private static Note Note(string content, int year, int month, int day)
    {
        // 归档按本地时区的日历日分层，所以用本地偏移构造，避免在别的时区跑测试时偏日。
        var local = new DateTime(year, month, day, 10, 0, 0, DateTimeKind.Unspecified);
        return new Note
        {
            Content = content,
            Subject = "数学",
            CreatedAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local))
        };
    }

    [Fact]
    public void SaveAll_WritesIntoYearMonthDayFolders()
    {
        var store = CreateStore();
        store.SaveAll([Note("一", 2026, 3, 4)]);

        var file = Path.Combine(_root, "2026", "03", "04", NoteStore.NotesFileName);

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void SaveAll_GroupsSameDayNotesIntoOneFile()
    {
        var store = CreateStore();
        store.SaveAll([Note("一", 2026, 3, 4), Note("二", 2026, 3, 4), Note("三", 2026, 3, 5)]);

        Assert.True(File.Exists(Path.Combine(_root, "2026", "03", "04", NoteStore.NotesFileName)));
        Assert.True(File.Exists(Path.Combine(_root, "2026", "03", "05", NoteStore.NotesFileName)));

        // 同一天的两条合进一个文件，另一天单独一个文件。
        Assert.Equal(3, store.LoadAll().Count);
        Assert.Equal(2, store.LoadAll().Count(note => note.CreatedAt.Day == 4));
    }

    [Fact]
    public void LoadAll_RoundTripsNotesAndSortsByCreatedAt()
    {
        var store = CreateStore();
        store.SaveAll([Note("晚", 2026, 3, 5), Note("早", 2026, 3, 4)]);

        var loaded = CreateStore().LoadAll();

        Assert.Equal(["早", "晚"], loaded.Select(note => note.Content));
    }

    [Fact]
    public void SaveAll_RemovesNoteFilesThatAreNoLongerNeeded()
    {
        var store = CreateStore();
        var kept = Note("保留", 2026, 3, 4);
        var removed = Note("删掉", 2026, 3, 5);
        store.SaveAll([kept, removed]);

        store.SaveAll([kept]);

        Assert.True(File.Exists(Path.Combine(_root, "2026", "03", "04", NoteStore.NotesFileName)));
        // 空掉的日期目录也要一起回收，不留空壳。
        Assert.False(Directory.Exists(Path.Combine(_root, "2026", "03", "05")));
    }

    [Fact]
    public void LoadAll_ReturnsEmptyWhenRootIsMissing()
    {
        Assert.Empty(CreateStore().LoadAll());
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void LoadAll_SkipsCorruptDayFileAndKeepsTheRest()
    {
        var store = CreateStore();
        store.SaveAll([Note("好", 2026, 3, 4)]);

        var brokenDirectory = Path.Combine(_root, "2026", "03", "05");
        Directory.CreateDirectory(brokenDirectory);
        File.WriteAllText(Path.Combine(brokenDirectory, NoteStore.NotesFileName), "{ 不是 JSON");

        var loaded = CreateStore().LoadAll();

        Assert.Single(loaded);
        Assert.Equal("好", loaded[0].Content);
    }

    [Fact]
    public void GetDateSegments_UsesCreatedDateLocalTime()
    {
        var local = new DateTime(2025, 12, 7, 8, 0, 0, DateTimeKind.Unspecified);
        var createdAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));

        Assert.Equal(["2025", "12", "07"], NoteStore.GetDateSegments(createdAt));
        Assert.EndsWith(Path.Combine("2025", "12", "07"), NoteStore.GetNoteDirectory(new Note
        {
            CreatedAt = createdAt
        }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        if (File.Exists(LegacyPath))
            File.Delete(LegacyPath);
    }
}
