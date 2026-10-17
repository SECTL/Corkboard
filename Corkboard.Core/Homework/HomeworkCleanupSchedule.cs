
namespace Corkboard.Core.Homework;

/// <summary>
///     「到点了吗」的纯判定：只有<b>今天</b>（按周清理时是本周的那个星期几）的清理时刻
///     已经过去、并且上一次动手早于这个时刻，才该动手。
///     <para>
///         ⚠️ 必须<b>先到点，才谈补清</b>：早先的写法是「最近一个过去的时刻」——
///         于是应用在凌晨 0 点启动时，会把昨天 4 点当成「错过的时刻」立刻清一遍，
///         用户看到的就是「没到清理时刻它就清了」。现在没到今天的时刻之前什么都不做，
///         应用没开着而错过时刻的，下次启动只要已经过了今天的时刻就补上。
///     </para>
///     <para>
///         上次动手的时刻由调用方在进程内记着（见 <see cref="HomeworkCleanupService.LastRun" />），
///         <b>不落盘</b>：磁盘上的状态文件只会在用户手改数据目录时变成第二份对不上的真相，
///         而这份记忆丢了也没关系——宁可这次启动补清一遍（空跑无害），也不能漏掉该清的时刻。
///     </para>
/// </summary>
public static class HomeworkCleanupSchedule
{
    /// <summary>
    ///     今天那个该动手的时刻；还没到点、或今天不是配置的清理日（按周清理）时为 <c>null</c>。
    ///     <para>
    ///         只认「今天」：昨天/上周的那个时刻不再拿来补清，否则不到点就会动手。
    ///     </para>
    /// </summary>
    public static DateTimeOffset? DueSlotToday(
        HomeworkCleanupFrequency frequency, DayOfWeek weekday, int hour, int minute, DateTimeOffset now)
    {
        var local = now.ToLocalTime();

        // 按周清理只在那一天动手；其它日子一整天都不该清。
        if (frequency == HomeworkCleanupFrequency.Weekly && local.DayOfWeek != weekday)
            return null;

        var time = new TimeSpan(HomeworkCleanupDefaults.ClampHour(hour), HomeworkCleanupDefaults.ClampMinute(minute), 0);
        var slot = new DateTimeOffset(local.Date + time, local.Offset);

        // 没到今天的清理时刻：什么都别做。
        return slot <= now ? slot : null;
    }

    /// <summary>
    ///     现在该不该动手：今天的清理时刻已经过了，并且上次动手早于它。
    ///     <paramref name="lastRun" /> 为空表示这次会话里还没动过手，当时的时刻只要已经过了就动手。
    /// </summary>
    public static bool ShouldRun(
        HomeworkCleanupFrequency frequency,
        DayOfWeek weekday,
        int hour,
        int minute,
        DateTimeOffset? lastRun,
        DateTimeOffset now)
    {
        if (DueSlotToday(frequency, weekday, hour, minute, now) is not { } slot)
            return false;

        return lastRun is not { } previous || previous < slot;
    }
}
