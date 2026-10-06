using Corkboard.Core.Models.Board;

namespace Corkboard.Core.Services.Board;

/// <summary>回放时间轴上的一个停靠点：某一天，以及到那一天为止累计的作业数。</summary>
public sealed record BoardReplayStop(DateOnly Date, int NoteCount);

/// <summary>
///     时空回放的纯逻辑：把作业按创建日期摊成一条时间轴，并按某个停靠点筛出「当时已经存在的作业」。
///     <para>
///         作业本来就按 <c>&lt;年&gt;/&lt;月&gt;/&lt;日&gt;</c> 归档，所以一天正好是一个停靠点；
///         回放看的是**累计**结果——那一刻板子上的全部内容，而不是只看那一天新增的。
///     </para>
/// </summary>
public static class BoardReplayTimeline
{
    /// <summary>按创建日期（本地时区的日历日）升序摊出停靠点，作业数逐日累加。</summary>
    public static IReadOnlyList<BoardReplayStop> Build(IEnumerable<BoardNote> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var stops = new List<BoardReplayStop>();
        var total = 0;

        foreach (var group in notes
                     .GroupBy(note => ToLocalDate(note.CreatedAt))
                     .OrderBy(group => group.Key))
        {
            total += group.Count();
            stops.Add(new BoardReplayStop(group.Key, total));
        }

        return stops;
    }

    /// <summary>筛出到 <paramref name="throughDate" /> 当天为止已经存在的作业（含当天）。</summary>
    public static IReadOnlyList<BoardNote> Select(IEnumerable<BoardNote> notes, DateOnly throughDate)
    {
        ArgumentNullException.ThrowIfNull(notes);

        return [.. notes.Where(note => ToLocalDate(note.CreatedAt) <= throughDate)];
    }

    /// <summary>作业落在哪一天：按本地时区的日历日算，和归档目录的分层规则一致。</summary>
    public static DateOnly ToLocalDate(DateTimeOffset createdAt) =>
        DateOnly.FromDateTime(createdAt.ToLocalTime().DateTime);
}
