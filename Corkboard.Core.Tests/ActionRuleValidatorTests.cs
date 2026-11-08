using Corkboard.Core.Actions;
using Xunit;

namespace Corkboard.Core.Tests;

/// <summary>
///     规则校验：错（拦保存）与警告（可保存）的边界。
///     <para>
///         校验器是纯函数，「现在」由参数传进来；<see cref="ActionValidationError.Message" /> 是
///         <b>资源键</b>而不是中文文案，所以这里的断言与机器语言无关。
///     </para>
/// </summary>
public class ActionRuleValidatorTests
{
    private static readonly DateOnly Saturday = new(2026, 11, 7);
    private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Unspecified);

    private static ActionRule Rule(ActionTrigger trigger, ActionCondition? condition = null,
        params ActionStep[] steps) => new()
    {
        Id = Guid.NewGuid(),
        Name = "测试规则",
        Enabled = true,
        Trigger = trigger,
        Condition = condition,
        Steps = steps.Length > 0
            ? steps
            : [new ActionStep { TargetId = BoardTargetIds.Homework, Operation = BoardOperation.Show }]
    };

    private static ActionStep Step(string targetId, BoardOperation operation) =>
        new() { TargetId = targetId, Operation = operation };

    private static ActionValidationMessage[] Codes(ActionRule rule, DateTime? now = null) =>
        ActionRuleValidator.Validate(rule, now).Select(error => error.Code).ToArray();

    /// <summary>完全合规的规则一条问题都没有。</summary>
    [Fact]
    public void ValidRule_HasNoError()
    {
        var rule = Rule(new ActionTrigger
        {
            Kind = ActionTriggerKind.Weekly,
            Days = ActionDayFlags.Monday | ActionDayFlags.Friday,
            Time = new TimeOnly(19, 0)
        }, new ActionCondition
        {
            Kind = ActionConditionKind.DateRange,
            Start = Saturday,
            End = Saturday.AddDays(7)
        });

        Assert.Empty(ActionRuleValidator.Validate(rule, Now));
    }

    /// <summary>没有步骤是错误。</summary>
    [Fact]
    public void NoStep_IsError()
    {
        var rule = new ActionRule
        {
            Id = Guid.NewGuid(),
            Trigger = new ActionTrigger { Kind = ActionTriggerKind.Daily, Time = new TimeOnly(8, 0) },
            Steps = []
        };

        var errors = ActionRuleValidator.Validate(rule, Now);
        var error = Assert.Single(errors);
        Assert.Equal(ActionValidationMessage.NoStep, error.Code);
        Assert.Equal(ActionValidationSeverity.Error, error.Severity);
        Assert.False(error.IsWarning);
        Assert.Equal("Action_Validation_NoStep", error.Message);
    }

    /// <summary>每周规则一天都没选是错误。</summary>
    [Fact]
    public void Weekly_WithoutDay_IsError()
    {
        var rule = Rule(new ActionTrigger
        {
            Kind = ActionTriggerKind.Weekly,
            Days = ActionDayFlags.None,
            Time = new TimeOnly(8, 0)
        });

        Assert.Equal(new[] { ActionValidationMessage.NoDay }, Codes(rule));
    }

    /// <summary>单次规则没有日期是错误。</summary>
    [Fact]
    public void Once_WithoutDate_IsError()
    {
        var rule = Rule(new ActionTrigger
        {
            Kind = ActionTriggerKind.Once,
            Time = new TimeOnly(8, 0)
        });

        Assert.Equal(new[] { ActionValidationMessage.NoDate }, Codes(rule));
    }

    /// <summary>单次规则的时刻已经过去只是警告（给 <c>now</c> 才判）。</summary>
    [Fact]
    public void Once_InPast_IsWarningOnly()
    {
        var rule = Rule(new ActionTrigger
        {
            Kind = ActionTriggerKind.Once,
            Date = Saturday,
            Time = new TimeOnly(8, 0)
        });

        var errors = ActionRuleValidator.Validate(rule, Now);
        var warning = Assert.Single(errors);
        Assert.Equal(ActionValidationMessage.PastOnce, warning.Code);
        Assert.Equal(ActionValidationSeverity.Warning, warning.Severity);
        Assert.True(warning.IsWarning);
        Assert.Equal("Action_Validation_PastOnce", warning.Message);

        // 不传 now 就不判「已过」：单测与设置页之外的调用方都不该被当前时间影响。
        Assert.Empty(ActionRuleValidator.Validate(rule));
        Assert.Empty(ActionRuleValidator.Validate(rule, Now.AddDays(-1)));
    }

    /// <summary>日期范围起点晚于终点是错误，同一天不算。</summary>
    [Fact]
    public void Condition_ReversedRange_IsError()
    {
        var reversed = Rule(new ActionTrigger { Kind = ActionTriggerKind.Daily, Time = new TimeOnly(8, 0) },
            new ActionCondition
            {
                Kind = ActionConditionKind.DateRange,
                Start = Saturday.AddDays(1),
                End = Saturday
            });
        var sameDay = Rule(new ActionTrigger { Kind = ActionTriggerKind.Daily, Time = new TimeOnly(8, 0) },
            new ActionCondition
            {
                Kind = ActionConditionKind.DateRange,
                Start = Saturday,
                End = Saturday
            });

        Assert.Equal(new[] { ActionValidationMessage.RangeOrder }, Codes(reversed));
        Assert.Empty(ActionRuleValidator.Validate(sameDay, Now));
    }

    /// <summary>同一块看板既 Show 又 Hide 是警告（带目标 Id），不同目标不算冲突。</summary>
    [Fact]
    public void SameTarget_ShowAndHide_IsWarningWithTargetId()
    {
        var conflicting = Rule(
            new ActionTrigger { Kind = ActionTriggerKind.Daily, Time = new TimeOnly(8, 0) },
            null,
            Step(BoardTargetIds.ExamBoard, BoardOperation.Show),
            Step(BoardTargetIds.ExamBoard, BoardOperation.Hide));
        var different = Rule(
            new ActionTrigger { Kind = ActionTriggerKind.Daily, Time = new TimeOnly(8, 0) },
            null,
            Step(BoardTargetIds.ExamBoard, BoardOperation.Show),
            Step(BoardTargetIds.DisplayBoard, BoardOperation.Hide));

        var errors = ActionRuleValidator.Validate(conflicting, Now);
        var warning = Assert.Single(errors);
        Assert.Equal(ActionValidationMessage.Conflict, warning.Code);
        Assert.Equal(ActionValidationSeverity.Warning, warning.Severity);
        Assert.Equal(BoardTargetIds.ExamBoard, warning.TargetId);

        Assert.Empty(ActionRuleValidator.Validate(different, Now));
    }

    /// <summary>问题代码 → 资源键的映射固定（App 层按它查文案）。</summary>
    [Theory]
    [InlineData(ActionValidationMessage.NoStep, "Action_Validation_NoStep")]
    [InlineData(ActionValidationMessage.NoDay, "Action_Validation_NoDay")]
    [InlineData(ActionValidationMessage.NoDate, "Action_Validation_NoDate")]
    [InlineData(ActionValidationMessage.NoTime, "Action_Validation_NoTime")]
    [InlineData(ActionValidationMessage.RangeOrder, "Action_Validation_RangeOrder")]
    [InlineData(ActionValidationMessage.PastOnce, "Action_Validation_PastOnce")]
    [InlineData(ActionValidationMessage.Conflict, "Action_Validation_Conflict")]
    public void ResolveResourceKey_IsStable(ActionValidationMessage message, string expected)
    {
        Assert.Equal(expected, ActionRuleValidator.ResolveResourceKey(message));
    }

    /// <summary>空规则引用直接抛（调用方的 bug，不该被静默吞掉）。</summary>
    [Fact]
    public void NullRule_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ActionRuleValidator.Validate(null!));
    }
}
