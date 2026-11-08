namespace Corkboard.Core.Actions;

/// <summary>校验结果里的问题代码。数值冻结，只能末尾追加。</summary>
public enum ActionValidationMessage
{
    /// <summary>一条步骤都没有。</summary>
    NoStep = 0,

    /// <summary>每周规则一天都没选。</summary>
    NoDay = 1,

    /// <summary>单次规则没有日期。</summary>
    NoDate = 2,

    /// <summary>
    ///     单次规则没有时刻。
    ///     <para>
    ///         保留的代码：<see cref="ActionTrigger.Time" /> 是 <see cref="TimeOnly" />（值类型，不能为空），
    ///         JSON 里缺 <c>time</c> 会读成 00:00（本身是合法时刻），因此校验器当前不会产出这一条；
    ///         编辑器自己做过「没选时刻」的草稿判断时可以复用这个代码。
    ///     </para>
    /// </summary>
    NoTime = 3,

    /// <summary>日期范围的起始晚于结束。</summary>
    RangeOrder = 4,

    /// <summary>单次规则的时刻已经过去（只警告，不拦保存）。</summary>
    PastOnce = 5,

    /// <summary>同一块看板既 Show 又 Hide（只警告，执行时按步骤顺序生效）。</summary>
    Conflict = 6
}

/// <summary>问题严重度。数值冻结，只能末尾追加。</summary>
public enum ActionValidationSeverity
{
    /// <summary>错误：保存按钮禁用。</summary>
    Error = 0,

    /// <summary>警告：可以保存，保存前弹一句提示。</summary>
    Warning = 1
}

/// <summary>
///     一条校验结果。
///     <para>
///         <see cref="Message" /> 不是中文文案，而是<b>资源键</b>（如 <c>Action_Validation_NoStep</c>）：
///         Core 不依赖界面文案表（CI 是 en-US，文案由 App 层的 <c>Langs/Action</c> 提供），
///         界面拿它去查 resx 再显示。
///     </para>
/// </summary>
public sealed record ActionValidationError(
    ActionValidationMessage Code,
    ActionValidationSeverity Severity,
    string? TargetId = null)
{
    /// <summary>资源键：<c>Action_Validation_&lt;代码&gt;</c>。</summary>
    public string Message => ActionRuleValidator.ResolveResourceKey(Code);

    /// <summary>是不是「只警告、不拦保存」。</summary>
    public bool IsWarning => Severity == ActionValidationSeverity.Warning;
}

/// <summary>
///     规则校验 —— 纯函数，保存前调用；有 <see cref="ActionValidationSeverity.Error" /> 时界面禁用保存，
///     只有 <see cref="ActionValidationSeverity.Warning" /> 时允许保存（保存前提示一句）。
///     <para>
///         校验器不读当前时间：要判「单次已过」就把 <c>now</c> 传进来（App 传 <c>NtpTimeService.NowLocal</c>），
///         不传就跳过这一条 —— 单测因此永远确定。
///     </para>
/// </summary>
public static class ActionRuleValidator
{
    /// <summary>一条规则最多几行步骤（设置页放不下更多；超出不拦保存，界面自己限制）。</summary>
    public const int MaxSteps = 8;

    /// <summary>
    ///     校验一条规则。<paramref name="now" /> 为空时跳过「单次已过」警告。
    ///     返回顺序固定（错误在前、警告在后，同级别按本方法里的检查顺序），方便界面直接逐条显示。
    /// </summary>
    public static IReadOnlyList<ActionValidationError> Validate(ActionRule rule, DateTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var errors = new List<ActionValidationError>();
        var trigger = rule.Trigger ?? new ActionTrigger();

        if (rule.Steps is null || rule.Steps.Count == 0)
            errors.Add(Error(ActionValidationMessage.NoStep));

        switch (trigger.Kind)
        {
            case ActionTriggerKind.Weekly:
                if (((int)trigger.Days & (int)ActionDayFlags.EveryDay) == 0)
                    errors.Add(Error(ActionValidationMessage.NoDay));
                break;

            case ActionTriggerKind.Once:
                if (trigger.Date is null)
                {
                    errors.Add(Error(ActionValidationMessage.NoDate));
                }
                else if (now is { } current && trigger.Date.Value.ToDateTime(trigger.Time) < current)
                {
                    errors.Add(Warning(ActionValidationMessage.PastOnce));
                }

                break;
        }

        if (rule.Condition is { Start: { } start, End: { } end } && start > end)
            errors.Add(Error(ActionValidationMessage.RangeOrder));

        foreach (var targetId in FindConflictingTargets(rule.Steps))
            errors.Add(new ActionValidationError(
                ActionValidationMessage.Conflict, ActionValidationSeverity.Warning, targetId));

        return errors;
    }

    /// <summary>问题代码 → App 层 <c>Langs/Action</c> 里的资源键。</summary>
    public static string ResolveResourceKey(ActionValidationMessage message) => $"Action_Validation_{message}";

    /// <summary>同一块看板既 Show 又 Hide 的目标（按步骤顺序去重）。</summary>
    private static IEnumerable<string> FindConflictingTargets(IReadOnlyList<ActionStep>? steps)
    {
        if (steps is null || steps.Count == 0)
            yield break;

        var seen = new List<string>();
        foreach (var group in steps
                     .Where(step => step is not null)
                     .GroupBy(step => step.TargetId ?? string.Empty, StringComparer.Ordinal))
        {
            var hasShow = group.Any(step => step.Operation == BoardOperation.Show);
            var hasHide = group.Any(step => step.Operation == BoardOperation.Hide);
            if (hasShow && hasHide && !seen.Contains(group.Key, StringComparer.Ordinal))
            {
                seen.Add(group.Key);
                yield return group.Key;
            }
        }
    }

    private static ActionValidationError Error(ActionValidationMessage message) =>
        new(message, ActionValidationSeverity.Error);

    private static ActionValidationError Warning(ActionValidationMessage message) =>
        new(message, ActionValidationSeverity.Warning);
}
