namespace Corkboard.Core.Actions.Schedule;

/// <summary>
///     把多个数据源的日程条目合并成一条时间线（裁切 + 排序），以及按天计数。
///     <para>
///         排序口径固定为：<see cref="ScheduleItem.Start" /> → <see cref="ScheduleItem.Kind" /> 数值 → <see cref="ScheduleItem.Id" />（Ordinal）。
///         所以同一时刻自定义行动排在系统日程前面、考试排在看板模式前面；顺序不随字典遍历或数据源注册顺序变化，
///         界面不会每次刷新都跳一下。
///     </para>
/// </summary>
public static class ScheduleMerge
{
    /// <summary>
    ///     合并多个源在 <c>[from, to)</c> 里的条目。
    ///     <para>
    ///         区间条目会被裁到查询窗口内（只影响返回的副本，不改源数据）；点事件只保留落在窗口里的。
    ///         与窗口没有任何交集的条目直接丢掉。
    ///     </para>
    /// </summary>
    public static IReadOnlyList<ScheduleItem> Merge(
        IEnumerable<IEnumerable<ScheduleItem>>? sources, DateTime from, DateTime to)
    {
        var items = new List<ScheduleItem>();
        if (sources is null || from >= to)
            return items;

        foreach (var source in sources)
        {
            if (source is null)
                continue;

            foreach (var item in source)
            {
                if (item is null)
                    continue;

                if (item.End is { } end)
                {
                    var start = item.Start < from ? from : item.Start;
                    var clippedEnd = end > to ? to : end;
                    if (clippedEnd <= start)
                        continue;

                    items.Add(item with { Start = start, End = clippedEnd });
                }
                else
                {
                    if (item.Start < from || item.Start >= to)
                        continue;

                    items.Add(item);
                }
            }
        }

        return items
            .OrderBy(item => item.Start)
            .ThenBy(item => (int)item.Kind)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    ///     <paramref name="date" /> 这一天有几条日程（月视图的「这天 N 条」用）。
    ///     <para>
    ///         点事件按开始日算；区间按与当天 <c>[00:00, 次日 00:00)</c> 有没有交集算 ——
    ///         跨夜区间在两天里都会各算一次（它确实占了两天）。
    ///     </para>
    /// </summary>
    public static int CountOn(IEnumerable<ScheduleItem>? items, DateOnly date)
    {
        if (items is null)
            return 0;

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var count = 0;

        foreach (var item in items)
        {
            if (item is null)
                continue;

            if (item.End is { } end)
            {
                if (item.Start < dayEnd && end > dayStart)
                    count++;
            }
            else if (DateOnly.FromDateTime(item.Start) == date)
            {
                count++;
            }
        }

        return count;
    }
}
