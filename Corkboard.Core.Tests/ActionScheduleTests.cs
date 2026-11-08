using Corkboard.Core.Actions;
using Xunit;

namespace Corkboard.Core.Tests;

/// <summary>
///     行动规则的时间判定：「什么时候该发生」。
///     <para>
///         区间口径是<b>左开右闭</b> <c>(after, before]</c>，所以每个用例都要把两端各钉一次：
///         水位线那一刻不算（否则补发会重复执行）、上限那一刻算（否则会漏掉最后一拍）。
///     </para>
///     <para>
///         日期全部写死：2026-11-06 是周五、11-07 周六、11-08 周日、11-09 周一、11-13 周五。
///         用例里没有一处读 <see cref="DateTime.Now" />，CI 跑在哪个时区结论都一样。
///     </para>
/// </summary>
public class ActionScheduleTests
{
    /// <summary>2026 年 11 月某天某时刻（<see cref="DateTimeKind.Unspecified" />，与考试 / 行动域一致）。</summary>
    private static DateTime At(int day, int hour = 0, int minute = 0) =>
        new(2026, 11, day, hour, minute, 0, DateTimeKind.Unspecified);

    private static ActionRule Rule(ActionTrigger trigger, bool enabled = true, ActionCondition? condition = null) => new()
    {
        Id = Guid.NewGuid(),
        Enabled = enabled,
        Trigger = trigger,
        Condition = condition,
        Steps = [new ActionStep { TargetId = BoardTargetIds.Homework, Operation = BoardOperation.Show }]
    };

    private static ActionRule Once(int day, int hour, int minute = 0, bool enabled = true) =>
        Rule(new ActionTrigger
        {
            Kind = ActionTriggerKind.Once,
            Date = new DateOnly(2026, 11, day),
            Time = new TimeOnly(hour, minute)
        }, enabled);

    private static ActionRule Daily(int hour, int minute = 0, bool enabled = true, ActionCondition? condition = null) =>
        Rule(new ActionTrigger
        {
            Kind = ActionTriggerKind.Daily,
            Time = new TimeOnly(hour, minute)
        }, enabled, condition);

    private static ActionRule Weekly(ActionDayFlags days, int hour, int minute = 0, bool enabled = true) =>
        Rule(new ActionTrigger
        {
            Kind = ActionTriggerKind.Weekly,
            Days = days,
            Time = new TimeOnly(hour, minute)
        }, enabled);

    /// <summary>单次规则：水位线那一刻不算，上限那一刻算。</summary>
    [Fact]
    public void Once_RespectsLeftOpenRightClosed()
    {
        var rule = Once(7, 9);

        Assert.Empty(ActionSchedule.OccurrencesOf(rule, At(7, 9), At(7, 10)));
        Assert.Empty(ActionSchedule.OccurrencesOf(rule, At(7, 8), At(7, 8, 59)));

        var hit = Assert.Single(ActionSchedule.OccurrencesOf(rule, At(7, 8), At(7, 9)));
        Assert.Equal(At(7, 9), hit);
    }

    /// <summary>每天规则：区间里每一天各一次，右端那天超过时刻就不算。</summary>
    [Fact]
    public void Daily_ReturnsOneOccurrencePerDay()
    {
        var rule = Daily(8);
        var occurrences = ActionSchedule.OccurrencesOf(rule, At(6), At(9));

        Assert.Equal(3, occurrences.Count);
        Assert.Equal(At(6, 8), occurrences[0]);
        Assert.Equal(At(7, 8), occurrences[1]);
        Assert.Equal(At(8, 8), occurrences[2]);
    }

    /// <summary>每周规则跨周：周五 → 周一 → 周五，三天都命中。</summary>
    [Fact]
    public void Weekly_SpanningTwoWeeks_HitsSelectedDays()
    {
        var rule = Weekly(ActionDayFlags.Monday | ActionDayFlags.Friday, 7, 30);
        var occurrences = ActionSchedule.OccurrencesOf(rule, At(5), At(14));

        Assert.Equal(new[] { At(6, 7, 30), At(9, 7, 30), At(13, 7, 30) }, occurrences);
    }

    /// <summary>每周规则一天都没选（脏配置）不产生发生点。</summary>
    [Fact]
    public void Weekly_NoDay_ProducesNothing()
    {
        var rule = Weekly(ActionDayFlags.None, 8);
        Assert.Empty(ActionSchedule.OccurrencesOf(rule, At(6), At(13)));
    }

    /// <summary>多条规则：跳过停用的、结果按时刻升序（同一时刻保持传入顺序）。</summary>
    [Fact]
    public void OccurrencesBetween_SkipsDisabledAndSortsByTime()
    {
        var late = Daily(9);
        var early = Daily(8);
        var disabled = Daily(7, enabled: false);

        var occurrences = ActionSchedule.OccurrencesBetween([late, disabled, early], At(7), At(7, 23));

        Assert.Equal(2, occurrences.Count);
        Assert.Equal(At(7, 8), occurrences[0].At);
        Assert.Equal(At(7, 9), occurrences[1].At);
        Assert.Same(early, occurrences[0].Rule);
        Assert.Same(late, occurrences[1].Rule);
    }

    /// <summary>条件的两个端点都能留空：只限末尾、只限开头。</summary>
    [Fact]
    public void Condition_AllowsNullEnds()
    {
        var untilSaturday = Daily(8, condition: new ActionCondition
        {
            Kind = ActionConditionKind.DateRange,
            End = new DateOnly(2026, 11, 7)
        });
        var fromSunday = Daily(8, condition: new ActionCondition
        {
            Kind = ActionConditionKind.DateRange,
            Start = new DateOnly(2026, 11, 8)
        });

        var limited = ActionSchedule.OccurrencesOf(untilSaturday, At(6), At(10));
        Assert.Equal(new[] { At(6, 8), At(7, 8) }, limited);

        var openEnded = ActionSchedule.OccurrencesOf(fromSunday, At(6), At(10));
        Assert.Equal(new[] { At(8, 8), At(9, 8) }, openEnded);
    }

    /// <summary>条件两端都含当日：结束那天本身照样发生。</summary>
    [Fact]
    public void Condition_EndDateIsInclusive()
    {
        var rule = Daily(8, condition: new ActionCondition
        {
            Kind = ActionConditionKind.DateRange,
            Start = new DateOnly(2026, 11, 7),
            End = new DateOnly(2026, 11, 7)
        });

        Assert.Single(ActionSchedule.OccurrencesOf(rule, At(6), At(8)));
    }

    /// <summary>下一个发生点严格晚于给定时刻。</summary>
    [Fact]
    public void NextOccurrence_IsStrictlyAfter()
    {
        var rule = Daily(8);

        Assert.Equal(At(7, 8), ActionSchedule.NextOccurrence(rule, At(7, 7, 59)));
        Assert.Equal(At(8, 8), ActionSchedule.NextOccurrence(rule, At(7, 8)));
        Assert.Equal(At(8, 8), ActionSchedule.NextOccurrence(rule, At(7, 9)));
    }

    /// <summary>每周规则往后找时跳过没选中的日子（周五之后是周一）。</summary>
    [Fact]
    public void NextOccurrence_Weekly_SkipsUnselectedDays()
    {
        var rule = Weekly(ActionDayFlags.Monday, 8);

        Assert.Equal(At(9, 8), ActionSchedule.NextOccurrence(rule, At(6, 9)));
    }

    /// <summary>条件把窗口关死之后没有下一个发生点。</summary>
    [Fact]
    public void NextOccurrence_RespectsConditionEnd()
    {
        var rule = Daily(8, condition: new ActionCondition
        {
            Kind = ActionConditionKind.DateRange,
            End = new DateOnly(2026, 11, 7)
        });

        Assert.Null(ActionSchedule.NextOccurrence(rule, At(7, 9)));
    }

    /// <summary>单次规则过了就没有下一个；没过就是那一个。</summary>
    [Fact]
    public void NextOccurrence_OncePast_ReturnsNull()
    {
        var rule = Once(7, 8);

        Assert.Equal(At(7, 8), ActionSchedule.NextOccurrence(rule, At(7, 7)));
        Assert.Null(ActionSchedule.NextOccurrence(rule, At(7, 9)));
    }

    /// <summary>「这天有没有」只看时间与条件，不看启用状态（过滤留给调用方）。</summary>
    [Fact]
    public void OccursOn_IgnoresEnabled()
    {
        var saturday = Weekly(ActionDayFlags.Saturday, 8, enabled: false);
        var limited = Daily(8, condition: new ActionCondition
        {
            Kind = ActionConditionKind.DateRange,
            Start = new DateOnly(2026, 11, 8)
        });

        Assert.True(ActionSchedule.OccursOn(saturday, new DateOnly(2026, 11, 7)));
        Assert.False(ActionSchedule.OccursOn(saturday, new DateOnly(2026, 11, 8)));
        Assert.False(ActionSchedule.OccursOn(limited, new DateOnly(2026, 11, 7)));
        Assert.True(ActionSchedule.OccursOn(limited, new DateOnly(2026, 11, 8)));
    }

    /// <summary>没有条件、或条件类型不认识，都按满足处理（旧版本不会把行动全吞掉）。</summary>
    [Fact]
    public void IsWithinCondition_NullOrUnknown_IsAlwaysTrue()
    {
        var unlimited = Rule(new ActionTrigger { Kind = ActionTriggerKind.Daily, Time = new TimeOnly(8, 0) });
        var rangeOpen = Daily(8, condition: new ActionCondition { Kind = ActionConditionKind.DateRange });
        var unknownKind = Daily(8, condition: new ActionCondition
        {
            Kind = (ActionConditionKind)99,
            Start = new DateOnly(2026, 11, 9)
        });

        Assert.True(ActionSchedule.IsWithinCondition(unlimited, At(7, 8)));
        Assert.True(ActionSchedule.IsWithinCondition(rangeOpen, At(7, 8)));
        Assert.True(ActionSchedule.IsWithinCondition(unknownKind, At(7, 8)));

        var bounded = Daily(8, condition: new ActionCondition
        {
            Kind = ActionConditionKind.DateRange,
            Start = new DateOnly(2026, 11, 7),
            End = new DateOnly(2026, 11, 7)
        });
        Assert.True(ActionSchedule.IsWithinCondition(bounded, At(7, 0, 1)));
        Assert.True(ActionSchedule.IsWithinCondition(bounded, At(7, 23, 59)));
        Assert.False(ActionSchedule.IsWithinCondition(bounded, At(8, 0)));
    }

    /// <summary>一次查询最多 2000 条，脏配置不会把界面拖死。</summary>
    [Fact]
    public void OccurrencesBetween_TruncatesAtLimit()
    {
        var rule = Daily(8);
        var occurrences = ActionSchedule.OccurrencesBetween(
            [rule],
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));

        Assert.Equal(ActionSchedule.MaxOccurrences, occurrences.Count);
    }

    /// <summary>空输入、反区间都安全返回空列表。</summary>
    [Fact]
    public void OccurrencesBetween_EmptyInputs_ReturnEmpty()
    {
        Assert.Empty(ActionSchedule.OccurrencesBetween(null, At(6), At(7)));
        Assert.Empty(ActionSchedule.OccurrencesBetween([Daily(8)], At(7), At(6)));
        Assert.Empty(ActionSchedule.OccurrencesBetween([Daily(8)], At(7), At(7)));
        Assert.Empty(ActionSchedule.OccurrencesOf(null!, At(6), At(7)));
    }
}
