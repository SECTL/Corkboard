using Corkboard.Core.Notes;

namespace Corkboard.Core.Board.History;

/// <summary>
///     把历史日志重放成「某个时刻的板面」。纯逻辑、不碰文件：快照按哈希从存储层读。
///     <para>
///         与旧实现（<c>ReplayTimeline</c>，只看现存作业的创建时间）的本质区别：
///         这里用的是**全部变更**——之后被删掉、被改过的作业，在它还在的那一刻照样能画出来。
///     </para>
/// </summary>
public static class BoardHistory
{
    /// <summary>时间轴上的一个停靠点：某个时刻，以及那一刻板子上看得见几项作业。</summary>
    public sealed record Stop(DateTimeOffset At, int NoteCount);

    /// <summary>
    ///     按历史事件生成停靠点：每一秒有变更就算一个停靠点，<see cref="Stop.NoteCount" /> 是那一秒
    ///     全部事件应用完之后、板子上还看得见的作业数（被收起来的算看不见）。
    /// </summary>
    public static IReadOnlyList<Stop> BuildStops(
        IReadOnlyList<NoteHistoryEntry> entries,
        Func<string, Note?> loadSnapshot)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(loadSnapshot);

        var states = new Dictionary<Guid, Note>();
        var stops = new List<Stop>();
        var ordered = Sort(entries);

        var index = 0;
        while (index < ordered.Count)
        {
            var second = SecondKey(ordered[index].At);
            var at = ordered[index].At;

            // 同一秒里的几条事件算一个停靠点：先全部应用，再记条数。
            while (index < ordered.Count && SecondKey(ordered[index].At) == second)
            {
                Apply(states, ordered[index], loadSnapshot);
                index++;
            }

            stops.Add(new Stop(at, CountVisible(states)));
        }

        return stops;
    }

    /// <summary>
    ///     重放出「停在 <paramref name="at" /> 这一刻时板子上看得见的作业」。
    ///     那一刻之后才发生的编辑、删除都不算数，所以看到的是当时的内容。
    /// </summary>
    public static IReadOnlyList<Note> StateAt(
        IReadOnlyList<NoteHistoryEntry> entries,
        DateTimeOffset at,
        Func<string, Note?> loadSnapshot)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(loadSnapshot);

        var states = new Dictionary<Guid, Note>();
        var limit = SecondKey(at);

        foreach (var entry in Sort(entries))
        {
            if (SecondKey(entry.At) > limit)
                break;

            Apply(states, entry, loadSnapshot);
        }

        return [.. states.Values.Where(note => note.CleanedAt is null)];
    }

    private static int CountVisible(Dictionary<Guid, Note> states)
        => states.Values.Count(note => note.CleanedAt is null);

    /// <summary>历史里同一条作业可能被「删除 → 还原」来回折腾，所以状态按 Id 覆盖，不是一维时间线。</summary>
    private static void Apply(
        Dictionary<Guid, Note> states,
        NoteHistoryEntry entry,
        Func<string, Note?> loadSnapshot)
    {
        switch (entry.Action)
        {
            case NoteHistoryAction.Create:
            case NoteHistoryAction.Update:
                if (entry.Snapshot is not { } blob || loadSnapshot(blob) is not { } note)
                {
                    // 内容缺失（日志被手工改过 / 快照文件丢了）：保留上一次的状态，别把整条作业抹掉。
                    return;
                }

                states[entry.NoteId] = note;
                return;

            case NoteHistoryAction.Delete:
                states.Remove(entry.NoteId);
                return;

            case NoteHistoryAction.Clean:
                if (states.TryGetValue(entry.NoteId, out var cleaned))
                    cleaned.CleanedAt = entry.At;
                return;

            case NoteHistoryAction.Restore:
                if (states.TryGetValue(entry.NoteId, out var restored))
                    restored.CleanedAt = null;
                return;
        }
    }

    /// <summary>按时刻排序；同一时刻保持日志里的先后（<see cref="Enumerable.OrderBy" /> 是稳定排序）。</summary>
    private static List<NoteHistoryEntry> Sort(IReadOnlyList<NoteHistoryEntry> entries)
        => [.. entries.OrderBy(entry => entry.At)];

    /// <summary>把时刻截到本地时区的「秒」，停靠点与筛选都按它对齐（与界面显示的秒一致）。</summary>
    private static DateTime SecondKey(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        return new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, local.Second,
            DateTimeKind.Local);
    }
}
