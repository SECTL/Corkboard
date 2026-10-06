namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     作业板的便签排序方式。取代了早期的「置顶便签排到前面」，置顶这个概念已经取消。
///     <para>
///         枚举按数字落盘（见 <c>ConfigServiceBase.JsonOptions</c>），只能在<b>末尾</b>增删成员。
///         后续计划追加：手动拖拽、截止日期、科目。
///     </para>
/// </summary>
public enum BoardSortMode
{
    /// <summary>按创建时间，新的在前。</summary>
    CreatedDescending = 0,

    /// <summary>按创建时间，旧的在前。</summary>
    CreatedAscending = 1,

    /// <summary>最近修改的在前。</summary>
    UpdatedDescending = 2,

    /// <summary>按科目。枚举值 3 历史上是「按标题」，作业名移除后语义改为科目，数字保持不变。</summary>
    Subject = 3,

    /// <summary>
    ///     手动：科目区块的先后由主界面上长按手柄拖出来的顺序决定（落盘在
    ///     <c>BoardConfig.SubjectOrder</c>），区块内仍是创建顺序。
    /// </summary>
    Manual = 4
}
