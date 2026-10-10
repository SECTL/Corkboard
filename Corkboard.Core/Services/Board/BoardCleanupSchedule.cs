using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.Board;

namespace Corkboard.Core.Services.Board;

/// <summary>
///     「到点了吗」的纯判定：按设定的频率与时刻，从上一次动手到现在之间有没有跨过一个该动手的时刻。
///     <para>
///         上次动手的时刻由调用方在进程内记着（见 <see cref="BoardCleanupService.LastRun" />），
///         <b>不落盘</b>：磁盘上的状态文件只会在用户手改数据目录时变成第二份对不上的真相，
///         而这份记忆丢了也没关系——宁可这次启动补清一遍（空跑无害），也不能漏掉该清的时刻。
///     </para>
/// </summary>
public static class BoardCleanupSchedule
{
    /// <summary>
    ///     最近一个已经过去的动手时刻。传进来的时刻早于今天的设定时刻时会回退到昨天（每周则回退到上周）。
    /// </summary>
    public static DateTimeOffset LastDueSlot(
        BoardCleanupFrequency frequency, DayOfWeek weekday, int hour, int minute, DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        var time = new TimeSpan(BoardCleanupDefaults.ClampHour(hour), BoardCleanupDefaults.ClampMinute(minute), 0);
        var candidate = new DateTimeOffset(local.Date + time, local.Offset);

        if (candidate > now)
            candidate = candidate.AddDays(-1);

        if (frequency != BoardCleanupFrequency.Weekly)
            return candidate;

        // 从「今天或昨天」再往前退到最近的那个目标星期几，时刻不变。
        var back = ((int)candidate.DayOfWeek - (int)weekday + 7) % 7;
        return candidate.AddDays(-back);
    }

    /// <summary>
    ///     现在该不该动手：最近一个动手时刻是否晚于上次动手的时刻。
    ///     <paramref name="lastRun" /> 为空表示这次会话里还没动过手，只要时刻过了就动手（启动补清）。
    /// </summary>
    public static bool ShouldRun(
        BoardCleanupFrequency frequency,
        DayOfWeek weekday,
        int hour,
        int minute,
        DateTimeOffset? lastRun,
        DateTimeOffset now)
    {
        var slot = LastDueSlot(frequency, weekday, hour, minute, now);
        return lastRun is not { } previous || slot > previous;
    }
}
