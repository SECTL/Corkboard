using Corkboard.Core.Display;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.Display;

namespace Corkboard.Core.Actions.Schedule;

/// <summary>
///     把「看板模式的自动进出时段」投影成日程中心里的系统日程条目。
///     <para>
///         每个（时段, 日期）一段区间；<b>跨零点</b>（22:00 进、次日 02:00 出）在日界处拆成两条：
///         <c>[22:00, 次日 00:00)</c> + <c>[次日 00:00, 次日 02:00)</c> —— 两条首尾相接、不重叠，
///         这样周视图 / 月视图按天裁剪时不会把跨夜区间在两天里重复算一整段。
///     </para>
///     <para>
///         「哪一天算这一段」与 <see cref="DisplaySchedulePolicy" /> 完全一致：跨夜段归<b>开始那天</b>的星期，
///         所以这里用 <c>DisplayDayFlagsExtensions.Includes</c> 判开始日，而不是次日。
///     </para>
/// </summary>
public static class DisplayScheduleProjection
{
    /// <summary>系统日程源 Id（也是「今天不执行」的抑制键）。</summary>
    public const string SourceId = "display-auto-show";

    /// <summary>对应的设置页 Id（「看板模式 → 时段」页）。</summary>
    public const string SettingsPageId = "settings.display.schedule";

    /// <summary>条目显示名。</summary>
    public const string Title = "看板模式";

    /// <summary>
    ///     投影 <c>[fromInclusive, toExclusive)</c> 里与时段有交集的条目（按开始时刻升序）。
    ///     <para>
    ///         <see cref="DisplaySettingsConfig.AutoShow" /> 关掉时条目照样产出，但
    ///         <see cref="ScheduleItem.IsEnabled" /> 为假 —— 窗口要能把「配了但没开」的时段显示成灰色。
    ///         停用的时段（<see cref="DisplaySlot.Enabled" /> 为假）与零长度时段不产出。
    ///     </para>
    /// </summary>
    public static IReadOnlyList<ScheduleItem> Project(
        DisplaySettingsConfig settings, DateTime fromInclusive, DateTime toExclusive)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var items = new List<ScheduleItem>();
        if (fromInclusive >= toExclusive)
            return items;

        var slots = settings.ActiveSlots;

        // 跨夜段可能从前一天开始、延续到查询区间里，所以日期要多扫一天。
        var firstDate = DateOnly.FromDateTime(fromInclusive).AddDays(-1);
        var lastDate = DateOnly.FromDateTime(toExclusive);

        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            if (slot is null || !slot.Enabled || slot.IsEmpty)
                continue;

            var enter = slot.EnterTime;
            var exit = slot.ExitTime;

            for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
            {
                if (!DisplayDayFlagsExtensions.Includes(slot.Days, date))
                    continue;

                if (enter < exit)
                {
                    AddIfOverlaps(items, settings, index, date,
                        date.ToDateTime(enter), date.ToDateTime(exit),
                        fromInclusive, toExclusive);
                    continue;
                }

                // 跨零点：日界处拆成两段，左闭右开、不重叠、不丢边界。
                var midnight = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
                AddIfOverlaps(items, settings, index, date,
                    date.ToDateTime(enter), midnight, fromInclusive, toExclusive);
                AddIfOverlaps(items, settings, index, date.AddDays(1),
                    midnight, date.AddDays(1).ToDateTime(exit), fromInclusive, toExclusive);
            }
        }

        return items
            .OrderBy(item => item.Start)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddIfOverlaps(List<ScheduleItem> items, DisplaySettingsConfig settings, int slotIndex,
        DateOnly startDate, DateTime start, DateTime end, DateTime fromInclusive, DateTime toExclusive)
    {
        if (start >= toExclusive || end <= fromInclusive)
            return;

        items.Add(new ScheduleItem
        {
            Id = $"{SourceId}-{slotIndex}-{startDate:yyyy-MM-dd}",
            Kind = ScheduleItemKind.DisplayAutoShow,
            Title = Title,
            Start = start,
            End = end,
            IsEnabled = settings.AutoShow,
            IsEditable = false,
            CanPause = true,
            IsSuppressed = false,
            SettingsPageId = SettingsPageId,
            TargetIds = [BoardTargetIds.DisplayBoard],
            RuleId = null
        });
    }
}
