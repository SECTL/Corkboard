namespace Corkboard.Core.Actions;

/// <summary>
///     规则的时间判定 —— 纯函数，全项目只此一份。
///     <para>
///         周视图、月视图、调度器（15 秒一拍）都从这里取「什么时候该发生」，
///         所以「界面看到的」与「实际执行到点」永远是同一套边界。
///     </para>
///     <para>
///         区间口径一律<b>左开右闭</b> <c>(afterExclusive, beforeInclusive]</c>：
///         水位线之后的第一个发生点算在内、水位线本身不算，这样补发（错过的那一段）不会重复执行同一拍。
///     </para>
/// </summary>
public static class ActionSchedule
{
    /// <summary>一次查询最多返回多少个发生点；超过就截断，防止脏配置把界面 / 调度拖死。</summary>
    public const int MaxOccurrences = 2000;

    /// <summary>单条规则最多扫描多少天（安全阀；正常规则几天内就能命中）。</summary>
    private const int MaxScanDays = 366 * 100;

    /// <summary>
    ///     <see cref="NextOccurrence" /> 最多往后找多少天：每天 / 每周规则最迟 7 天内必有发生点，
    ///     留 400 天只是兜底（脏配置里一天都没选时不要在这里空转）。
    /// </summary>
    private const int MaxNextScanDays = 400;

    /// <summary>
    ///     多条规则在 <c>(afterExclusive, beforeInclusive]</c> 里的全部发生点，按时间升序返回。
    ///     <para>
    ///         只算<b>启用</b>的规则。结果按 <see cref="ActionOccurrence.At" /> 排序；同一时刻保持规则顺序
    ///         （LINQ 的 <c>OrderBy</c> 是稳定排序）。超过 <see cref="MaxOccurrences" /> 时截断。
    ///     </para>
    /// </summary>
    public static IReadOnlyList<ActionOccurrence> OccurrencesBetween(
        IEnumerable<ActionRule>? rules, DateTime afterExclusive, DateTime beforeInclusive)
    {
        var result = new List<ActionOccurrence>();
        if (rules is null || beforeInclusive <= afterExclusive)
            return result;

        foreach (var rule in rules)
        {
            if (rule is null || !rule.Enabled)
                continue;

            foreach (var at in OccurrencesOf(rule, afterExclusive, beforeInclusive))
            {
                if (result.Count >= MaxOccurrences)
                    return result.OrderBy(o => o.At).ToArray();

                result.Add(new ActionOccurrence(rule, at));
            }
        }

        return result.OrderBy(o => o.At).ToArray();
    }

    /// <summary>
    ///     单条规则在 <c>(afterExclusive, beforeInclusive]</c> 里的发生点，升序。
    ///     <para>
    ///         只看时间与条件，<b>不看</b> <see cref="ActionRule.Enabled" />（那是调用方的过滤口径）；
    ///         条件（日期范围）与触发时间叠加，两个都满足才算。
    ///     </para>
    /// </summary>
    public static IReadOnlyList<DateTime> OccurrencesOf(
        ActionRule rule, DateTime afterExclusive, DateTime beforeInclusive)
    {
        var result = new List<DateTime>();
        if (rule is null || beforeInclusive <= afterExclusive)
            return result;

        var trigger = rule.Trigger ?? new ActionTrigger();

        switch (trigger.Kind)
        {
            case ActionTriggerKind.Once:
                if (trigger.Date is { } onceDate)
                    AddIfInRange(result, rule, onceDate.ToDateTime(trigger.Time), afterExclusive, beforeInclusive);
                break;

            case ActionTriggerKind.Daily:
            case ActionTriggerKind.Weekly:
                AddRecurring(result, rule, trigger, afterExclusive, beforeInclusive);
                break;
        }

        return result;
    }

    /// <summary>
    ///     <paramref name="after" /> 之后（不含）的下一个发生点；没有就返回 <see langword="null" />。
    ///     <para>
    ///         与 <see cref="OccurrencesBetween" /> 同一套「严格大于」口径，两处结论不会打架。
    ///     </para>
    /// </summary>
    public static DateTime? NextOccurrence(ActionRule rule, DateTime after)
    {
        if (rule is null)
            return null;

        var trigger = rule.Trigger ?? new ActionTrigger();

        switch (trigger.Kind)
        {
            case ActionTriggerKind.Once:
            {
                if (trigger.Date is not { } onceDate)
                    return null;

                var at = onceDate.ToDateTime(trigger.Time);
                return at > after && IsWithinCondition(rule, at) ? at : null;
            }

            case ActionTriggerKind.Daily:
            case ActionTriggerKind.Weekly:
            {
                var date = DateOnly.FromDateTime(after);
                if (rule.Condition is { Start: { } start } && start > date)
                    date = start;

                var last = rule.Condition is { End: { } end } ? end : date.AddDays(MaxNextScanDays);

                for (var step = 0; step < MaxNextScanDays && date <= last; step++, date = date.AddDays(1))
                {
                    if (trigger.Kind == ActionTriggerKind.Weekly && !IncludesDay(trigger.Days, date))
                        continue;

                    var at = date.ToDateTime(trigger.Time);
                    if (at > after && IsWithinCondition(rule, at))
                        return at;
                }

                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>
    ///     这条规则在 <paramref name="date" /> 这一天有没有发生点（月视图数「这天几条」用）。
    ///     <para>
    ///         只看触发方式与日期范围条件，<b>不看</b> <see cref="ActionRule.Enabled" /> 与具体时刻。
    ///     </para>
    /// </summary>
    public static bool OccursOn(ActionRule rule, DateOnly date)
    {
        if (rule is null)
            return false;

        var trigger = rule.Trigger ?? new ActionTrigger();
        var at = date.ToDateTime(trigger.Time);
        if (!IsWithinCondition(rule, at))
            return false;

        return trigger.Kind switch
        {
            ActionTriggerKind.Once => trigger.Date == date,
            ActionTriggerKind.Daily => true,
            ActionTriggerKind.Weekly => IncludesDay(trigger.Days, date),
            _ => false
        };
    }

    /// <summary>
    ///     <paramref name="at" /> 这一刻满不满足规则的条件（目前只有日期范围）。
    ///     <para>
    ///         没有条件、条件类型不认识（未来版本新增的）都按「满足」处理，旧版本读到新条件不会把行动全部吞掉。
    ///     </para>
    /// </summary>
    public static bool IsWithinCondition(ActionRule rule, DateTime at)
    {
        if (rule?.Condition is not { } condition)
            return true;

        if (condition.Kind != ActionConditionKind.DateRange)
            return true;

        var date = DateOnly.FromDateTime(at);
        return (condition.Start is null || date >= condition.Start.Value)
               && (condition.End is null || date <= condition.End.Value);
    }

    private static void AddRecurring(List<DateTime> result, ActionRule rule, ActionTrigger trigger,
        DateTime afterExclusive, DateTime beforeInclusive)
    {
        var first = DateOnly.FromDateTime(afterExclusive);
        var last = DateOnly.FromDateTime(beforeInclusive);

        // 日期范围条件先把扫描区间收窄，不满足就直接不进循环。
        if (rule.Condition is { Start: { } start } && start > first)
            first = start;
        if (rule.Condition is { End: { } end } && end < last)
            last = end;

        var scanned = 0;
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (++scanned > MaxScanDays)
                break;

            if (trigger.Kind == ActionTriggerKind.Weekly && !IncludesDay(trigger.Days, date))
                continue;

            AddIfInRange(result, rule, date.ToDateTime(trigger.Time), afterExclusive, beforeInclusive);

            if (result.Count >= MaxOccurrences)
                break;
        }
    }

    private static void AddIfInRange(List<DateTime> result, ActionRule rule, DateTime at,
        DateTime afterExclusive, DateTime beforeInclusive)
    {
        if (at <= afterExclusive || at > beforeInclusive)
            return;

        if (!IsWithinCondition(rule, at))
            return;

        result.Add(at);
    }

    /// <summary>某一天在不在星期位掩码里（<see cref="ActionDayFlags.None" /> 一天都不命中）。</summary>
    private static bool IncludesDay(ActionDayFlags flags, DateOnly date)
    {
        var bit = date.DayOfWeek switch
        {
            DayOfWeek.Monday => ActionDayFlags.Monday,
            DayOfWeek.Tuesday => ActionDayFlags.Tuesday,
            DayOfWeek.Wednesday => ActionDayFlags.Wednesday,
            DayOfWeek.Thursday => ActionDayFlags.Thursday,
            DayOfWeek.Friday => ActionDayFlags.Friday,
            DayOfWeek.Saturday => ActionDayFlags.Saturday,
            _ => ActionDayFlags.Sunday
        };

        return ((int)flags & (int)bit) != 0;
    }
}
