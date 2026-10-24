using Corkboard.Core.Exam;
using Xunit;

namespace Corkboard.Core.Tests;

/// <summary>
///     科目 → 预设时长。用户点名的那几档（语文 150、数学/英语 120、理化生政史地 75、
///     信息技术/通用技术/音乐 60）不能悄悄变，否则向导排出来的时间全错。
/// </summary>
public class ExamDurationPresetsTests
{
    [Theory]
    [InlineData("语文", 150)]
    [InlineData("数学", 120)]
    [InlineData("英语", 120)]
    [InlineData("物理", 75)]
    [InlineData("化学", 75)]
    [InlineData("生物", 75)]
    [InlineData("政治", 75)]
    [InlineData("历史", 75)]
    [InlineData("地理", 75)]
    [InlineData("信息技术", 60)]
    [InlineData("通用技术", 60)]
    [InlineData("音乐", 60)]
    public void Resolve_KnownSubjects_ReturnsPreset(string subject, int expected) =>
        Assert.Equal(expected, ExamDurationPresets.Resolve(subject));

    [Theory]
    [InlineData("  语文  ")]
    [InlineData("CHINESE")]
    [InlineData("English")]
    [InlineData("PHYSICS")]
    public void Resolve_IgnoresCaseAndWhitespace(string subject)
    {
        // 作业板科目是用户自己敲的，写「英语」还是「English」都得认。
        var minutes = ExamDurationPresets.Resolve(subject);
        Assert.Contains(minutes, new[] { 150, 120, 75, 60 });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("班会")]
    [InlineData("社团活动")]
    public void Resolve_UnknownSubjects_FallBackToDefault(string? subject) =>
        Assert.Equal(ExamDurationPresets.FallbackMinutes, ExamDurationPresets.Resolve(subject));

    [Fact]
    public void QuickMinutes_ContainsThePresetValues()
    {
        // 下拉里那几档要覆盖预设表出现过的所有值，否则用户想改回 150 得手输。
        foreach (var minutes in new[] { 150, 120, 75, 60 })
            Assert.Contains(minutes, ExamDurationPresets.QuickMinutes);

        Assert.Contains(30, ExamDurationPresets.QuickMinutes);
    }

    [Theory]
    [InlineData(0, ExamDurationPresets.MinMinutes)]
    [InlineData(-30, ExamDurationPresets.MinMinutes)]
    [InlineData(9999, ExamDurationPresets.MaxMinutes)]
    [InlineData(120, 120)]
    public void Clamp_KeepsValuesInsideRange(int input, int expected) =>
        Assert.Equal(expected, ExamDurationPresets.Clamp(input));

    [Fact]
    public void Describe_UsesLocalizedTemplate() =>
        Assert.Contains("120", ExamDurationPresets.Describe(120));
}

/// <summary>时段由开考时刻推出（不是落盘字段），分界线固定在 12:00 与 18:00。</summary>
public class ExamTimeSlotPolicyTests
{
    [Theory]
    [InlineData(0, 0, ExamTimeSlot.Morning)]
    [InlineData(8, 0, ExamTimeSlot.Morning)]
    [InlineData(11, 59, ExamTimeSlot.Morning)]
    [InlineData(12, 0, ExamTimeSlot.Afternoon)] // 边界：正好 12:00 算下午
    [InlineData(17, 59, ExamTimeSlot.Afternoon)]
    [InlineData(18, 0, ExamTimeSlot.Evening)] // 边界：正好 18:00 算晚上
    [InlineData(23, 30, ExamTimeSlot.Evening)]
    public void Resolve_SplitsAtNoonAndEvening(int hour, int minute, ExamTimeSlot expected) =>
        Assert.Equal(expected, ExamTimeSlotPolicy.Resolve(new TimeOnly(hour, minute)));

    [Fact]
    public void DefaultStart_MatchesTheSlot()
    {
        Assert.Equal(new TimeOnly(8, 0), ExamTimeSlotPolicy.DefaultStart(ExamTimeSlot.Morning));
        Assert.Equal(new TimeOnly(14, 0), ExamTimeSlotPolicy.DefaultStart(ExamTimeSlot.Afternoon));
        Assert.Equal(new TimeOnly(19, 0), ExamTimeSlotPolicy.DefaultStart(ExamTimeSlot.Evening));
    }

    [Fact]
    public void DefaultStart_ResolvesBackToItsOwnSlot()
    {
        // 默认时刻自己必须落在对应时段里，否则「切时段」一按就跳回去。
        foreach (var slot in Enum.GetValues<ExamTimeSlot>())
            Assert.Equal(slot, ExamTimeSlotPolicy.Resolve(ExamTimeSlotPolicy.DefaultStart(slot)));
    }
}

/// <summary>合卷：绑定的科目清单与拼出来的显示名。</summary>
public class ExamPlanSubjectTests
{
    [Fact]
    public void SingleSubject_KeepsItsName()
    {
        var subject = new ExamPlanSubject(["物理"], 75);

        Assert.Equal("物理", subject.DisplayName);
        Assert.False(subject.IsCombined);
        Assert.Equal(75, subject.DurationMinutes);
    }

    [Fact]
    public void CombinedSubjects_JoinWithSeparator()
    {
        var subject = new ExamPlanSubject(["物理", "化学", "生物"], 150);

        Assert.Equal("物理 · 化学 · 生物", subject.DisplayName);
        Assert.True(subject.IsCombined);
        Assert.Equal(3, subject.Subjects.Count);
    }

    [Fact]
    public void DuplicateAndBlankSubjects_AreDropped()
    {
        var subject = new ExamPlanSubject(["物理", " 物理 ", "", "  ", "化学"], 75);

        Assert.Equal(2, subject.Subjects.Count);
        Assert.Equal("物理 · 化学", subject.DisplayName);
    }

    [Fact]
    public void Duration_IsClampedIntoTheLegalRange()
    {
        Assert.Equal(ExamDurationPresets.MinMinutes, new ExamPlanSubject(["语文"], 1).DurationMinutes);
        Assert.Equal(ExamDurationPresets.MaxMinutes, new ExamPlanSubject(["语文"], 99999).DurationMinutes);
    }

    [Fact]
    public void EmptySubjectList_ProducesEmptyDisplayName()
    {
        var subject = new ExamPlanSubject([], 75);

        Assert.Empty(subject.Subjects);
        Assert.Equal(string.Empty, subject.DisplayName);
        Assert.False(subject.IsCombined);
    }
}

/// <summary>
///     排布引擎：科目序列 + 一天的作息 + 第一天 → 一张具体的考试表。
///     <para>
///         这是快速布置唯一算时间的地方（向导的对照表、模板套用都走它），
///         所以每条规则都单独钉住：一天几场、接上一场怎么算、跨天、冲突判定。
///     </para>
/// </summary>
public class ExamSchedulePlannerTests
{
    private static readonly DateOnly Day1 = new(2026, 6, 7);

    private static ExamPlanSubject Subject(string name, int minutes) => new([name], minutes);

    /// <summary>两场一天：08:00 与 14:00，都固定时刻。</summary>
    private static List<ExamPlanSlot> TwoPerDay() =>
    [
        new(new TimeOnly(8, 0)),
        new(new TimeOnly(14, 0))
    ];

    [Fact]
    public void Plan_FillsOneDayThenMovesOn()
    {
        // 一天两场 × 三个科目 → 第一天两场、第二天一场。
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 150), Subject("数学", 120), Subject("英语", 120)],
            TwoPerDay(),
            Day1);

        Assert.Equal(3, plan.Entries.Count);
        Assert.Equal(2, plan.DayCount);
        Assert.Equal(Day1, plan.Entries[0].Date);
        Assert.Equal(Day1, plan.Entries[1].Date);
        Assert.Equal(Day1.AddDays(1), plan.Entries[2].Date);
        Assert.False(plan.HasConflicts);
    }

    [Fact]
    public void Plan_ComputesEndFromDuration()
    {
        var plan = ExamSchedulePlanner.Plan([Subject("语文", 150)], TwoPerDay(), Day1);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(new TimeOnly(8, 0), entry.Start);
        Assert.Equal(new TimeOnly(10, 30), entry.End); // 8:00 + 150 分钟
        Assert.Equal(TimeSpan.FromMinutes(150), entry.Duration);
    }

    [Fact]
    public void Plan_AfterPreviousMode_AddsTheBreak()
    {
        // 上午第一场 08:00 考 150 分钟 → 10:30 收卷；第二场接上一场、歇 20 分钟 → 10:50。
        List<ExamPlanSlot> slots =
        [
            new(new TimeOnly(8, 0)),
            new(new TimeOnly(0, 0), ExamSlotStartMode.AfterPrevious, 20)
        ];

        var plan = ExamSchedulePlanner.Plan([Subject("语文", 150), Subject("数学", 120)], slots, Day1);

        Assert.Equal(new TimeOnly(10, 50), plan.Entries[1].Start);
        Assert.Equal(new TimeOnly(12, 50), plan.Entries[1].End);
        Assert.False(plan.HasConflicts);
    }

    [Fact]
    public void Plan_FirstSlotOfTheDay_AlwaysUsesItsFixedTime()
    {
        // 第 1 场没有「上一场」可接：哪怕选了「接上一场」，也只能按固定时刻开考。
        List<ExamPlanSlot> slots =
        [
            new(new TimeOnly(9, 0), ExamSlotStartMode.AfterPrevious, 30)
        ];

        var plan = ExamSchedulePlanner.Plan([Subject("语文", 150)], slots, Day1);

        Assert.Equal(new TimeOnly(9, 0), Assert.Single(plan.Entries).Start);
    }

    [Fact]
    public void Plan_FixedSlotEarlierThanPreviousEnd_IsFlaggedAsOverlap()
    {
        // 08:00 考 150 分钟到 10:30，第二场却写死 10:00 开考 → 撞了。
        List<ExamPlanSlot> slots =
        [
            new(new TimeOnly(8, 0)),
            new(new TimeOnly(10, 0))
        ];

        var plan = ExamSchedulePlanner.Plan([Subject("语文", 150), Subject("数学", 120)], slots, Day1);

        Assert.True(plan.HasConflicts);
        Assert.Equal(1, plan.ConflictCount);
        Assert.False(string.IsNullOrEmpty(plan.Entries[1].ConflictReason));
    }

    [Fact]
    public void Plan_ConflictsDoNotBlockThePlan()
    {
        // 撞了也要排出来、交给用户在对照表里决定——这里守的是「只标红不吞掉」。
        List<ExamPlanSlot> slots =
        [
            new(new TimeOnly(8, 0)),
            new(new TimeOnly(8, 0))
        ];

        var plan = ExamSchedulePlanner.Plan([Subject("语文", 150), Subject("数学", 120)], slots, Day1);

        Assert.Equal(2, plan.Entries.Count);
        Assert.True(plan.HasConflicts);
    }

    [Fact]
    public void Plan_RunningPastMidnight_IsClampedAndFlagged()
    {
        // 23:00 开考 150 分钟会跨到第二天：收卷夹成 23:59 并标冲突。
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 150)],
            [new ExamPlanSlot(new TimeOnly(23, 0))],
            Day1);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(new TimeOnly(23, 59), entry.End);
        Assert.True(entry.HasConflict);
    }

    [Fact]
    public void Plan_WithNoSubjects_IsEmpty()
    {
        var plan = ExamSchedulePlanner.Plan([], TwoPerDay(), Day1);

        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.Entries);
        Assert.Equal(0, plan.DayCount);
        Assert.Null(plan.FirstDay);
    }

    [Fact]
    public void Plan_WithNoSlots_IsEmpty()
    {
        // 作息一行都没有就没法排——不能悄悄排成「同一天无限连排」。
        var plan = ExamSchedulePlanner.Plan([Subject("语文", 150)], [], Day1);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Plan_NullArguments_AreTreatedAsEmpty()
    {
        Assert.True(ExamSchedulePlanner.Plan(null, null, Day1).IsEmpty);
    }

    [Fact]
    public void Plan_GroupsByDayInOrder()
    {
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 150), Subject("数学", 120), Subject("英语", 120)],
            TwoPerDay(),
            Day1);

        var groups = plan.GroupByDay().ToList();

        Assert.Equal(2, groups.Count);
        Assert.Equal(Day1, groups[0].Key);
        Assert.Equal(2, groups[0].Count());
        Assert.Equal(Day1.AddDays(1), groups[1].Key);
    }

    [Fact]
    public void Plan_IndexInDay_StartsAtOnePerDay()
    {
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 150), Subject("数学", 120), Subject("英语", 120)],
            TwoPerDay(),
            Day1);

        Assert.Equal([1, 2, 1], plan.Entries.Select(entry => entry.IndexInDay));
    }

    [Fact]
    public void Plan_CombinedSubject_CarriesSubjectKeys()
    {
        ExamPlanSubject combined = new(["物理", "化学", "生物"], 150);

        var plan = ExamSchedulePlanner.Plan([combined], TwoPerDay(), Day1);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal("物理 · 化学 · 生物", entry.Subject);
        Assert.Equal(3, entry.SubjectKeys.Count);
    }

    [Fact]
    public void Plan_SingleSubject_DoesNotCarrySubjectKeys()
    {
        // 单科的 Subject 自己就是科目名，不再存一份可能不一致的清单。
        var plan = ExamSchedulePlanner.Plan([Subject("语文", 150)], TwoPerDay(), Day1);

        Assert.Empty(Assert.Single(plan.Entries).SubjectKeys);
        Assert.Null(Assert.Single(plan.ToEntries()).SubjectKeys);
    }

    [Fact]
    public void Plan_EntriesAreSortedByDateThenStart()
    {
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 150), Subject("数学", 120), Subject("英语", 120), Subject("物理", 75)],
            TwoPerDay(),
            Day1);

        var ordered = plan.Entries.ToList();
        var expected = ordered.OrderBy(entry => entry.Date).ThenBy(entry => entry.Start).ToList();
        Assert.Equal(expected, ordered);
    }

    [Fact]
    public void Plan_ToEntries_ProducesFreshIds()
    {
        var plan = ExamSchedulePlanner.Plan([Subject("语文", 150)], TwoPerDay(), Day1);

        var entries = plan.ToEntries();

        Assert.NotEqual(Guid.Empty, Assert.Single(entries).Id);
        // 转两次要是两份不同的对象（对照表里改草稿不能影响已落盘的）。
        Assert.NotEqual(plan.ToEntries()[0].Id, entries[0].Id);
    }

    [Fact]
    public void PlanAfter_FlagsClashesWithExistingExams()
    {
        // 看板上已经有一场 08:00–10:00 的语文，再排一场 08:00 起的就撞了。
        ExamEntry existing = new()
        {
            Subject = "语文",
            Date = Day1,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(10, 0)
        };

        var plan = ExamSchedulePlanner.PlanAfter([Subject("数学", 120)], TwoPerDay(), Day1, [existing]);

        Assert.True(plan.HasConflicts);
        Assert.Equal(1, plan.ConflictCount);
    }

    [Fact]
    public void PlanAfter_NonClashingExams_StayClean()
    {
        ExamEntry existing = new()
        {
            Subject = "语文",
            Date = Day1,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(10, 0)
        };

        // 下午 14:00 那场跟上午的语文不撞。
        List<ExamPlanSlot> afternoon = [new(new TimeOnly(14, 0))];
        var plan = ExamSchedulePlanner.PlanAfter([Subject("数学", 120)], afternoon, Day1, [existing]);

        Assert.False(plan.HasConflicts);
    }

    [Fact]
    public void PlanAfter_WithoutExisting_EqualsPlainPlan()
    {
        var plain = ExamSchedulePlanner.Plan([Subject("语文", 150)], TwoPerDay(), Day1);
        var after = ExamSchedulePlanner.PlanAfter([Subject("语文", 150)], TwoPerDay(), Day1, null);

        Assert.Equal(plain.Entries.Count, after.Entries.Count);
        Assert.False(after.HasConflicts);
    }

    [Fact]
    public void Plan_SameStartOnConsecutiveDays_IsNotAConflict()
    {
        // 跨天之后「上一场」要清空，否则第二天的 08:00 会被当成撞了。
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 150), Subject("数学", 120), Subject("英语", 120)],
            [new ExamPlanSlot(new TimeOnly(8, 0))],
            Day1);

        Assert.False(plan.HasConflicts);
        Assert.Equal(3, plan.DayCount);
    }
}

/// <summary>作息预设：用户点一下就该拿到一天一场 / 两场 / 四场 / 五场。</summary>
public class ExamPlanSlotPresetsTests
{
    [Fact]
    public void Presets_HaveExpectedSlotCounts()
    {
        Assert.Single(ExamPlanSlotPresets.SingleMorning());
        Assert.Equal(2, ExamPlanSlotPresets.TwoPerDay().Count);
        Assert.Equal(4, ExamPlanSlotPresets.FourPerDay().Count);
        Assert.Equal(5, ExamPlanSlotPresets.FivePerDay().Count);
        Assert.Equal(4, ExamPlanSlotPresets.LongMorningPlusTwo().Count);
    }

    [Fact]
    public void Presets_FirstSlotIsAlwaysFixed()
    {
        foreach (var preset in new[]
                 {
                     ExamPlanSlotPresets.SingleMorning(),
                     ExamPlanSlotPresets.TwoPerDay(),
                     ExamPlanSlotPresets.FourPerDay(),
                     ExamPlanSlotPresets.FivePerDay(),
                     ExamPlanSlotPresets.LongMorningPlusTwo()
                 })
            Assert.Equal(ExamSlotStartMode.Fixed, preset[0].StartMode);
    }

    [Fact]
    public void Presets_AreOrderedByStartTime()
    {
        foreach (var preset in new[]
                 {
                     ExamPlanSlotPresets.TwoPerDay(),
                     ExamPlanSlotPresets.FourPerDay(),
                     ExamPlanSlotPresets.FivePerDay()
                 })
        {
            var times = preset.Select(slot => slot.FixedStart).ToList();
            Assert.Equal(times.OrderBy(time => time), times);
        }
    }

    [Fact]
    public void Materialize_ReturnsIndependentCopies()
    {
        var preset = ExamPlanSlotPresets.TwoPerDay();
        var copies = ExamPlanSlotPresets.Materialize(preset);

        copies[0].FixedStart = new TimeOnly(6, 30);

        // 改了副本不能反过来动到预设本身（预设是共享的静态数据）。
        Assert.Equal(new TimeOnly(8, 0), preset[0].FixedStart);
        Assert.NotSame(preset[0], copies[0]);
    }

    [Fact]
    public void ClampBreak_KeepsBreakInsideRange()
    {
        Assert.Equal(ExamPlanSlot.MinBreakMinutes, ExamPlanSlot.ClampBreak(-10));
        Assert.Equal(ExamPlanSlot.MaxBreakMinutes, ExamPlanSlot.ClampBreak(9999));
        Assert.Equal(30, ExamPlanSlot.ClampBreak(30));
    }
}

/// <summary>合卷科目清单的落盘形态：单科不存、老数据兼容。</summary>
public class ExamSubjectKeysTests
{
    [Fact]
    public void Normalize_WithLessThanTwoKeys_ReturnsNull()
    {
        Assert.Null(ExamEntry.NormalizeSubjectKeys(null));
        Assert.Null(ExamEntry.NormalizeSubjectKeys([]));
        Assert.Null(ExamEntry.NormalizeSubjectKeys(["物理"]));
    }

    [Fact]
    public void Normalize_WithTwoOrMore_KeepsThemInOrder()
    {
        var keys = ExamEntry.NormalizeSubjectKeys(["物理", "化学"]);

        Assert.NotNull(keys);
        Assert.Equal(["物理", "化学"], keys);
    }

    [Fact]
    public void Normalize_DropsBlanksAndDuplicates()
    {
        var keys = ExamEntry.NormalizeSubjectKeys(["物理", " 物理 ", "", null, "化学"]);

        Assert.Equal(["物理", "化学"], keys);
    }

    [Fact]
    public void ResolveSubjectKeys_FallsBackToSubjectForSingleExams()
    {
        ExamEntry single = new() { Subject = "语文" };
        Assert.Equal(["语文"], single.ResolveSubjectKeys);

        ExamEntry combined = new() { Subject = "物理 · 化学", SubjectKeys = ["物理", "化学"] };
        Assert.Equal(["物理", "化学"], combined.ResolveSubjectKeys);
    }

    [Fact]
    public void Clone_CopiesSubjectKeysIndependently()
    {
        ExamEntry original = new() { Subject = "物理 · 化学", SubjectKeys = ["物理", "化学"] };

        var clone = original.Clone();
        clone.SubjectKeys!.Add("生物");

        Assert.Equal(2, original.SubjectKeys!.Count);
    }

    [Fact]
    public void CopyFrom_CopiesSubjectKeys()
    {
        ExamEntry source = new() { Subject = "物理 · 化学", SubjectKeys = ["物理", "化学"] };
        ExamEntry target = new() { Subject = "旧" };

        target.CopyFrom(source);

        Assert.Equal(["物理", "化学"], target.SubjectKeys);
    }

    [Fact]
    public void CopyFrom_WithSingleSubject_ClearsSubjectKeys()
    {
        // 从合卷改回单科时，旧清单必须跟着清掉，否则数据里留着对不上的科目。
        ExamEntry source = new() { Subject = "语文" };
        ExamEntry target = new() { Subject = "物理 · 化学", SubjectKeys = ["物理", "化学"] };

        target.CopyFrom(source);

        Assert.Null(target.SubjectKeys);
    }
}

/// <summary>
///     作息只有三个数（一天几场 / 第一场几点 / 两场之间歇多久）时摊出来的场次，
///     以及「上面那场拖堂了、下面那场跟着顺延」的链式重算。
/// </summary>
public class ExamScheduleCascadeTests
{
    private static readonly DateOnly Day1 = new(2026, 6, 7);

    private static ExamPlanSubject Subject(string name, int minutes) => new([name], minutes);

    [Fact]
    public void ForCount_FirstSlotIsFixedAndTheRestFollowWithTheBreak()
    {
        var slots = ExamPlanSlotPresets.ForCount(3, new TimeOnly(8, 0), 15);

        Assert.Equal(3, slots.Count);
        Assert.Equal(ExamSlotStartMode.Fixed, slots[0].StartMode);
        Assert.Equal(new TimeOnly(8, 0), slots[0].FixedStart);

        foreach (var slot in slots.Skip(1))
        {
            Assert.Equal(ExamSlotStartMode.AfterPrevious, slot.StartMode);
            Assert.Equal(15, slot.BreakMinutes);
        }
    }

    [Fact]
    public void ForCount_ClampsNonPositiveCountToOne()
    {
        Assert.Single(ExamPlanSlotPresets.ForCount(0, new TimeOnly(8, 0), 20));
        Assert.Single(ExamPlanSlotPresets.ForCount(-3, new TimeOnly(8, 0), 20));
    }

    [Fact]
    public void Plan_ForCountScheduleChainsTheWholeDay()
    {
        // 08:00 语文 150 → 10:30，歇 15 → 10:45 数学 120 → 12:45，歇 15 → 13:00 英语 120。
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 150), Subject("数学", 120), Subject("英语", 120)],
            ExamPlanSlotPresets.ForCount(3, new TimeOnly(8, 0), 15),
            Day1);

        Assert.Equal(1, plan.DayCount);
        Assert.Equal(new TimeOnly(8, 0), plan.Entries[0].Start);
        Assert.Equal(new TimeOnly(10, 45), plan.Entries[1].Start);
        Assert.Equal(new TimeOnly(13, 0), plan.Entries[2].Start);
        Assert.False(plan.HasConflicts);
    }

    [Fact]
    public void Cascade_ShiftsLaterSlotsForwardWhenAnEarlierOneRunsLate()
    {
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 120), Subject("数学", 120), Subject("英语", 120)],
            ExamPlanSlotPresets.ForCount(3, new TimeOnly(8, 0), 20),
            Day1);

        var entries = plan.Entries.ToList();

        // 第一场从 120 分钟拉长到 180 分钟（8:00 收卷变 11:00），后面两场该整体后移 60 分钟。
        var beforeSecond = entries[1].Start;
        var beforeThird = entries[2].Start;

        entries[0].DurationMinutes = 180;
        ExamSchedulePlanner.Cascade(entries, 0);

        Assert.Equal(beforeSecond.Add(TimeSpan.FromMinutes(60)), entries[1].Start);
        Assert.Equal(beforeThird.Add(TimeSpan.FromMinutes(60)), entries[2].Start);
    }

    [Fact]
    public void Cascade_StopsAtTheNextDay()
    {
        // 一天一场，两天：改第一天的场次不该动到第二天。
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 120), Subject("数学", 120)],
            [new ExamPlanSlot(new TimeOnly(8, 0))],
            Day1);

        var entries = plan.Entries.ToList();
        var secondDayStart = entries[1].Start;

        entries[0].DurationMinutes = 300;
        ExamSchedulePlanner.Cascade(entries, 0);

        Assert.Equal(Day1.AddDays(1), entries[1].Date);
        Assert.Equal(secondDayStart, entries[1].Start);
    }

    [Fact]
    public void SyncBreak_RecordsTheGapAgainstThePreviousExam()
    {
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 120), Subject("数学", 120)],
            ExamPlanSlotPresets.ForCount(2, new TimeOnly(8, 0), 20),
            Day1);

        var entries = plan.Entries.ToList();

        // 用户手工把第二场从 10:20 拖到 11:00 → 间隔变成 60 分钟。
        entries[1].Start = new TimeOnly(11, 0);
        ExamSchedulePlanner.SyncBreak(entries, 1);

        Assert.Equal(60, entries[1].BreakMinutes);
    }

    [Fact]
    public void SyncBreak_OnTheFirstExamOfTheDay_ResetsToTheDefault()
    {
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 120), Subject("数学", 120)],
            ExamPlanSlotPresets.ForCount(2, new TimeOnly(8, 0), 45),
            Day1);

        var entries = plan.Entries.ToList();
        ExamSchedulePlanner.SyncBreak(entries, 0);

        Assert.Equal(ExamPlanSlot.DefaultBreakMinutes, entries[0].BreakMinutes);
    }

    [Fact]
    public void SyncBreak_ThenCascade_PushesTheFollowingExams()
    {
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 120), Subject("数学", 120), Subject("英语", 120)],
            ExamPlanSlotPresets.ForCount(3, new TimeOnly(8, 0), 20),
            Day1);

        var entries = plan.Entries.ToList();

        // 第二场手工拖到 12:00：它自己记 60 分钟间隔，第三场 12:00 + 120 + 20 = 14:20。
        entries[1].Start = new TimeOnly(12, 0);
        ExamSchedulePlanner.SyncBreak(entries, 1);
        ExamSchedulePlanner.Cascade(entries, 1);

        Assert.Equal(new TimeOnly(12, 0), entries[1].Start);
        Assert.Equal(new TimeOnly(14, 20), entries[2].Start);
    }

    [Fact]
    public void Relayout_RepacksTheWholeTableByTheSchedule()
    {
        // 一天两场、08:00 起、歇 20：三场 → 第一天两场、第二天一场。
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 150), Subject("数学", 120), Subject("英语", 120)],
            ExamPlanSlotPresets.ForCount(2, new TimeOnly(8, 0), 20),
            Day1);

        var entries = plan.Entries.ToList();
        ExamSchedulePlanner.Relayout(entries, 2, new TimeOnly(8, 0), 20);

        Assert.Equal(2, entries.Select(entry => entry.Date).Distinct().Count());
        Assert.Equal(new TimeOnly(8, 0), entries[0].Start);
        Assert.Equal(new TimeOnly(10, 50), entries[1].Start); // 8:00 + 150 + 20
        Assert.Equal(Day1.AddDays(1), entries[2].Date);
        Assert.Equal(new TimeOnly(8, 0), entries[2].Start);   // 第二天第一场回到锚点
    }

    [Fact]
    public void Relayout_MovesASubjectToAnotherDayWhenTheOrderChanges()
    {
        // 一天两场、三门课 → 英语排在第二天；把它挪到最前面，它就该落到第一天第一场。
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 120), Subject("数学", 120), Subject("英语", 120)],
            ExamPlanSlotPresets.ForCount(2, new TimeOnly(8, 0), 20),
            Day1);

        var entries = plan.Entries.ToList();
        var english = entries[2];
        Assert.Equal(Day1.AddDays(1), english.Date);

        // 用户点了两次「上移」：列表顺序变成 英语、语文、数学。
        var ordered = new List<ExamPlanEntry> { english, entries[0], entries[1] };
        ExamSchedulePlanner.Relayout(ordered, 2, new TimeOnly(8, 0), 20);

        Assert.Equal(Day1, english.Date);
        Assert.Equal(new TimeOnly(8, 0), english.Start);
        Assert.Equal(1, english.IndexInDay);
    }

    [Fact]
    public void Relayout_WithOneSlotPerDay_PutsEveryExamOnItsOwnDay()
    {
        var plan = ExamSchedulePlanner.Plan(
            [Subject("语文", 120), Subject("数学", 120), Subject("英语", 120)],
            [new ExamPlanSlot(new TimeOnly(9, 0))],
            Day1);

        var entries = plan.Entries.ToList();
        ExamSchedulePlanner.Relayout(entries, 1, new TimeOnly(9, 0), 20);

        for (var i = 0; i < entries.Count; i++)
        {
            Assert.Equal(Day1.AddDays(i), entries[i].Date);
            Assert.Equal(new TimeOnly(9, 0), entries[i].Start);
            Assert.Equal(1, entries[i].IndexInDay);
        }
    }

    [Fact]
    public void Relayout_WithEmptyList_DoesNothing()
    {
        var entries = new List<ExamPlanEntry>();
        ExamSchedulePlanner.Relayout(entries, 2, new TimeOnly(8, 0), 20);
        Assert.Empty(entries);
    }
}
