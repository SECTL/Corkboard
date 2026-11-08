using Corkboard.Core.Actions;
using Corkboard.Core.Actions.Schedule;

namespace Corkboard.Services.Actions.Schedule;

/// <summary>
///     自定义行动的日程源：把 <see cref="ActionRuleStore" /> 里的规则按触发方式投影成日程条目。
///     <para>
///         时间判定一律交给 <see cref="ActionSchedule" />（界面与调度器同一份口径），
///         这里只负责「规则 → 条目」的形状转换。停用的规则照样产出条目（<see cref="ScheduleItem.IsEnabled" />
///         为假），窗口才能把它显示成灰色，而不是像没配过一样消失。
///     </para>
///     <para>
///         「今天不执行」只作用于系统日程（老服务各自判自己的源），自定义行动的暂停是规则自己的
///         <see cref="ActionRule.Enabled" />，所以 <see cref="CanPause" /> 为假。
///     </para>
/// </summary>
public sealed class CustomActionScheduleSource : IScheduleSource
{
    /// <summary>源标识（与系统日程的 Id 区分开）。</summary>
    public const string SourceId = "custom";

    private readonly ActionRuleStore _rules;

    /// <summary>构造日程源。</summary>
    public CustomActionScheduleSource(ActionRuleStore rules)
    {
        _rules = rules;
    }

    /// <inheritdoc />
    public string Id => SourceId;

    /// <inheritdoc />
    public string DisplayName => ActionResources.Get("Action_Group_Mine", "我的行动");

    /// <inheritdoc />
    public bool CanPause => false;

    /// <inheritdoc />
    public IReadOnlyList<ScheduleItem> GetItems(DateTime fromInclusive, DateTime toExclusive)
    {
        if (fromInclusive >= toExclusive)
            return [];

        var items = new List<ScheduleItem>();
        foreach (var rule in _rules.Rules)
        {
            if (rule is null)
                continue;

            foreach (var at in ActionSchedule.OccurrencesOf(rule, fromInclusive, toExclusive))
            {
                if (items.Count >= ActionSchedule.MaxOccurrences)
                    return items.OrderBy(item => item.Start).ToArray();

                items.Add(new ScheduleItem
                {
                    Id = rule.Id.ToString("D"),
                    Kind = ScheduleItemKind.CustomAction,
                    Title = ActionTextFormatter.Summarize(rule),
                    Start = at,
                    End = null,
                    IsEnabled = rule.Enabled,
                    IsEditable = true,
                    CanPause = false,
                    IsSuppressed = false,
                    SettingsPageId = null,
                    TargetIds = rule.Steps.Select(step => step.TargetId).Distinct(StringComparer.Ordinal).ToArray(),
                    RuleId = rule.Id
                });
            }
        }

        return items
            .OrderBy(item => item.Start)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
