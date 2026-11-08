namespace Corkboard.Core.Actions;

/// <summary>
///     一次「规则到点」的发生点：哪条规则、在哪个绝对时刻。
///     <para>
///         周视图 / 月视图与调度器都从 <see cref="ActionSchedule.OccurrencesBetween" /> 拿它，
///         两边看的是同一份结果，不会出现「界面上显示了、到点却没跑」。
///     </para>
/// </summary>
public readonly record struct ActionOccurrence(ActionRule Rule, DateTime At);
