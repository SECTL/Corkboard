using Corkboard.Core.Actions.Schedule;

namespace Corkboard.Services.Actions.Schedule;

/// <summary>
///     日程源注册表：把各数据源的条目合成一条时间线。
///     <para>
///         源顺序按 <see cref="IScheduleSource.Id" /> 排序（不随 DI 注册顺序或字典遍历变化），
///         合并与裁切交给 <see cref="ScheduleMerge.Merge" /> —— 同一份输入每次得到同一个顺序，界面不会跳。
///     </para>
/// </summary>
public sealed class ScheduleSourceRegistry : IScheduleSourceRegistry
{
    /// <summary>构造注册表；源列表在构造期定型。</summary>
    public ScheduleSourceRegistry(IEnumerable<IScheduleSource> sources)
    {
        Sources = sources
            .Where(source => source is not null)
            .OrderBy(source => source.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<IScheduleSource> Sources { get; }

    /// <inheritdoc />
    public IReadOnlyList<ScheduleItem> GetItems(DateTime fromInclusive, DateTime toExclusive)
    {
        if (fromInclusive >= toExclusive)
            return [];

        var perSource = Sources.Select(source => source.GetItems(fromInclusive, toExclusive));
        return ScheduleMerge.Merge(perSource, fromInclusive, toExclusive);
    }
}
