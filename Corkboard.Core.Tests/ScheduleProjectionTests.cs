using Corkboard.Core.Actions;
using Corkboard.Core.Actions.Schedule;
using Corkboard.Core.Display;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Display;
using Corkboard.Core.Models.SubConfigs.Exam;
using Xunit;

namespace Corkboard.Core.Tests;

/// <summary>
///     日程中心的投影与合并：考试自动放映、看板模式时段、多源合并与按天计数。
///     <para>
///         考试侧<b>不另算一套窗口</b>：用例直接拿 <see cref="ExamAutoShowPolicy.ResolveWindow" /> 的结果
///         和投影条目逐字段比，两处一旦漂移这里就红。
///     </para>
///     <para>
///         日期固定成 2026-06-07（周日）/ 06-08（周一）与 2026-11-06～11-09（周五～周一），
///         不读 <see cref="DateTime.Now" />；断言里出现的「考试看板」「看板模式」是 Core 自己的常量，与机器语言无关。
///     </para>
/// </summary>
public class ScheduleProjectionTests
{
    private static readonly DateOnly Day1 = new(2026, 6, 7);
    private static readonly DateOnly Day2 = new(2026, 6, 8);

    private static ExamEntry Exam(string subject, DateOnly date, int startHour, int startMinute, int endHour,
        int endMinute) => new()
    {
        Subject = subject,
        Date = date,
        StartTime = new TimeOnly(startHour, startMinute),
        EndTime = new TimeOnly(endHour, endMinute)
    };

    private static List<ExamEntry> TwoDayExams() =>
    [
        Exam("语文", Day1, 9, 0, 11, 0),
        Exam("数学", Day2, 9, 0, 10, 0)
    ];

    private static DateTime At(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private static DisplaySettingsConfig DisplaySettings(params DisplaySlot[] slots) => new()
    {
        AutoShow = true,
        Slots = [.. slots]
    };

    private static DisplaySlot Slot(int enterHour, int enterMinute, int exitHour, int exitMinute,
        DisplayDayFlags days = DisplayDayFlags.EveryDay, bool enabled = true) => new()
    {
        Enabled = enabled,
        Days = days,
        EnterHour = enterHour,
        EnterMinute = enterMinute,
        ExitHour = exitHour,
        ExitMinute = exitMinute
    };

    // ── 考试自动放映 ────────────────────────────────────────────────

    /// <summary>投影的起止必须与 <see cref="ExamAutoShowPolicy.ResolveWindow" /> 逐字段一致。</summary>
    [Fact]
    public void ExamProjection_MatchesResolveWindow()
    {
        var exams = TwoDayExams();
        var settings = new ExamAutoShowSettingsConfig
        {
            LeadMinutes = 30,
            CloseDelayMinutes = 0,
            AutoSwitchBoard = true
        };

        var window = ExamAutoShowPolicy.ResolveWindow(exams, settings.LeadMinutes, settings.CloseDelayMinutes);
        Assert.True(window.HasValue);

        var items = ExamScheduleProjection.Project(exams, settings, At(6, 6, 0), At(6, 10, 0));
        var item = Assert.Single(items);

        Assert.Equal(window.Value.Start, item.Start);
        Assert.Equal(window.Value.End, item.End);
        Assert.Equal(ScheduleItemKind.ExamAutoShow, item.Kind);
        Assert.Equal(ExamScheduleProjection.SourceId, item.Id);
        Assert.Equal(ExamScheduleProjection.SettingsPageId, item.SettingsPageId);
        Assert.True(item.IsEnabled);
        Assert.True(item.CanPause);
        Assert.False(item.IsEditable);
        Assert.False(item.IsSuppressed);
        Assert.False(item.IsPointEvent);
        Assert.Null(item.RuleId);
    }

    /// <summary>提前量 / 延迟都由策略夹取，投影不自己算（0 分钟提前量会被夹成 1）。</summary>
    [Fact]
    public void ExamProjection_UsesClampedLeadAndDelay()
    {
        var exams = TwoDayExams();
        var settings = new ExamAutoShowSettingsConfig
        {
            LeadMinutes = 0,
            CloseDelayMinutes = 0,
            AutoSwitchBoard = false
        };

        var item = Assert.Single(ExamScheduleProjection.Project(exams, settings, At(6, 6, 0), At(6, 10, 0)));
        var firstStart = Day1.ToDateTime(new TimeOnly(9, 0));

        Assert.Equal(firstStart.AddMinutes(-ExamAutoShowPolicy.MinLeadMinutes), item.Start);
        Assert.Equal(new[] { BoardTargetIds.ExamBoard }, item.TargetIds);
    }

    /// <summary>标题用科目拼：单场「考试看板 · 语文」。</summary>
    [Fact]
    public void ExamProjection_TitleOfSingleExam()
    {
        var exams = new List<ExamEntry> { Exam("语文", Day1, 9, 0, 11, 0) };
        var settings = new ExamAutoShowSettingsConfig();

        var item = Assert.Single(ExamScheduleProjection.Project(exams, settings, At(6, 6, 0), At(6, 10, 0)));
        Assert.Equal($"{ExamScheduleProjection.TitlePrefix} · 语文", item.Title);
    }

    /// <summary>多场取开考最早的科目，后缀「等 N 场」（输入顺序不影响结果）。</summary>
    [Fact]
    public void ExamProjection_TitleOfMultipleExams_UsesEarliestSubject()
    {
        var exams = new List<ExamEntry>
        {
            Exam("数学", Day2, 9, 0, 10, 0),
            Exam("语文", Day1, 9, 0, 11, 0)
        };
        var settings = new ExamAutoShowSettingsConfig();

        var item = Assert.Single(ExamScheduleProjection.Project(exams, settings, At(6, 6, 0), At(6, 10, 0)));
        Assert.Equal($"{ExamScheduleProjection.TitlePrefix} · 语文 等 2 场", item.Title);
    }

    /// <summary>与查询区间没有交集就不出现；相切也不算（区间左闭右开），条目本身不裁切。</summary>
    [Fact]
    public void ExamProjection_QueryIntersectionAndClipping()
    {
        var exams = TwoDayExams();
        var settings = new ExamAutoShowSettingsConfig();
        var window = ExamAutoShowPolicy.ResolveWindow(exams, settings.LeadMinutes, settings.CloseDelayMinutes)!.Value;

        // 查询窗口落在放映期内部：照样返回完整区间（裁切是 ScheduleMerge 的事）。
        var inside = Assert.Single(ExamScheduleProjection.Project(exams, settings,
            window.Start.AddMinutes(5), window.Start.AddMinutes(10)));
        Assert.Equal(window.Start, inside.Start);
        Assert.Equal(window.End, inside.End);

        Assert.Empty(ExamScheduleProjection.Project(exams, settings, window.End, window.End.AddHours(1)));
        Assert.Empty(ExamScheduleProjection.Project(exams, settings, window.Start.AddHours(-1), window.Start));
        Assert.Empty(ExamScheduleProjection.Project(exams, settings, window.Start, window.Start));
    }

    /// <summary>没有考试 / 空区间 / null 考试集合都安全返回空。</summary>
    [Fact]
    public void ExamProjection_EmptyInputs_ReturnEmpty()
    {
        var settings = new ExamAutoShowSettingsConfig();

        Assert.Empty(ExamScheduleProjection.Project(null, settings, At(6, 6, 0), At(6, 10, 0)));
        Assert.Empty(ExamScheduleProjection.Project([], settings, At(6, 6, 0), At(6, 10, 0)));
        Assert.Empty(ExamScheduleProjection.Project(TwoDayExams(), settings, At(6, 10, 0), At(6, 6, 0)));
        Assert.Throws<ArgumentNullException>(() =>
            ExamScheduleProjection.Project(TwoDayExams(), null!, At(6, 6, 0), At(6, 10, 0)));
    }

    // ── 看板模式时段 ────────────────────────────────────────────────

    /// <summary>跨零点在日界拆成两条：22:00–00:00 + 00:00–02:00，首尾相接不重叠。</summary>
    [Fact]
    public void DisplayProjection_SplitsCrossMidnightSlot()
    {
        var settings = DisplaySettings(Slot(22, 0, 2, 0));

        var items = DisplayScheduleProjection.Project(settings, At(11, 7, 21, 0), At(11, 8, 3, 0));

        Assert.Equal(2, items.Count);
        Assert.Equal(At(11, 7, 22, 0), items[0].Start);
        Assert.Equal(At(11, 8, 0, 0), items[0].End);
        Assert.Equal(items[0].End, items[1].Start);
        Assert.Equal(At(11, 8, 2, 0), items[1].End);
        Assert.Equal(ScheduleItemKind.DisplayAutoShow, items[0].Kind);
        Assert.Equal($"{DisplayScheduleProjection.SourceId}-0-2026-11-07", items[0].Id);
        Assert.Equal($"{DisplayScheduleProjection.SourceId}-0-2026-11-08", items[1].Id);
        Assert.Equal(DisplayScheduleProjection.Title, items[0].Title);
        Assert.Equal(DisplayScheduleProjection.SettingsPageId, items[0].SettingsPageId);
        Assert.Equal(new[] { BoardTargetIds.DisplayBoard }, items[0].TargetIds);
    }

    /// <summary>普通时段一天一条；零长度时段（进=出）与停用的时段不产出。</summary>
    [Fact]
    public void DisplayProjection_SkipsEmptyAndDisabledSlots()
    {
        var settings = DisplaySettings(
            Slot(19, 0, 19, 0),
            Slot(19, 0, 21, 0, enabled: false),
            Slot(19, 0, 21, 0));

        var items = DisplayScheduleProjection.Project(settings, At(11, 7, 0, 0), At(11, 8, 0, 0));

        var item = Assert.Single(items);
        Assert.Equal(At(11, 7, 19, 0), item.Start);
        Assert.Equal(At(11, 7, 21, 0), item.End);
        Assert.True(item.IsEnabled);
    }

    /// <summary>只按开始日的星期判定（与 <see cref="DisplaySchedulePolicy" /> 同口径）。</summary>
    [Fact]
    public void DisplayProjection_RespectsDayFlags()
    {
        // 2026-11-09 是周一，11-10 是周二。
        var settings = DisplaySettings(Slot(19, 0, 21, 0, DisplayDayFlags.Monday));

        Assert.Empty(DisplayScheduleProjection.Project(settings, At(11, 10, 0, 0), At(11, 11, 0, 0)));

        var monday = Assert.Single(DisplayScheduleProjection.Project(settings, At(11, 9, 0, 0), At(11, 10, 0, 0)));
        Assert.Equal(At(11, 9, 19, 0), monday.Start);
    }

    /// <summary>总开关关掉时条目照样产出，但标记为未启用（界面显示成灰色）。</summary>
    [Fact]
    public void DisplayProjection_AutoShowOff_StillProjectsDisabledItems()
    {
        var settings = DisplaySettings(Slot(19, 0, 21, 0));
        settings.AutoShow = false;

        var item = Assert.Single(DisplayScheduleProjection.Project(settings, At(11, 7, 0, 0), At(11, 8, 0, 0)));
        Assert.False(item.IsEnabled);
    }

    // ── 合并与计数 ──────────────────────────────────────────────────

    private static ScheduleItem Item(string id, ScheduleItemKind kind, DateTime start, DateTime? end) => new()
    {
        Id = id,
        Kind = kind,
        Title = id,
        Start = start,
        End = end
    };

    /// <summary>区间条目被裁到查询窗口内；完全在窗口外的丢掉。</summary>
    [Fact]
    public void Merge_ClipsIntervalsToWindow()
    {
        var merged = ScheduleMerge.Merge(
        [
            new[]
            {
                Item("display", ScheduleItemKind.DisplayAutoShow, At(11, 7, 19, 0), At(11, 7, 21, 30)),
                Item("outside", ScheduleItemKind.DisplayAutoShow, At(11, 7, 22, 0), At(11, 7, 23, 0))
            }
        ], At(11, 7, 20, 0), At(11, 7, 21, 0));

        var item = Assert.Single(merged);
        Assert.Equal("display", item.Id);
        Assert.Equal(At(11, 7, 20, 0), item.Start);
        Assert.Equal(At(11, 7, 21, 0), item.End);
    }

    /// <summary>点事件只保留落在 <c>[from, to)</c> 里的；上限那一刻不算。</summary>
    [Fact]
    public void Merge_KeepsPointEventsInsideWindowOnly()
    {
        var merged = ScheduleMerge.Merge(
        [
            new[]
            {
                Item("at-from", ScheduleItemKind.CustomAction, At(11, 7, 20, 0), null),
                Item("inside", ScheduleItemKind.CustomAction, At(11, 7, 20, 30), null),
                Item("at-to", ScheduleItemKind.CustomAction, At(11, 7, 21, 0), null),
                Item("before", ScheduleItemKind.CustomAction, At(11, 7, 19, 0), null)
            }
        ], At(11, 7, 20, 0), At(11, 7, 21, 0));

        Assert.Equal(new[] { "at-from", "inside" }, merged.Select(item => item.Id).ToArray());
        Assert.All(merged, item => Assert.True(item.IsPointEvent));
    }

    /// <summary>排序口径固定：开始时刻 → 类型数值 → Id（Ordinal），与数据源顺序无关。</summary>
    [Fact]
    public void Merge_SortsByStartThenKindThenId()
    {
        var merged = ScheduleMerge.Merge(
        [
            new[]
            {
                Item("exam-b", ScheduleItemKind.ExamAutoShow, At(11, 7, 10, 0), null),
                Item("display-a", ScheduleItemKind.DisplayAutoShow, At(11, 7, 10, 0), null),
                Item("custom-z", ScheduleItemKind.CustomAction, At(11, 7, 10, 0), null),
                Item("early", ScheduleItemKind.DisplayAutoShow, At(11, 7, 9, 0), null)
            }
        ], At(11, 7, 0, 0), At(11, 8, 0, 0));

        Assert.Equal(new[] { "early", "custom-z", "exam-b", "display-a" }, merged.Select(item => item.Id).ToArray());
    }

    /// <summary>按天计数：点事件按开始日、区间按与当天有没有交集（跨夜两天各算一次）。</summary>
    [Fact]
    public void CountOn_CountsPointsAndIntervalsPerDay()
    {
        var items = new[]
        {
            Item("point", ScheduleItemKind.CustomAction, At(11, 7, 10, 0), null),
            Item("cross", ScheduleItemKind.DisplayAutoShow, At(11, 7, 22, 0), At(11, 8, 2, 0)),
            Item("to-midnight", ScheduleItemKind.DisplayAutoShow, At(11, 7, 23, 0), At(11, 8, 0, 0))
        };

        Assert.Equal(3, ScheduleMerge.CountOn(items, new DateOnly(2026, 11, 7)));
        Assert.Equal(1, ScheduleMerge.CountOn(items, new DateOnly(2026, 11, 8)));
        Assert.Equal(0, ScheduleMerge.CountOn(items, new DateOnly(2026, 11, 9)));
        Assert.Equal(0, ScheduleMerge.CountOn(null, new DateOnly(2026, 11, 7)));
    }

    /// <summary>空源、反区间都返回空列表。</summary>
    [Fact]
    public void Merge_EmptyInputs_ReturnEmpty()
    {
        Assert.Empty(ScheduleMerge.Merge(null, At(11, 7, 0, 0), At(11, 8, 0, 0)));
        Assert.Empty(ScheduleMerge.Merge([Array.Empty<ScheduleItem>()], At(11, 7, 0, 0), At(11, 8, 0, 0)));
        Assert.Empty(ScheduleMerge.Merge(
            [new[] { Item("x", ScheduleItemKind.CustomAction, At(11, 7, 10, 0), null) }],
            At(11, 8, 0, 0), At(11, 7, 0, 0)));
    }
}
