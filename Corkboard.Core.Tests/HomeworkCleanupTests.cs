using System.Text.Json;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Enums;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Services.Config;
using Microsoft.Extensions.Logging.Abstractions;
using CR = Corkboard.Core.Langs.Common.Resources;

using Corkboard.Core.Board;
using Corkboard.Core.Homework;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Tests;

/// <summary>
///     自动清理过期作业：判定只有一条（过了截止日期），到点判定按「每天 / 每周某天某时刻」。
///     <para>
///     清理只给作业打「已清理」标记（<see cref="Note.CleanedAt" />），作业数据留在原来那个日期文件里，
///     所以这里断言的重点是「板子上看不见了」＋「文件里还在」。
///     </para>
///     <para>
///     作业根、定义文件、settings.json 全部指向临时沙箱，绝不碰开发机上的真实数据根。
///     </para>
/// </summary>
public class HomeworkCleanupTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(), "corkboard-cleanup", Guid.NewGuid().ToString("N"));

    private string BoardRoot => Path.Combine(_sandbox, "board");

    private string ConfigPath => Path.Combine(BoardRoot, "board.json");

    private string SettingsPath => Path.Combine(_sandbox, "config", "settings.json");

    private NoteStore CreateStore() => new(NullLogger<NoteStore>.Instance, BoardRoot, null);

    /// <summary>把配置写到沙箱里的 JSON 文件：真实序列化，但不碰 <c>Utils</c> 解析出的真实路径。</summary>
    private sealed class SandboxConfigService(string filePath) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => File.Exists(filePath);

        public override T LoadConfig<T>(T fallback)
        {
            if (!File.Exists(filePath))
                return fallback;

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }

        public override void SaveConfig<T>(T config)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(config, JsonOptions));
        }

        public override void DeleteConfig<T>(T config)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private sealed record Harness(
        BoardService Board,
        MainConfigHandler Config,
        HomeworkCleanupService Cleanup);

    private Harness CreateHarness()
    {
        var board = new BoardService(CreateStore(), new HomeworkSectionCatalog(new SandboxConfigService(ConfigPath)), NullLogger<BoardService>.Instance);
        var config = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new SandboxConfigService(SettingsPath));
        var cleanup = new HomeworkCleanupService(config, board, NullLogger<HomeworkCleanupService>.Instance);

        // 清理时刻钉死在 4:00：判定只看截止日期、与几点无关，钉死只是让到点用例好写。
        var settings = config.Data.BoardSettings;
        settings.CleanupHour = 4;
        settings.CleanupMinute = 0;

        return new Harness(board, config, cleanup);
    }

    private static DateTimeOffset LocalTime(int year, int month, int day, int hour = 10, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    // ── 判定：只看截止日期 ──────────────────────────────────────────────────

    /// <summary>
    ///     过期只有一条规则：<b>过了截止日期才算过期</b>（截止当天不算，要等第二天）。
    ///     没填截止日期的作业不管建了多久都不会被清掉。
    /// </summary>
    [Fact]
    public void Expiry_OnlyLooksAtTheDueDate()
    {
        var ancient = new Note { CreatedAt = DateTimeOffset.Now.AddDays(-4000) };
        var dueToday = new Note { DueDate = Today };
        var dueTomorrow = new Note { DueDate = Today.AddDays(1) };
        var dueYesterday = new Note { DueDate = Today.AddDays(-1) };
        var dueLongAgo = new Note { DueDate = Today.AddDays(-4000) };

        Assert.Null(HomeworkExpiryPolicy.ExpiryDate(ancient));
        Assert.False(HomeworkExpiryPolicy.IsExpired(ancient, Today));
        Assert.False(HomeworkExpiryPolicy.IsExpired(dueToday, Today));
        Assert.False(HomeworkExpiryPolicy.IsExpired(dueTomorrow, Today));
        Assert.True(HomeworkExpiryPolicy.IsExpired(dueYesterday, Today));
        Assert.True(HomeworkExpiryPolicy.IsExpired(dueLongAgo, Today));
    }

    /// <summary>
    ///     清理判据与界面那行「今天截止 / 过期 N 天」<b>完全一致</b>：截止当天两边都说还没过期，
    ///     第二天两边才都算过期。用户明确要求「今天截止的作业今天不应该清理」。
    /// </summary>
    [Fact]
    public void Expiry_MatchesTheCardLabel()
    {
        foreach (var offset in new[] { -7, -3, -1, 0, 1, 7 })
        {
            var due = Today.AddDays(offset);
            var note = new Note { DueDate = due };

            Assert.Equal(HomeworkDueDateFormatter.IsOverdue(due, Today), HomeworkExpiryPolicy.IsExpired(note, Today));
        }

        // 当天：界面的文案是「今天截止」，清理也不该动它。
        Assert.False(HomeworkDueDateFormatter.IsOverdue(Today, Today));
        Assert.False(HomeworkExpiryPolicy.IsExpired(new Note { DueDate = Today }, Today));
    }

    /// <summary>自动清理默认开着（用户明确要的默认值），设置页第一眼看到的就是已启用。</summary>
    [Fact]
    public void AutomaticCleanup_IsEnabledByDefault()
    {
        var harness = CreateHarness();

        Assert.True(harness.Config.Data.BoardSettings.CleanupEnabled);
    }

    /// <summary>
    ///     自动清理默认每天 8:00 动手（用户指定的默认值）：早上一开始，板子上就只剩还没过截止日期的作业。
    ///     这里用没被 <see cref="CreateHarness" /> 改过的配置，钉住「出厂默认」本身。
    /// </summary>
    [Fact]
    public void AutomaticCleanup_DefaultTimeIsEightOClockEveryDay()
    {
        var config = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance, new SandboxConfigService(SettingsPath));
        var settings = config.Data.BoardSettings;

        Assert.Equal(HomeworkCleanupFrequency.Daily, settings.CleanupFrequency);
        Assert.Equal(8, settings.CleanupHour);
        Assert.Equal(0, settings.CleanupMinute);
    }

    // ── 到点判定 ────────────────────────────────────────────────────────────

    [Fact]
    public void Schedule_DailySlotFiresOnlyAfterTodaysConfiguredTime()
    {
        var now = LocalTime(2026, 5, 20, 5);

        // 还没到今天的 6 点：就算这次会话还没动过手（凌晨刚启动），也一律不动手。
        Assert.Null(HomeworkCleanupSchedule.DueSlotToday(HomeworkCleanupFrequency.Daily, DayOfWeek.Monday, 6, 0, now));
        Assert.False(HomeworkCleanupSchedule.ShouldRun(HomeworkCleanupFrequency.Daily, DayOfWeek.Monday, 6, 0, null, now));

        // 昨天 6 点已经清过了 → 今天 6 点还没到，不该再动手。
        Assert.False(HomeworkCleanupSchedule.ShouldRun(HomeworkCleanupFrequency.Daily, DayOfWeek.Monday, 6, 0, LocalTime(2026, 5, 19, 6), now));

        // 到了今天的 6 点之后，昨天那次就不算数了。
        Assert.Equal(
            LocalTime(2026, 5, 20, 6),
            HomeworkCleanupSchedule.DueSlotToday(HomeworkCleanupFrequency.Daily, DayOfWeek.Monday, 6, 0, LocalTime(2026, 5, 20, 6)));
        Assert.True(HomeworkCleanupSchedule.ShouldRun(HomeworkCleanupFrequency.Daily, DayOfWeek.Monday, 6, 0, LocalTime(2026, 5, 19, 6), LocalTime(2026, 5, 20, 6, 0)));

        // 今天已经清过一次（哪怕清了之后还没到 6 点——比如刚清完就把时刻往后调），也不再重复。
        var justRan = LocalTime(2026, 5, 20, 6);
        Assert.False(HomeworkCleanupSchedule.ShouldRun(HomeworkCleanupFrequency.Daily, DayOfWeek.Monday, 6, 0, justRan, justRan));
    }

    [Fact]
    public void Schedule_WeeklySlotFiresOnlyOnTheConfiguredWeekdayAfterTheConfiguredTime()
    {
        // 2026-05-20 是星期三：按周清理定在星期一 6 点，这一整天都不该动手。
        var wednesday = LocalTime(2026, 5, 20, 5);
        Assert.Null(HomeworkCleanupSchedule.DueSlotToday(HomeworkCleanupFrequency.Weekly, DayOfWeek.Monday, 6, 0, wednesday));
        Assert.False(HomeworkCleanupSchedule.ShouldRun(HomeworkCleanupFrequency.Weekly, DayOfWeek.Monday, 6, 0, null, wednesday));

        // 星期一但还没到 6 点：不动手；过了 6 点：动手，并且只认今天这个时刻。
        var mondayBefore = LocalTime(2026, 5, 18, 5);
        Assert.Null(HomeworkCleanupSchedule.DueSlotToday(HomeworkCleanupFrequency.Weekly, DayOfWeek.Monday, 6, 0, mondayBefore));
        Assert.False(HomeworkCleanupSchedule.ShouldRun(HomeworkCleanupFrequency.Weekly, DayOfWeek.Monday, 6, 0, null, mondayBefore));

        var mondayAfter = LocalTime(2026, 5, 18, 6);
        Assert.Equal(
            LocalTime(2026, 5, 18, 6),
            HomeworkCleanupSchedule.DueSlotToday(HomeworkCleanupFrequency.Weekly, DayOfWeek.Monday, 6, 0, mondayAfter));
        Assert.True(HomeworkCleanupSchedule.ShouldRun(HomeworkCleanupFrequency.Weekly, DayOfWeek.Monday, 6, 0, LocalTime(2026, 5, 11, 6), mondayAfter));
        Assert.False(HomeworkCleanupSchedule.ShouldRun(HomeworkCleanupFrequency.Weekly, DayOfWeek.Monday, 6, 0, mondayAfter, mondayAfter));
    }

    /// <summary>自动清理关掉时，到点判定永远为假（手动「立即清理」不受影响，见下）。</summary>
    [Fact]
    public void IsDue_IgnoresEverythingWhileAutomaticCleanupIsOff()
    {
        var harness = CreateHarness();
        harness.Config.Data.BoardSettings.CleanupEnabled = false;

        Assert.False(harness.Cleanup.IsDue(LocalTime(2026, 5, 20, 23)));

        harness.Config.Data.BoardSettings.CleanupEnabled = true;
        harness.Config.Data.BoardSettings.CleanupFrequency = HomeworkCleanupFrequency.Daily;
        harness.Config.Data.BoardSettings.CleanupHour = 6;

        Assert.True(harness.Cleanup.IsDue(LocalTime(2026, 5, 20, 23)));

        // 动手之后当天不再重复。
        harness.Cleanup.Run(LocalTime(2026, 5, 20, 23));
        Assert.False(harness.Cleanup.IsDue(LocalTime(2026, 5, 20, 23, 30)));
    }

    // ── 清理：从板子上拿掉，数据原地不动 ──────────────────────────────────────

    /// <summary>
    ///     过了截止日期就从板子上清掉——不管作业是哪天建的，也不管现在是几点
    ///     （用户点「立即清理」本来就该立刻见效）；截止当天的不动、没填截止日期的一条都不动。
    /// </summary>
    [Fact]
    public void Run_ClearsExpiredNotesFromTheBoardAndLeavesTheDataInPlace()
    {
        var harness = CreateHarness();
        var board = harness.Board;

        var overdueLong = board.Add(new Note { Content = "早过期了", Subject = "语文", DueDate = Today.AddDays(-30) });
        var overdueYesterday = board.Add(new Note { Content = "昨天交", Subject = "数学", DueDate = Today.AddDays(-1) });
        var dueToday = board.Add(new Note { Content = "今天交", Subject = "语文", DueDate = Today });
        var dueLater = board.Add(new Note { Content = "下周交", Subject = "英语", CreatedAt = DateTimeOffset.Now.AddDays(-4000), DueDate = Today.AddDays(7) });
        var withoutDueDate = board.Add(new Note { Content = "没期限", Subject = "语文", CreatedAt = DateTimeOffset.Now.AddDays(-4000) });

        var result = harness.Cleanup.Run(DateTimeOffset.Now);

        // 清掉的只是「板子上看不见」：作业仍旧留在内存集合里——不留在集合里的话，
        // 下一次落盘会把这条日期文件整个重写成没有它的样子，那就真没了。
        // 今天截止的那条不算过期（用户明确要求今天不清理），要等明天。
        Assert.Equal(2, result.Cleaned);
        Assert.Equal(5, board.Notes.Count);
        Assert.NotNull(board.Notes.Single(note => note.Id == overdueLong.Id).CleanedAt);
        Assert.NotNull(board.Notes.Single(note => note.Id == overdueYesterday.Id).CleanedAt);
        Assert.Null(board.Notes.Single(note => note.Id == dueToday.Id).CleanedAt);
        Assert.False(board.Notes.Single(note => note.Id == overdueLong.Id).IsOnBoard);
        Assert.False(board.Notes.Single(note => note.Id == overdueYesterday.Id).IsOnBoard);
        Assert.True(board.Notes.Single(note => note.Id == dueToday.Id).IsOnBoard);
        Assert.True(board.Notes.Single(note => note.Id == dueLater.Id).IsOnBoard);
        Assert.True(board.Notes.Single(note => note.Id == withoutDueDate.Id).IsOnBoard);

        // 也没有多出第二个目录：作业只在自己那个日期文件里。
        Assert.DoesNotContain(
            Directory.GetDirectories(BoardRoot, "*", SearchOption.AllDirectories),
            directory => Path.GetFileName(directory).StartsWith('_'));

        // 真落盘了：重开一个存储层，五条作业都在，标记也跟着回来。
        var reloaded = CreateStore().LoadAll();
        Assert.Equal(5, reloaded.Count);
        Assert.NotNull(reloaded.Single(note => note.Id == overdueLong.Id).CleanedAt);
        Assert.Null(reloaded.Single(note => note.Id == dueToday.Id).CleanedAt);
        Assert.Null(reloaded.Single(note => note.Id == dueLater.Id).CleanedAt);
    }

    /// <summary>「立即清理」不看时刻：清理时刻设在 23:00，上午手动跑一样把过了截止日期的清掉。</summary>
    [Fact]
    public void Run_IgnoresTheConfiguredHour()
    {
        var harness = CreateHarness();
        harness.Config.Data.BoardSettings.CleanupHour = 23;
        harness.Board.Add(new Note { Content = "过期了", DueDate = Today.AddDays(-2) });

        var result = harness.Cleanup.Run(LocalTime(Today.Year, Today.Month, Today.Day));

        Assert.Equal(1, result.Cleaned);
    }

    /// <summary>
    ///     清理是幂等的：已经清过的作业不会在下一轮被再算一次，标记也不会被刷新
    ///     （不然「上次清理」每轮都像刚清完东西一样）。
    /// </summary>
    [Fact]
    public void Run_DoesNotCountNotesThatAreAlreadyCleaned()
    {
        var harness = CreateHarness();
        var note = harness.Board.Add(new Note { Content = "过期了", DueDate = Today.AddDays(-30) });

        var first = harness.Cleanup.Run(DateTimeOffset.Now);
        var marked = harness.Board.Notes.Single().CleanedAt;

        var second = harness.Cleanup.Run(DateTimeOffset.Now.AddHours(1));

        Assert.Equal(1, first.Cleaned);
        Assert.Equal(0, second.Cleaned);
        Assert.Equal(marked, harness.Board.Notes.Single().CleanedAt);
        Assert.Equal(note.Id, harness.Board.Notes.Single().Id);
    }

    /// <summary>
    ///     旧版回收目录（<c>_trash</c>）里可能还留着以前归档的批次：存储层必须继续把它整棵子树排除，
    ///     否则那些作业会被当成活作业读回板子，紧接着又在「删掉这一轮没写过的文件」里被清干净。
    /// </summary>
    [Fact]
    public void LegacyTrashFolder_IsInvisibleToTheNoteStore()
    {
        var harness = CreateHarness();
        var alive = harness.Board.Add(new Note { Content = "留下来的" });

        var trash = Path.Combine(BoardRoot, "_trash", "20260101-040000");
        Directory.CreateDirectory(trash);
        var trashedFile = Path.Combine(trash, NoteStore.NotesFileName);
        File.WriteAllText(
            trashedFile,
            JsonSerializer.Serialize(
                new NoteDayFile { Notes = [new Note { Content = "以前归档的" }] },
                ConfigServiceBase.JsonOptions));

        // 读：被归档的作业不会回到板子上。
        Assert.Equal([alive.Id], CreateStore().LoadAll().Select(note => note.Id));

        // 写：下一次保存（含「删掉这轮没写过的文件」）也不碰回收目录。
        harness.Board.Save();
        Assert.True(File.Exists(trashedFile));
        Assert.Contains("以前归档的", File.ReadAllText(trashedFile));
    }

    // ── 截止日期本身 ────────────────────────────────────────────────────────

    /// <summary>
    ///     编辑一条作业时必须把截止日期一起写回去：<see cref="BoardService.Update" /> 是逐字段复制的，
    ///     漏掉哪一个就等于「一编辑就把那一项洗掉」。
    /// </summary>
    [Fact]
    public void Update_KeepsTheDueDate()
    {
        var harness = CreateHarness();
        var note = harness.Board.Add(new Note { Content = "原样" });
        var due = Today.AddDays(3);

        var stored = harness.Board.Notes.Single();
        stored.DueDate = due;

        var edited = new Note { Id = note.Id, Content = "改过", DueDate = due };
        Assert.True(harness.Board.Update(edited));

        Assert.Equal(due, harness.Board.Notes.Single().DueDate);
        Assert.Equal(due, CreateStore().LoadAll().Single().DueDate);
    }

    /// <summary>
    ///     编辑今天这条作业时，不能顺手动到别条作业：<see cref="BoardService.Update" /> 按 Id 定位，
    ///     `CreatedAt` / `Order` 不参与编辑（前者决定归档到哪个日期文件、后者决定区块内顺序）。
    /// </summary>
    [Fact]
    public void Update_TouchesOnlyTheEditedNote()
    {
        var harness = CreateHarness();
        var earlier = harness.Board.Add(new Note { Content = "以前的", CreatedAt = LocalTime(2026, 1, 5) });
        var today = harness.Board.Add(new Note { Content = "今天的" });

        var edited = new Note { Id = today.Id, Content = "今天的（改过）" };
        Assert.True(harness.Board.Update(edited));

        var earlierAfter = harness.Board.Notes.Single(note => note.Id == earlier.Id);
        Assert.Equal("以前的", earlierAfter.Content);
        Assert.Equal(LocalTime(2026, 1, 5), earlierAfter.CreatedAt);
        Assert.Equal("今天的（改过）", harness.Board.Notes.Single(note => note.Id == today.Id).Content);
    }

    /// <summary>
    ///     <c>DueDate</c> 是可空字段：没填过的作业不该被写出一行 <c>"due_date": null</c>，
    ///     否则旧数据一保存就凭空多出一堆空字段。填过的要能原样读回来。
    /// </summary>
    [Fact]
    public void DueDate_IsOmittedWhenUnsetAndSurvivesReopen()
    {
        var harness = CreateHarness();
        var without = harness.Board.Add(new Note { Content = "没填" });
        var with = harness.Board.Add(new Note
        {
            Content = "填了",
            // 落到一个固定的过去日期：两次保存各自一个日期目录，才能分开断言文件内容。
            CreatedAt = LocalTime(2020, 3, 2),
            DueDate = new DateOnly(2026, 3, 2)
        });

        var filled = Path.Combine(BoardRoot, "2020", "03", "02", NoteFileFormat.GetFileName(with));
        var todayFile = Path.Combine(
            BoardRoot,
            Today.Year.ToString("D4"),
            Today.Month.ToString("D2"),
            Today.Day.ToString("D2"),
            NoteFileFormat.GetFileName(without));

        Assert.Contains("due=", File.ReadAllText(filled));
        Assert.DoesNotContain("due=", File.ReadAllText(todayFile));

        var reloaded = CreateStore().LoadAll();
        Assert.Null(reloaded.Single(note => note.Id == without.Id).DueDate);
        Assert.Equal(new DateOnly(2026, 3, 2), reloaded.Single(note => note.Id == with.Id).DueDate);
    }

    [Fact]
    public void DueDateFormatter_SaysNearbyDaysInWords()
    {
        var today = new DateOnly(2026, 3, 2);

        Assert.Equal(0, HomeworkDueDateFormatter.DaysLeft(today, today));
        Assert.Equal(7, HomeworkDueDateFormatter.DaysLeft(today.AddDays(7), today));
        Assert.False(HomeworkDueDateFormatter.IsOverdue(today, today));
        Assert.True(HomeworkDueDateFormatter.IsOverdue(today.AddDays(-1), today));

        // 今天 / 明天说人话，再远报日期，过期报天数。
        Assert.Equal(CR.Board_DueDateToday, HomeworkDueDateFormatter.Describe(today, today));
        Assert.Equal(CR.Board_DueDateTomorrow, HomeworkDueDateFormatter.Describe(today.AddDays(1), today));
        Assert.Contains("2026-03-09", HomeworkDueDateFormatter.Describe(today.AddDays(7), today));
        Assert.Equal(
            string.Format(CR.Board_DueDateOverdueFormat, 3),
            HomeworkDueDateFormatter.Describe(today.AddDays(-3), today));
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);
    }
}
