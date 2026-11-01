using Corkboard.Core.Display;
using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Tests;

/// <summary>
///     「到点自动进入 / 退出看板模式」的判定：时段内外、跨零点、要不要进 / 要不要退、还剩多久。
///     <para>
///     「现在」一律由参数传进来，用例里没有一处读本机时钟——判定是纯函数，钉死时刻才守得住边界。
///     </para>
///     <para>
///     边界口径与全项目一致：**左闭右开**。<c>now == enter</c> 已经算进来了，
///     <c>now == exit</c> 已经算出去了。这条最容易写错，所以每个窗口都单独钉一次。
///     </para>
/// </summary>
public class DisplaySchedulePolicyTests
{
    /// <summary>普通当天一段：19:00 进、21:30 退（就是默认的那一对时刻）。</summary>
    private static readonly TimeOnly Enter = new(19, 0);

    private static readonly TimeOnly Exit = new(21, 30);

    /// <summary>跨零点那一段：今晚 22:00 进、明晨 06:00 退。</summary>
    private static readonly TimeOnly NightEnter = new(22, 0);

    private static readonly TimeOnly NightExit = new(6, 0);

    /// <summary>默认时刻就是晚上七点到九点半，别在别处另写一组数字。</summary>
    [Fact]
    public void Defaults_AreSevenPmAndHalfPastNine()
    {
        Assert.Equal(new TimeOnly(19, 0), DisplaySchedulePolicy.DefaultEnterTime);
        Assert.Equal(new TimeOnly(21, 30), DisplaySchedulePolicy.DefaultExitTime);
    }

    /// <summary>
    ///     当天一段的左闭右开：进入那一刻就在时段里，退出那一刻已经不在。
    /// </summary>
    [Theory]
    [InlineData(12, 0, false)] // 中午：离得远
    [InlineData(18, 59, false)] // 差一分钟才到进入时刻
    [InlineData(19, 0, true)] // 边界：进入那一刻 → 已经在里面
    [InlineData(19, 1, true)]
    [InlineData(20, 45, true)]
    [InlineData(21, 29, true)] // 最后一分钟
    [InlineData(21, 30, false)] // 边界：退出那一刻 → 已经出来了
    [InlineData(21, 31, false)]
    [InlineData(23, 59, false)]
    public void IsInside_HonoursLeftClosedRightOpenBounds(int hour, int minute, bool expected) =>
        Assert.Equal(expected, DisplaySchedulePolicy.IsInside(new TimeOnly(hour, minute), Enter, Exit));

    /// <summary>
    ///     跨零点：<c>enter &gt; exit</c> 就读成「今晚这段 + 明晨那段」，不引入日期概念。
    /// </summary>
    [Theory]
    [InlineData(21, 59, false)] // 还差一分钟才进
    [InlineData(22, 0, true)] // 边界：今晚这一段开始
    [InlineData(23, 0, true)]
    [InlineData(23, 59, true)]
    [InlineData(0, 0, true)] // 已经过了零点，仍在明晨那半段里
    [InlineData(0, 30, true)]
    [InlineData(5, 59, true)] // 最后一分钟
    [InlineData(6, 0, false)] // 边界：明晨到点就出来
    [InlineData(12, 0, false)] // 白天：两段都不在
    [InlineData(18, 0, false)]
    public void IsInside_CrossesMidnightWhenEnterIsLaterThanExit(int hour, int minute, bool expected) =>
        Assert.Equal(expected, DisplaySchedulePolicy.IsInside(new TimeOnly(hour, minute), NightEnter, NightExit));

    /// <summary>
    ///     进入与退出同一时刻 = 一段零长度的时间。当成「全天展示」会把屏幕永久占住，
    ///     所以一律读成「不展示」——包括恰好等于那一刻。
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 30)]
    [InlineData(19, 0)]
    [InlineData(23, 59)]
    public void IsInside_EqualEnterAndExitMeansNoWindow(int hour, int minute)
    {
        var moment = new TimeOnly(hour, minute);

        Assert.False(DisplaySchedulePolicy.IsInside(moment, Enter, Enter));
        Assert.False(DisplaySchedulePolicy.IsInside(moment, moment, moment));
    }

    /// <summary>
    ///     该不该现在铺出来：时段内、并且此刻还没开着。
    ///     已经开着的那一扇不能再被要求「进」一次（不然看门狗每十几秒就抢一次屏幕）。
    /// </summary>
    [Theory]
    [InlineData(false, 19, 30, true)] // 时段内、没开着 → 进
    [InlineData(true, 19, 30, false)] // 时段内、已经开着 → 什么都不做
    [InlineData(false, 21, 30, false)] // 时段外 → 不进
    [InlineData(false, 18, 0, false)]
    public void ShouldEnter_OnlyWhenInsideAndNotOpenYet(bool isOpen, int hour, int minute, bool expected) =>
        Assert.Equal(expected,
            DisplaySchedulePolicy.ShouldEnter(isOpen, new TimeOnly(hour, minute), Enter, Exit));

    /// <summary>
    ///     该不该现在收掉：时段已经过了、并且此刻正开着。
    ///     本来就没开着的那一扇不能被要求「退」——那样会去关一个用户自己开的窗。
    /// </summary>
    [Theory]
    [InlineData(true, 21, 30, true)] // 到点了、正开着 → 退
    [InlineData(true, 22, 0, true)]
    [InlineData(true, 19, 30, false)] // 还在时段里 → 不退
    [InlineData(false, 22, 0, false)] // 时段外、但没开着 → 什么都不做
    [InlineData(false, 19, 30, false)]
    public void ShouldExit_OnlyWhenOutsideAndStillOpen(bool isOpen, int hour, int minute, bool expected) =>
        Assert.Equal(expected,
            DisplaySchedulePolicy.ShouldExit(isOpen, new TimeOnly(hour, minute), Enter, Exit));

    /// <summary>跨零点的时段同样遵守这两条：凌晨两点正开着就不退，六点整才退。</summary>
    [Fact]
    public void ShouldEnterAndExit_WorkAcrossMidnight()
    {
        var twoAm = new TimeOnly(2, 0);
        var sixAm = new TimeOnly(6, 0);

        Assert.True(DisplaySchedulePolicy.ShouldEnter(false, twoAm, NightEnter, NightExit));
        Assert.False(DisplaySchedulePolicy.ShouldEnter(true, twoAm, NightEnter, NightExit));

        Assert.False(DisplaySchedulePolicy.ShouldExit(true, twoAm, NightEnter, NightExit));
        Assert.True(DisplaySchedulePolicy.ShouldExit(true, sixAm, NightEnter, NightExit));
    }

    /// <summary>时段内：距退出还有多久（设置页那句「距离退出还有 1 小时 20 分」）。</summary>
    [Fact]
    public void TimeUntilExit_ReturnsTheRemainingSpanInsideTheWindow()
    {
        Assert.Equal(TimeSpan.FromMinutes(90), DisplaySchedulePolicy.TimeUntilExit(new TimeOnly(20, 0), Enter, Exit));
        Assert.Equal(TimeSpan.FromMinutes(150), DisplaySchedulePolicy.TimeUntilExit(Enter, Enter, Exit)); // 刚进来
        Assert.Equal(TimeSpan.FromMinutes(1), DisplaySchedulePolicy.TimeUntilExit(new TimeOnly(21, 29), Enter, Exit));
    }

    /// <summary>跨零点时退出时刻在「明天」：23:00 到明晨 06:00 还有 7 小时，不是 −17 小时。</summary>
    [Fact]
    public void TimeUntilExit_WrapsAcrossMidnight()
    {
        Assert.Equal(TimeSpan.FromHours(7), DisplaySchedulePolicy.TimeUntilExit(new TimeOnly(23, 0), NightEnter, NightExit));
        Assert.Equal(TimeSpan.FromHours(8), DisplaySchedulePolicy.TimeUntilExit(NightEnter, NightEnter, NightExit));
        Assert.Equal(TimeSpan.FromMinutes(1), DisplaySchedulePolicy.TimeUntilExit(new TimeOnly(5, 59), NightEnter, NightExit));
    }

    /// <summary>不在时段里就没有「还剩多久」——返回 null，调用方据此不显示那句话。</summary>
    [Theory]
    [InlineData(18, 59)]
    [InlineData(21, 30)]
    [InlineData(23, 0)]
    public void TimeUntilExit_ReturnsNullOutsideTheWindow(int hour, int minute) =>
        Assert.Null(DisplaySchedulePolicy.TimeUntilExit(new TimeOnly(hour, minute), Enter, Exit));

    /// <summary>零长度时段也没有剩余时间。</summary>
    [Fact]
    public void TimeUntilExit_ReturnsNullForZeroLengthWindow() =>
        Assert.Null(DisplaySchedulePolicy.TimeUntilExit(Enter, Enter, Enter));

    /// <summary>
    ///     手改过 settings.json 也不会在界面上崩掉：小时夹进 0–23、分钟夹进 0–59。
    /// </summary>
    [Theory]
    [InlineData(19, 0, 19, 0)] // 正常值原样
    [InlineData(0, 0, 0, 0)]
    [InlineData(23, 59, 23, 59)]
    [InlineData(-5, 0, 0, 0)] // 负的小时
    [InlineData(99, 0, 23, 0)] // 越界的小时
    [InlineData(19, -1, 19, 0)] // 负的分钟
    [InlineData(19, 99, 19, 59)] // 越界的分钟
    [InlineData(-5, -5, 0, 0)] // 两个都越界
    [InlineData(99, 99, 23, 59)]
    [InlineData(int.MaxValue, int.MaxValue, 23, 59)]
    [InlineData(int.MinValue, int.MinValue, 0, 0)]
    public void ResolveTime_ClampsOutOfRangeNumbers(int hour, int minute, int expectedHour, int expectedMinute) =>
        Assert.Equal(new TimeOnly(expectedHour, expectedMinute), DisplaySchedulePolicy.ResolveTime(hour, minute));

    /// <summary>
    ///     <see cref="DisplaySchedulePolicy.Split" /> 与 <see cref="DisplaySchedulePolicy.ResolveTime" />
    ///     必须是一对互逆的换算（设置页两个输入框各绑一半）。
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 5)]
    [InlineData(19, 0)]
    [InlineData(21, 30)]
    [InlineData(23, 59)]
    public void Split_RoundTripsThroughResolveTime(int hour, int minute)
    {
        var time = new TimeOnly(hour, minute);

        var (splitHour, splitMinute) = DisplaySchedulePolicy.Split(time);

        Assert.Equal(hour, splitHour);
        Assert.Equal(minute, splitMinute);
        Assert.Equal(time, DisplaySchedulePolicy.ResolveTime(splitHour, splitMinute));
    }

    /// <summary>一周里的一段：19:00 进、21:30 退，只在给定的那几天生效。</summary>
    private static DisplaySlot Slot(DisplayDayFlags days, int exitHour = 21, int exitMinute = 30) => new()
    {
        Days = days,
        EnterHour = 19,
        EnterMinute = 0,
        ExitHour = exitHour,
        ExitMinute = exitMinute
    };

    // 2026-10-26 是周一，2026-10-31 是周六——星期几的用例都按这两天算。
    private static readonly DateTime MondayEvening = new(2026, 10, 26, 20, 0, 0);

    private static readonly DateTime SaturdayEvening = new(2026, 10, 31, 20, 0, 0);

    /// <summary>工作日配了时段，周末那一天就不该自己弹出来。</summary>
    [Fact]
    public void IsActive_FollowsTheWeekdayFlags()
    {
        DisplaySlot[] slots = [Slot(DisplayDayFlags.Weekdays)];

        Assert.True(DisplaySchedulePolicy.IsActive(slots[0], MondayEvening));
        Assert.False(DisplaySchedulePolicy.IsActive(slots[0], SaturdayEvening));
    }

    /// <summary>关掉的那一段一律不算。</summary>
    [Fact]
    public void IsActive_IgnoresDisabledSlots()
    {
        var slot = Slot(DisplayDayFlags.EveryDay);
        slot.Enabled = false;

        Assert.False(DisplaySchedulePolicy.IsActive(slot, MondayEvening));
    }

    /// <summary>跨零点那半段算在「开始那一天」头上：周五晚上那一段，周六凌晨五点半还在里面。</summary>
    [Fact]
    public void IsActive_CrossMidnightCountsTheStartDay()
    {
        var slot = new DisplaySlot
        {
            Days = DisplayDayFlags.Friday,
            EnterHour = 22,
            EnterMinute = 0,
            ExitHour = 6,
            ExitMinute = 0
        };

        Assert.True(DisplaySchedulePolicy.IsActive(slot, new DateTime(2026, 10, 30, 23, 0, 0)));  // 周五夜里
        Assert.True(DisplaySchedulePolicy.IsActive(slot, new DateTime(2026, 10, 31, 5, 30, 0)));   // 周六凌晨
        Assert.False(DisplaySchedulePolicy.IsActive(slot, new DateTime(2026, 10, 31, 6, 0, 0)));   // 到点出来了
        Assert.False(DisplaySchedulePolicy.IsActive(slot, new DateTime(2026, 10, 31, 23, 0, 0)));  // 周六夜里没这一段
    }

    /// <summary>几段里挑出「现在正处在」的那一段。</summary>
    [Fact]
    public void FindActiveSlot_PicksTheSlotThatContainsNow()
    {
        DisplaySlot[] slots =
        [
            Slot(DisplayDayFlags.EveryDay, 8, 0),
            new() { Days = DisplayDayFlags.EveryDay, EnterHour = 19, EnterMinute = 0, ExitHour = 21, ExitMinute = 30 }
        ];

        var active = DisplaySchedulePolicy.FindActiveSlot(slots, MondayEvening);

        Assert.NotNull(active);
        Assert.Equal(new TimeOnly(19, 0), active!.EnterTime);
        Assert.Null(DisplaySchedulePolicy.FindActiveSlot(slots, new DateTime(2026, 10, 26, 12, 0, 0)));
    }

    /// <summary>该不该自动弹出，看的是那一段在不在、开没开。</summary>
    [Fact]
    public void ShouldEnter_UsesTheSlots()
    {
        DisplaySlot[] slots = [Slot(DisplayDayFlags.Weekdays)];

        Assert.True(DisplaySchedulePolicy.ShouldEnter(false, MondayEvening, slots));
        Assert.False(DisplaySchedulePolicy.ShouldEnter(true, MondayEvening, slots));
        Assert.False(DisplaySchedulePolicy.ShouldEnter(false, SaturdayEvening, slots));
    }

    /// <summary>到点该收了；还在时段里就不收。</summary>
    [Fact]
    public void ShouldExit_FollowsTheSlots()
    {
        DisplaySlot[] slots = [Slot(DisplayDayFlags.Weekdays)];

        Assert.True(DisplaySchedulePolicy.ShouldExit(true, new DateTime(2026, 10, 26, 21, 30, 0), slots));
        Assert.False(DisplaySchedulePolicy.ShouldExit(true, MondayEvening, slots));
    }

    /// <summary>今天这一段已经过了：下一次是明天同一时刻。</summary>
    [Fact]
    public void NextStart_ReturnsTheNextEnabledSlotStart()
    {
        DisplaySlot[] slots = [Slot(DisplayDayFlags.Weekdays)];

        Assert.Equal(
            new DateTime(2026, 10, 27, 19, 0, 0),
            DisplaySchedulePolicy.NextStart(slots, new DateTime(2026, 10, 26, 22, 0, 0)));

        // 周五晚上之后下一次是下周一，周末不排。
        Assert.Equal(
            new DateTime(2026, 11, 2, 19, 0, 0),
            DisplaySchedulePolicy.NextStart(slots, new DateTime(2026, 10, 30, 22, 0, 0)));
    }

    /// <summary>一段都没配：没有下一次，也就不会自己弹。</summary>
    [Fact]
    public void NextStart_ReturnsNullWithoutSlots()
    {
        Assert.Null(DisplaySchedulePolicy.NextStart([], MondayEvening));
        Assert.Null(DisplaySchedulePolicy.NextStart(null, MondayEvening));
    }

    /// <summary>当前的结束时刻：当天这一段就在今晚，跨零点的那段要落到明晨。</summary>
    [Fact]
    public void ResolveActiveEnd_HandlesCrossMidnight()
    {
        var daytime = Slot(DisplayDayFlags.EveryDay);
        Assert.Equal(
            new DateTime(2026, 10, 26, 21, 30, 0),
            DisplaySchedulePolicy.ResolveActiveEnd(daytime, MondayEvening));

        var overnight = new DisplaySlot
        {
            Days = DisplayDayFlags.Friday,
            EnterHour = 22,
            EnterMinute = 0,
            ExitHour = 6,
            ExitMinute = 0
        };

        Assert.Equal(
            new DateTime(2026, 10, 31, 6, 0, 0),
            DisplaySchedulePolicy.ResolveActiveEnd(overnight, new DateTime(2026, 10, 30, 23, 0, 0)));
    }
}
