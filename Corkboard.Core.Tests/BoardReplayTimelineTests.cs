using Corkboard.Core.Models.Board;
using Corkboard.Core.Services.Board;

namespace Corkboard.Core.Tests;

/// <summary>
///     时空回放的纯逻辑：按创建日期摊出停靠点，并筛出「到某天为止已存在」的作业。
///     回放看的是累计结果，这两个语义一旦反了，界面上就会变成「按天翻页」。
/// </summary>
public class BoardReplayTimelineTests
{
    private static BoardNote Note(string content, int year, int month, int day, int hour = 10)
    {
        // 归档与回放都按本地时区的日历日分层，所以用本地偏移构造。
        var local = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);
        return new BoardNote
        {
            Content = content,
            Subject = "数学",
            CreatedAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local))
        };
    }

    [Fact]
    public void Build_ProducesOneStopPerDayInAscendingOrder()
    {
        var notes = new[]
        {
            Note("三", 2026, 3, 6),
            Note("一", 2026, 3, 4),
            Note("二", 2026, 3, 4)
        };

        var stops = BoardReplayTimeline.Build(notes);

        Assert.Equal(2, stops.Count);
        Assert.Equal(new DateOnly(2026, 3, 4), stops[0].Date);
        Assert.Equal(new DateOnly(2026, 3, 6), stops[1].Date);
    }

    [Fact]
    public void Build_AccumulatesCountsAcrossDays()
    {
        // 停靠点上的数量是「到那天为止」的累计值，不是当天新增数。
        var notes = new[]
        {
            Note("一", 2026, 3, 4),
            Note("二", 2026, 3, 4),
            Note("三", 2026, 3, 6)
        };

        var stops = BoardReplayTimeline.Build(notes);

        Assert.Equal(2, stops[0].NoteCount);
        Assert.Equal(3, stops[1].NoteCount);
    }

    [Fact]
    public void Select_ReturnsNotesUpToAndIncludingTheGivenDay()
    {
        var notes = new[]
        {
            Note("早", 2026, 3, 4),
            Note("当天晚些", 2026, 3, 5, 23),
            Note("之后", 2026, 3, 6)
        };

        var selected = BoardReplayTimeline.Select(notes, new DateOnly(2026, 3, 5));

        // 当天 23 点也算「当天为止」，所以必须包含它。
        Assert.Equal(["早", "当天晚些"], selected.Select(note => note.Content));
    }

    [Fact]
    public void Select_BeforeEarliestDay_ReturnsNothing()
    {
        var notes = new[] { Note("一", 2026, 3, 4) };

        Assert.Empty(BoardReplayTimeline.Select(notes, new DateOnly(2026, 3, 3)));
    }

    [Fact]
    public void Select_OnLastDay_ReturnsEverything()
    {
        var notes = new[]
        {
            Note("一", 2026, 3, 4),
            Note("二", 2026, 3, 6)
        };

        var selected = BoardReplayTimeline.Select(notes, new DateOnly(2026, 3, 6));

        Assert.Equal(2, selected.Count);
    }

    [Fact]
    public void Build_OnEmptyInput_ProducesNoStops()
    {
        Assert.Empty(BoardReplayTimeline.Build([]));
    }

    [Fact]
    public void ToLocalDate_UsesCalendarDayOfLocalTime()
    {
        var local = new DateTime(2026, 3, 4, 0, 30, 0, DateTimeKind.Unspecified);
        var createdAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));

        Assert.Equal(new DateOnly(2026, 3, 4), BoardReplayTimeline.ToLocalDate(createdAt));
    }
}
