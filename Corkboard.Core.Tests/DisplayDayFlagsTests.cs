using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Tests;

/// <summary>
///     「这一段哪几天生效」的位标志：七个星期位、三个预设组合，以及脏值兜底。
///     <para>
///         位标志最容易出的错是「一个都没勾」被当成「哪一天都不生效」，那样用户清空之后就再也弹不出来了；
///         这里的口径是**空 = 每天**，用例把这条钉死。
///     </para>
/// </summary>
public class DisplayDayFlagsTests
{
    // 2026-10-26 是周一。
    private static readonly DateOnly Monday = new(2026, 10, 26);

    [Fact]
    public void Presets_CoverTheExpectedDays()
    {
        Assert.Equal(
            DisplayDayFlags.Monday | DisplayDayFlags.Tuesday | DisplayDayFlags.Wednesday |
            DisplayDayFlags.Thursday | DisplayDayFlags.Friday,
            DisplayDayFlags.Weekdays);

        Assert.Equal(DisplayDayFlags.Saturday | DisplayDayFlags.Sunday, DisplayDayFlags.Weekend);
        Assert.Equal(DisplayDayFlags.Weekdays | DisplayDayFlags.Weekend, DisplayDayFlags.EveryDay);
        Assert.Equal(DisplayDayFlags.EveryDay, DisplayDayFlagsExtensions.All);
    }

    /// <summary>一周七天的顺序从周一开始（界面上那排小方块就是按这个排的）。</summary>
    [Fact]
    public void Ordered_StartsOnMonday()
    {
        Assert.Equal(7, DisplayDayFlagsExtensions.Ordered.Count);
        Assert.Equal(DisplayDayFlags.Monday, DisplayDayFlagsExtensions.Ordered[0]);
        Assert.Equal(DisplayDayFlags.Sunday, DisplayDayFlagsExtensions.Ordered[6]);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, DisplayDayFlags.Monday)]
    [InlineData(DayOfWeek.Wednesday, DisplayDayFlags.Wednesday)]
    [InlineData(DayOfWeek.Sunday, DisplayDayFlags.Sunday)]
    public void ForDayOfWeek_MapsEveryDay(DayOfWeek day, DisplayDayFlags expected) =>
        Assert.Equal(expected, DisplayDayFlagsExtensions.ForDayOfWeek(day));

    [Fact]
    public void ForDate_FollowsTheCalendar()
    {
        Assert.Equal(DisplayDayFlags.Monday, DisplayDayFlagsExtensions.ForDate(Monday));
    }

    [Fact]
    public void Includes_TestsTheRightBit()
    {
        Assert.True(DisplayDayFlagsExtensions.Includes(DisplayDayFlags.Weekdays, Monday));
        Assert.False(DisplayDayFlagsExtensions.Includes(DisplayDayFlags.Weekend, Monday));
    }

    /// <summary>空或者带不带得动的脏位，都回落到每天——宁可多弹一次，也不要永远弹不出来。</summary>
    [Theory]
    [InlineData(DisplayDayFlags.None)]
    [InlineData((DisplayDayFlags)0b1000_0000)]
    [InlineData((DisplayDayFlags)(-1))]
    public void Normalize_FallsBackToEveryDay(DisplayDayFlags flags) =>
        Assert.Equal(DisplayDayFlags.EveryDay, DisplayDayFlagsExtensions.Normalize(flags));

    [Fact]
    public void Normalize_KeepsValidCombinations()
    {
        Assert.Equal(DisplayDayFlags.Weekend, DisplayDayFlagsExtensions.Normalize(DisplayDayFlags.Weekend));
        Assert.Equal(DisplayDayFlags.Monday, DisplayDayFlagsExtensions.Normalize(DisplayDayFlags.Monday));
    }
}
