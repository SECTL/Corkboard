using System.Text.Json;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Abstraction.Services;
using Corkboard.Core.Enums;
using Corkboard.Core.Models;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Services.Board;
using Corkboard.Shared.Abstraction;
using Microsoft.Extensions.Logging.Abstractions;

namespace Corkboard.Core.Tests;

/// <summary>
///     作业板领域服务：作业走增删改方法、按创建日期分文件落盘，类型与科目是「直接改集合 + Save」的用法。
///     这里用内存配置服务替换磁盘实现，验证每次变更都会落盘一次。
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
    private sealed class InMemoryNoteStore : IBoardNoteStore
    {
        private List<BoardNote> _notes = [];

        public LegacyBoardFile? LegacyFile { get; set; }

        public int SaveCount { get; private set; }

        public int LegacyDeleteCount { get; private set; }

        public IReadOnlyList<BoardNote> LoadAll() => [.. _notes];

        public void SaveAll(IReadOnlyCollection<BoardNote> notes)
        {
            SaveCount++;
            _notes = [.. notes];
        }

        public LegacyBoardFile? TryLoadLegacy() => LegacyFile;

        public void TryDeleteLegacy() => LegacyDeleteCount++;

        public void Seed(IEnumerable<BoardNote> notes) => _notes = [.. notes];
    }

    private static (BoardService Service, InMemoryConfigService Config, InMemoryNoteStore Store) CreateService(
        InMemoryConfigService? config = null,
        InMemoryNoteStore? store = null)
    {
        config ??= new InMemoryConfigService();
        store ??= new InMemoryNoteStore();
        var service = new BoardService(config, store, NullLogger<BoardService>.Instance);
        return (service, config, store);
    }

    [Fact]
    public void Add_PersistsContentAndRaisesChanged()
    {
        var (service, config, store) = CreateService();
        var changed = 0;
        service.Changed += (_, _) => changed++;

        var note = service.Add(new BoardNote { Content = "数学", Subject = "数学" });

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

        var first = service.Add(new BoardNote { Content = "一" });
        var second = service.Add(new BoardNote { Content = "二" });

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
        var note = service.Add(new BoardNote { Content = "数学" });
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
        var note = service.Add(new BoardNote
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
        var edited = new BoardNote
        {
            Id = note.Id,
            Content = "新内容",
            Subject = "数学",
            TypeId = Guid.NewGuid(),
            Values = { ["f1"] = "12-15" }
        };

        Assert.True(service.Update(edited));

        var stored = service.Notes.Single();
        Assert.Equal("新内容", stored.Content);
        Assert.Equal("数学", stored.Subject);
        Assert.Equal("12-15", stored.Values["f1"]);
        Assert.True(stored.UpdatedAt > new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(savesBefore + 1, config.SaveCount);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Update_KeepsCreatedAtAndOrderSoTheNoteStaysInItsDatedFile()
    {
        var (service, _, store) = CreateService();
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var note = service.Add(new BoardNote { Content = "一", CreatedAt = createdAt });
        service.Add(new BoardNote { Content = "二" });

        service.Update(new BoardNote { Id = note.Id, Content = "一改" });

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
        service.Add(new BoardNote { Content = "一" });
        var savesBefore = config.SaveCount;

        Assert.False(service.Update(new BoardNote { Content = "谁也不认识我" }));
        Assert.Equal(savesBefore, config.SaveCount);
        Assert.Single(service.Notes);
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void SetSubjectOrder_PersistsOrderAndDropsSubjectsThatNoLongerExist()
    {
        var (service, config, _) = CreateService();
        var savesBefore = config.SaveCount;

        service.SetSubjectOrder(["数学", "语文", "已经删掉的科目"]);

        // 记顺序就是一次落盘（和增删作业一样，每次都写）。
        Assert.Equal(savesBefore + 1, config.SaveCount);
        // 科目清单里没有的条目直接清掉，不留脏数据。
        Assert.Equal(["数学", "语文"], service.SubjectOrder);

        var stored = (BoardConfig)config.Saved[new BoardConfig().ConfigFilePath];
        Assert.Equal(["数学", "语文"], stored.SubjectOrder);
    }

    [Fact]
    public void SetSubjectOrder_TrimsAndDeduplicatesKeepingTheFirstOccurrence()
    {
        var (service, _, _) = CreateService();

        service.SetSubjectOrder([" 数学 ", "数学", "语文", "  ", "语文"]);

        // 空科目（界面上是「未分类」区块）不记进顺序表，重复项只留第一次。
        Assert.Equal(["数学", "语文"], service.SubjectOrder);
    }

    [Fact]
    public void SubjectOrder_RoundTripsThroughReload()
    {
        var (service, config, store) = CreateService();
        service.SetSubjectOrder(["语文", "数学"]);

        var reloaded = new BoardService(config, store, NullLogger<BoardService>.Instance);

        Assert.Equal(["语文", "数学"], reloaded.SubjectOrder);
    }

    [Fact]
    public void FindType_ResolvesByIdAndToleratesMissing()
    {        var (service, _, _) = CreateService();
        var type = service.Types[0];

        Assert.Same(type, service.FindType(type.Id));
        Assert.Null(service.FindType(null));
        Assert.Null(service.FindType(Guid.NewGuid()));
    }

    [Fact]
    public void Save_PersistsEditedTypesAndSubjects()
    {
        var (service, config, _) = CreateService();
        var before = config.SaveCount;

        service.Types.Add(new BoardTypeDef { Name = "作文" });
        service.Subjects.Add("物理");
        service.Save();

        // 集合本身不会自动落盘，必须显式 Save——这是领域服务而不是配置管道的用法。
        Assert.Equal(before + 1, config.SaveCount);
        Assert.Contains(service.Types, type => type.Name == "作文");
        Assert.Contains("物理", service.Subjects);
    }

    [Fact]
    public void Notes_ComeFromTheNoteStoreNotTheConfigFile()
    {
        // 作业的定义（类型/科目）与作业本身是两份存储：作业只认归档目录。
        var store = new InMemoryNoteStore();
        store.Seed([new BoardNote { Content = "来自归档目录" }]);

        var (service, config, _) = CreateService(store: store);

        Assert.Single(service.Notes);
        Assert.Equal("来自归档目录", service.Notes[0].Content);

        // 落盘后配置对象上只有定义，不含作业。
        service.Save();
        var stored = (BoardConfig)config.Saved[new BoardConfig().ConfigFilePath];
        var json = JsonSerializer.Serialize(stored, ConfigServiceBase.JsonOptions);
        Assert.Contains("\"types\"", json);
        Assert.DoesNotContain("\"notes\"", json);
    }

    [Fact]
    public void Reload_ReadsBackWhatWasPersisted()
    {
        var (service, config, store) = CreateService();
        service.Add(new BoardNote { Content = "数学", Values = { ["f1"] = "12-15" } });
        service.Types.Add(new BoardTypeDef { Name = "作文" });
        service.Save();

        var reloaded = new BoardService(config, store, NullLogger<BoardService>.Instance);

        Assert.Single(reloaded.Notes);
        Assert.Equal("数学", reloaded.Notes[0].Content);
        Assert.Equal("12-15", reloaded.Notes[0].Values["f1"]);
        Assert.Contains(reloaded.Types, type => type.Name == "作文");
    }

    [Fact]
    public void FreshConfig_ComesWithSeedTypes()
    {
        // 首次运行（没有任何作业文件）时不应该是一张白纸：得先有几种常见作业类型可挑。
        var (service, _, _) = CreateService();

        Assert.NotEmpty(service.Types);
        Assert.Contains(service.Types,
            type => type.Fields.Any(field => field.Kind == BoardFieldKind.PageRange));
    }

    [Fact]
    public void LegacyNotes_AreMigratedIntoDatedStorageWhenArchiveIsEmpty()
    {
        // 旧版作业全在 data/config/board.json：归档目录还空着时必须读进来并摊到新的日期目录下。
        var legacy = new LegacyBoardFile();
        legacy.Notes.Add(new BoardNote { Content = "旧作业", Subject = "数学" });
        var store = new InMemoryNoteStore { LegacyFile = legacy };

        var (service, _, _) = CreateService(store: store);

        Assert.Single(service.Notes);
        Assert.Equal("旧作业", service.Notes[0].Content);
        // 搬迁会把读到的旧作业写进归档存储，并删掉旧文件。
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, store.LegacyDeleteCount);
    }

    [Fact]
    public void LegacyDefinition_IsTakenOverWhenNoDefinitionFileExistedYet()
    {
        // 全新安装的第一次迁移：定义文件还没建过，旧文件里的类型与科目就是用户原来那份，应当接管。
        var legacy = new LegacyBoardFile();
        legacy.Notes.Add(new BoardNote { Content = "旧作业" });
        legacy.Types.Clear();
        legacy.Types.Add(new BoardTypeDef { Name = "旧类型" });
        legacy.Subjects.Clear();
        legacy.Subjects.Add("旧科目");
        var store = new InMemoryNoteStore { LegacyFile = legacy };

        var (service, _, _) = CreateService(store: store);

        Assert.Contains(service.Types, type => type.Name == "旧类型");
        Assert.Contains("旧科目", service.Subjects);
    }

    [Fact]
    public void LegacyDefinition_IsNotTakenOverWhenCurrentDefinitionExists()
    {
        // 定义文件已经存在（用户管过自己的类型与科目，哪怕清空了）：旧文件里的定义不该回灌，只搬作业。
        var legacy = new LegacyBoardFile();
        legacy.Notes.Add(new BoardNote { Content = "旧作业" });
        legacy.Types.Add(new BoardTypeDef { Name = "旧类型" });
        legacy.Subjects.Add("旧科目");
        var store = new InMemoryNoteStore { LegacyFile = legacy };

        var config = new InMemoryConfigService();
        config.Saved[new BoardConfig().ConfigFilePath] = new BoardConfig();

        var (service, _, _) = CreateService(config, store);

        // 作业照搬，定义保持用户自己的。
        Assert.Single(service.Notes);
        Assert.DoesNotContain(service.Types, type => type.Name == "旧类型");
        Assert.DoesNotContain("旧科目", service.Subjects);
    }

    [Fact]
    public void LegacyNotes_AreIgnoredWhenArchiveAlreadyHasNotes()
    {
        var legacy = new LegacyBoardFile();
        legacy.Notes.Add(new BoardNote { Content = "旧作业" });
        var store = new InMemoryNoteStore { LegacyFile = legacy };
        store.Seed([new BoardNote { Content = "新作业" }]);

        var (service, _, _) = CreateService(store: store);

        Assert.Single(service.Notes);
        Assert.Equal("新作业", service.Notes[0].Content);
        // 已经有归档数据就不再搬迁，更不该覆盖。
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, store.LegacyDeleteCount);
    }

    [Fact]
    public void BoardDefinition_IsStoredApartFromTheSettingsFile()
    {
        // 作业数据必须单独一份，不和 settings.json 挤在同一个目录里。
        var boardPath = new BoardConfig().ConfigFilePath;

        Assert.EndsWith(Path.Combine("board", "board.json"), boardPath);
        Assert.NotEqual(new MainConfigModel().ConfigFilePath, boardPath);
        Assert.NotEqual(Path.GetDirectoryName(new MainConfigModel().ConfigFilePath),
            Path.GetDirectoryName(boardPath));
    }
}

/// <summary>
///     按年/月/日归档的作业存储：真正跑一遍磁盘，验证目录形状、合并与清理。
/// </summary>
public class BoardNoteStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "corkboard-tests", Guid.NewGuid().ToString("N"));

    private BoardNoteStore CreateStore() => new(NullLogger<BoardNoteStore>.Instance, _root, LegacyPath);

    private string LegacyPath => Path.Combine(_root, "..", "legacy-board.json");

    private static BoardNote Note(string content, int year, int month, int day)
    {
        // 归档按本地时区的日历日分层，所以用本地偏移构造，避免在别的时区跑测试时偏日。
        var local = new DateTime(year, month, day, 10, 0, 0, DateTimeKind.Unspecified);
        return new BoardNote
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

        var file = Path.Combine(_root, "2026", "03", "04", BoardNoteStore.NotesFileName);

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void SaveAll_GroupsSameDayNotesIntoOneFile()
    {
        var store = CreateStore();
        store.SaveAll([Note("一", 2026, 3, 4), Note("二", 2026, 3, 4), Note("三", 2026, 3, 5)]);

        Assert.True(File.Exists(Path.Combine(_root, "2026", "03", "04", BoardNoteStore.NotesFileName)));
        Assert.True(File.Exists(Path.Combine(_root, "2026", "03", "05", BoardNoteStore.NotesFileName)));

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

        Assert.True(File.Exists(Path.Combine(_root, "2026", "03", "04", BoardNoteStore.NotesFileName)));
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
        File.WriteAllText(Path.Combine(brokenDirectory, BoardNoteStore.NotesFileName), "{ 不是 JSON");

        var loaded = CreateStore().LoadAll();

        Assert.Single(loaded);
        Assert.Equal("好", loaded[0].Content);
    }

    [Fact]
    public void GetDateSegments_UsesCreatedDateLocalTime()
    {
        var local = new DateTime(2025, 12, 7, 8, 0, 0, DateTimeKind.Unspecified);
        var createdAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));

        Assert.Equal(["2025", "12", "07"], BoardNoteStore.GetDateSegments(createdAt));
        Assert.EndsWith(Path.Combine("2025", "12", "07"), BoardNoteStore.GetNoteDirectory(new BoardNote
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
