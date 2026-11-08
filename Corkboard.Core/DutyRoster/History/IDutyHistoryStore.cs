namespace Corkboard.Core.DutyRoster.History;

/// <summary>
///     值日表历史（临时层文件）的读写契约。
///     <para>
///         一天一个 <c>.jsonl</c>，只追加；导入、兑换、加罚、指定、撤销都写在这里，
///         用于事后追溯「这一天为什么是这一组、这个人」。
///     </para>
/// </summary>
public interface IDutyHistoryStore
{
    /// <summary>某一天的事件（按写入顺序）。</summary>
    IReadOnlyList<DutyHistoryEvent> Read(DateOnly date);

    /// <summary>[from, to] 内全部事件（按日期、写入顺序）。</summary>
    IReadOnlyList<DutyHistoryEvent> ReadRange(DateOnly from, DateOnly to);

    /// <summary>有历史文件的日期（升序）。</summary>
    IReadOnlyList<DateOnly> ListDays();

    /// <summary>追加一条事件；写盘失败只记日志、不打断业务。</summary>
    void Append(DutyHistoryEvent historyEvent);

    /// <summary>删除 <paramref name="keepFrom" /> 之前的历史文件。</summary>
    void Prune(DateOnly keepFrom);
}
