namespace Corkboard.Core.Actions.Schedule;

/// <summary>
///     日程源注册表：把各数据源的条目合成一份时间线给行动窗口 / 周视图用。
///     <para>
///         实现方（App 层）负责按 <see cref="IScheduleSource.Id" /> 稳定排序后合并
///         （顺序不随 <see cref="Dictionary{TKey,TValue}" /> 遍历变化），再交给
///         <see cref="ScheduleMerge.Merge" /> 裁切与排序 —— 同一份输入每次都要得到同一个顺序。
///     </para>
/// </summary>
public interface IScheduleSourceRegistry
{
    /// <summary>全部数据源（顺序固定：按 <see cref="IScheduleSource.Id" /> 排序）。</summary>
    IReadOnlyList<IScheduleSource> Sources { get; }

    /// <summary>合并所有源在 <c>[fromInclusive, toExclusive)</c> 里的条目。</summary>
    IReadOnlyList<ScheduleItem> GetItems(DateTime fromInclusive, DateTime toExclusive);
}
