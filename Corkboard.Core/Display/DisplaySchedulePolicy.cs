using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Display;

/// <summary>
///     「到点自动进入 / 退出看板模式」的判定规则 —— 纯函数，有单测。
///     <para>
///         两件事：这一刻该不该进、这一刻该不该退。看门狗
///         （<c>Services/Display/DisplayAutoShowHostedService</c>）每十几秒问一次，
///         设置页的状态卡也读同一份，<b>不要在别处另写一套时间比较</b>。
///     </para>
///     <para>
///         边界一律**左闭右开**，与全项目的时间判定口径一致：
///         <c>now ≥ 进入时刻</c> 就算进了，<c>now ≥ 退出时刻</c> 就算该退了。
///     </para>
///     <para>
///         <b>支持跨零点</b>：进入 21:00、退出 23:00 是普通的当天一段；
///         进入 22:00、退出 02:00 则视为「今晚 22 点到明晨 2 点」这一段。
///         判据只是 <c>enter &gt; exit</c>，不引入日期概念。
///     </para>
///     <para>
///         一个时段还能限定「星期几」（<see cref="DisplaySlot.Days" />），而且可以配多段；
///         带 <see cref="DisplaySlot" /> 的重载才是看门狗真正用的那一套，只带两个时刻的重载留着
///         给 URI 临时时段与老单测。
///     </para>
/// </summary>
public static class DisplaySchedulePolicy
{
    /// <summary>默认进入时刻：晚上 19:00。</summary>
    public static readonly TimeOnly DefaultEnterTime = new(19, 0);

    /// <summary>默认退出时刻：晚上 21:30。</summary>
    public static readonly TimeOnly DefaultExitTime = new(21, 30);

    /// <summary>
    ///     现在是不是落在这一段里（只看时刻，不看日期）。
    ///     <para>
    ///         进入时刻与退出时刻**相同**时返回 <see langword="false" />：那是一段零长度的时间，
    ///         当成「全天展示」会把屏幕永久占住，当成「不展示」才是安全的那一边。
    ///     </para>
    /// </summary>
    public static bool IsInside(TimeOnly now, TimeOnly enter, TimeOnly exit)
    {
        if (enter == exit)
            return false;

        return enter < exit
            ? now >= enter && now < exit // 当天一段
            : now >= enter || now < exit; // 跨零点：今晚那段 + 明晨那段
    }

    /// <summary>
    ///     <paramref name="now" /> 这一刻这一段是不是正在进行 —— 会同时看星期几与跨零点。
    ///     <para>
    ///         跨零点的那一段归**开始那天**：周日 22:00 → 02:00 只要求周日被选上，
    ///         周一凌晨的那一刻仍然算「周日晚那一段」，而不是要求周一也被选上。
    ///     </para>
    /// </summary>
    public static bool IsActive(DisplaySlot? slot, DateTime now)
    {
        if (slot is null || !slot.Enabled || slot.IsEmpty)
            return false;

        var enter = slot.EnterTime;
        var exit = slot.ExitTime;
        var today = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);

        // 今天开始的那一段。
        if (DisplayDayFlagsExtensions.Includes(slot.Days, today))
        {
            if (enter < exit && time >= enter && time < exit)
                return true;

            if (enter > exit && time >= enter)
                return true;
        }

        // 昨天开始、跨到今天凌晨的那半段。
        if (enter > exit)
        {
            var yesterday = today.AddDays(-1);
            if (DisplayDayFlagsExtensions.Includes(slot.Days, yesterday) && time < exit)
                return true;
        }

        return false;
    }

    /// <summary>这一刻正在进行的那一段；没有就返回 <see langword="null" />。</summary>
    public static DisplaySlot? FindActiveSlot(IEnumerable<DisplaySlot>? slots, DateTime now)
    {
        if (slots is null)
            return null;

        foreach (var slot in slots)
        {
            if (IsActive(slot, now))
                return slot;
        }

        return null;
    }

    /// <summary>该不该现在把看板模式铺出来：有正在进行的一段、并且此刻还没开着。</summary>
    public static bool ShouldEnter(bool isOpen, DateTime now, IEnumerable<DisplaySlot>? slots) =>
        !isOpen && FindActiveSlot(slots, now) is not null;

    /// <summary>该不该现在把看板模式收掉：这一段已经过了、并且此刻正开着。</summary>
    public static bool ShouldExit(bool isOpen, DateTime now, IEnumerable<DisplaySlot>? slots) =>
        isOpen && FindActiveSlot(slots, now) is null;

    /// <summary>老的「该不该进」重载（单段、只看时刻）。</summary>
    public static bool ShouldEnter(bool isOpen, TimeOnly now, TimeOnly enter, TimeOnly exit) =>
        !isOpen && IsInside(now, enter, exit);

    /// <summary>老的「该不该退」重载（单段、只看时刻）。</summary>
    public static bool ShouldExit(bool isOpen, TimeOnly now, TimeOnly enter, TimeOnly exit) =>
        isOpen && !IsInside(now, enter, exit);

    /// <summary>
    ///     这一段进行到什么时候结束（绝对时刻）。用来记「这一轮被手动关掉了，别再弹回来」。
    ///     现在不在这段里时返回 <see langword="null" />。
    /// </summary>
    public static DateTime? ResolveActiveEnd(DisplaySlot? slot, DateTime now)
    {
        if (!IsActive(slot, now))
            return null;

        var enter = slot!.EnterTime;
        var exit = slot.ExitTime;
        var today = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);

        // 跨零点的那段：今天凌晨时属于昨天开始的那一轮。
        var startDate = enter > exit && time < exit ? today.AddDays(-1) : today;
        var endDate = enter > exit ? startDate.AddDays(1) : startDate;
        return endDate.ToDateTime(exit);
    }

    /// <summary>这一段还剩多久（设置页写「距离退出还有 1 小时 20 分」）。现在不在段里返回 <see langword="null" />。</summary>
    public static TimeSpan? TimeUntilExit(DisplaySlot? slot, DateTime now) =>
        ResolveActiveEnd(slot, now) is { } end ? end - now : null;

    /// <summary>
    ///     时段在今天还剩多久（老的单段重载）。
    ///     现在不在时段里返回 <see langword="null" />。
    /// </summary>
    public static TimeSpan? TimeUntilExit(TimeOnly now, TimeOnly enter, TimeOnly exit)
    {
        if (!IsInside(now, enter, exit))
            return null;

        // 退出时刻已经过了（跨零点后的凌晨那半段），退到明天的退出时刻。
        return exit > now ? exit - now : exit.Add(TimeSpan.FromDays(1)) - now;
    }

    /// <summary>
    ///     下一段什么时候开始（设置页状态条、托盘提示用）。只看未来 8 天，
    ///     一段都没配就返回 <see langword="null" />。
    /// </summary>
    public static DateTime? NextStart(IEnumerable<DisplaySlot>? slots, DateTime now)
    {
        if (slots is null)
            return null;

        DateTime? earliest = null;
        var today = DateOnly.FromDateTime(now);

        foreach (var slot in slots)
        {
            if (slot is null || !slot.Enabled || slot.IsEmpty)
                continue;

            for (var offset = 0; offset <= 8; offset++)
            {
                var date = today.AddDays(offset);
                if (!DisplayDayFlagsExtensions.Includes(slot.Days, date))
                    continue;

                var start = date.ToDateTime(slot.EnterTime);
                if (start <= now)
                    continue;

                if (earliest is null || start < earliest)
                    earliest = start;

                break;
            }
        }

        return earliest;
    }

    /// <summary>
    ///     把「小时 + 分钟」两个数拼成时刻；越界的数一律夹回 0–23 / 0–59，
    ///     手改过 settings.json 也不会在界面上崩掉。
    /// </summary>
    public static TimeOnly ResolveTime(int hour, int minute) =>
        new(Math.Clamp(hour, 0, 23), Math.Clamp(minute, 0, 59));

    /// <summary>把时刻拆回「小时 + 分钟」两个数（设置页两个输入框各绑一个）。</summary>
    public static (int Hour, int Minute) Split(TimeOnly time) => (time.Hour, time.Minute);
}
