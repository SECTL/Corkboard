using Corkboard.Core.Notes;

namespace Corkboard.Core.Board.History;

/// <summary>历史占用了多少：多少条事件、多少份去重后的内容快照、总共多少字节（设置页显示用）。</summary>
public sealed record BoardHistoryStatistics(int EntryCount, int SnapshotCount, long Bytes);

/// <summary>
///     作业历史的存储：追加式日志（每天的 <c>.jsonl</c>）+ 按内容哈希去重的快照库。
///     <para>
///         日志回答「什么时候对哪条作业做了什么」，快照回答「那时候它的内容长什么样」。
///         删除作业时当前文件会被删掉，但日志里留下删除前的整份快照，所以回放能把它按当时的样子画回来，
///         「还原到此刻」也能把它放回板子。
///     </para>
///     <para>
///         三个容量参数（<see cref="CoalesceWindow" /> / <see cref="RetentionDays" /> / <see cref="MaxBytes" />）
///         由设置页与启动时的同步服务写入，存储层只负责照做。范围见 <see cref="BoardHistoryPolicy" />。
///     </para>
/// </summary>
public interface INoteHistoryStore
{
    /// <summary>同一条作业连续保存的合并窗口：两次保存间隔小于它，只留最后一版。</summary>
    TimeSpan CoalesceWindow { get; set; }

    /// <summary>历史保留天数，超过的文件会在 <see cref="Prune" /> 里删掉。</summary>
    int RetentionDays { get; set; }

    /// <summary>历史占用上限（字节），超了先删最老的那几天。</summary>
    long MaxBytes { get; set; }

    /// <summary>按时间顺序读出全部历史事件。</summary>
    IReadOnlyList<NoteHistoryEntry> LoadAll();

    /// <summary>追加一条历史事件（写进它发生那天对应的日志文件）。</summary>
    void Append(NoteHistoryEntry entry);

    /// <summary>
    ///     用新事件替换刚写下的最后一条（同一 <see cref="NoteHistoryEntry.NoteId" /> 且同一天才替换）。
    ///     这是「连续保存只记一版」的落点：追回去改最后一行，而不是再写一条。
    ///     没有可替换的行（例如刚启动、或别的作业刚写过）时返回 <c>false</c>。
    /// </summary>
    bool ReplaceLast(NoteHistoryEntry entry);

    /// <summary>把一条作业存成内容快照，返回内容哈希；同样的内容只存一份。</summary>
    string SaveSnapshot(Note note);

    /// <summary>按哈希读回一份作业快照；哈希非法或文件缺失/损坏时返回 <c>null</c>。</summary>
    Note? LoadSnapshot(string snapshot);

    /// <summary>按保留天数与容量上限清理历史，返回删掉的日志文件数。</summary>
    int Prune();

    /// <summary>统计当前历史占用（设置页显示用）。</summary>
    BoardHistoryStatistics GetStatistics();
}
