using Corkboard.Core.Notes;

namespace Corkboard.Core.Board;

/// <summary>回放时间轴上的一个停靠点：某个时刻（精确到秒），以及到那一刻为止累计的作业数。</summary>
public sealed record ReplayStop(DateTimeOffset At, int NoteCount);

/// <summary>
///     时空回放的纯逻辑：把作业按创建时刻摊成一条时间轴，并按某个停靠点筛出「当时已经存在的作业」。
///     <para>
///         停靠点精确到<b>秒</b>：同一秒内创建的多条作业合成一个停靠点。早先按日历日分组，
///         一天里的作业只能一起看，回放粒度太粗；现在停靠点之间就是「那一刻板子上有什么」。
///     </para>
///     <para>
///         回放看的是**累计**结果——那一刻板子上的全部内容，而不是只看那一刻新增的；
///         所以 <see cref="ReplayStop.NoteCount" /> 与 <see cref="Select" /> 筛出来的条数一致。
///     </para>
/// </summary>
public static class ReplayTimeline
{
    /// <summary>按创建时刻（本地时区、截到秒）升序摊出停靠点，作业数逐个累加。</summary>
    public static IReadOnlyList<ReplayStop> Build(IEnumerable<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var stops = new List<ReplayStop>();
        var total = 0;

        foreach (var group in notes
                     .GroupBy(note => ToLocalSecond(note.CreatedAt))
                     .OrderBy(group => group.Key))
        {
            total += group.Count();
            stops.Add(new ReplayStop(group.Key, total));
        }

        return stops;
    }

    /// <summary>筛出到 <paramref name="throughAt" /> 那一秒为止已经存在的作业（含那一秒）。</summary>
    public static IReadOnlyList<Note> Select(IEnumerable<Note> notes, DateTimeOffset throughAt)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var target = ToLocalSecond(throughAt);
        return [.. notes.Where(note => ToLocalSecond(note.CreatedAt) <= target)];
    }

    /// <summary>
    ///     作业创建时刻落在哪一秒：按本地时区取年月日时分秒，毫秒一律抹掉。
    ///     分组与比较都走它，所以「同一秒」的作业一定落在同一个停靠点、也一定一起被筛出来。
    /// </summary>
    public static DateTimeOffset ToLocalSecond(DateTimeOffset createdAt)
    {
        var local = createdAt.ToLocalTime();
        var second = new DateTime(local.Year, local.Month, local.Day,
            local.Hour, local.Minute, local.Second, DateTimeKind.Unspecified);

        return new DateTimeOffset(second, TimeZoneInfo.Local.GetUtcOffset(second));
    }
}
