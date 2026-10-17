namespace Corkboard.Core.Board.History;

/// <summary>历史日志里的一行动作。枚举按名字落盘（不是数字），方便人眼看日志。</summary>
public enum NoteHistoryAction
{
    /// <summary>作业第一次出现（新布置，或旧数据的历史起点）。</summary>
    Create,

    /// <summary>作业被编辑：内容、科目、分类、截止日期或标签改了。</summary>
    Update,

    /// <summary>作业被删除。当前文件已经删了，但日志里留着它删之前的整份快照。</summary>
    Delete,

    /// <summary>作业被「收起来」（自动清理 / 清空整板）：文件还在，只是从板子上隐藏。</summary>
    Clean,

    /// <summary>被收起来的作业重新放回板子。</summary>
    Restore
}

/// <summary>
///     一条历史事件：什么时候、对哪条作业、做了什么、内容长什么样。
///     <para>
///         内容不直接写在这行里，而是先按内容哈希去重存进 blob 库（见
///         <see cref="INoteHistoryStore.SaveSnapshot" /> ），这里只记哈希——
///         同一份内容被反复保存时不会越存越多，回放时再按哈希把快照读回来。
///         <see cref="Snapshot" /> 为空表示这次动作不需要内容（例如 Clean）。
///     </para>
/// </summary>
/// <param name="At">动作发生的时刻（写盘精确到秒）。</param>
/// <param name="NoteId">作业 Id。作业删掉之后，回放只能靠它把这行日志跟快照对上。</param>
/// <param name="Action">做了什么。</param>
/// <param name="Snapshot">作业整份内容的 blob 哈希；为空的动作不带内容。</param>
public sealed record NoteHistoryEntry(
    DateTimeOffset At,
    Guid NoteId,
    NoteHistoryAction Action,
    string? Snapshot = null);
