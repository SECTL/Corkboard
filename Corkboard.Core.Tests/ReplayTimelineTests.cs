using Corkboard.Core.Board;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Tests;

/// <summary>
///     时空回放的纯逻辑：按创建时刻（精确到秒）摊出停靠点，并筛出「到某一秒为止已存在」的作业。
///     回放看的是累计结果，这两个语义一旦反了，界面上就会变成「按秒翻页」。
/// </summary>
public class ReplayTimelineTests
{
    private static Note NoteAt(string content, int year, int month, int day,
        int hour = 10, int minute = 0, int second = 0, int millisecond = 0)
    {
        // 归档与回放都按本地时区分层，所以用本地偏移构造。
        var local = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
        return new Note
        {
            Content = content,
            Subject = "数学",
            CreatedAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local))
        };
    }

    private static DateTimeOffset At(int year, int month, int day,
        int hour = 10, int minute = 0, int second = 0, int millisecond = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    [Fact]
    public void Build_ProducesOneStopPerSecondInAscendingOrder()
    {
        var notes = new[]
        {
            NoteAt("三", 2026, 3, 4, 8, 30, 1),
            NoteAt("一", 2026, 3, 4, 8, 29, 59),
            NoteAt("二", 2026, 3, 4, 8, 30, 0)
        };

        var stops = ReplayTimeline.Build(notes);

        // 同一天里的不同秒也是不同的停靠点，按时刻升序。
        Assert.Equal(3, stops.Count);
        Assert.Equal([At(2026, 3, 4, 8, 29, 59), At(2026, 3, 4, 8, 30, 0), At(2026, 3, 4, 8, 30, 1)],
            stops.Select(stop => stop.At));
    }

    [Fact]
    public void Build_SameDayDifferentSeconds_ProducesSeparateStops()
    {
        // 一天里早上布置的和下午布置的不是同一个停靠点——这是「精确到秒」的核心要求。
        var notes = new[]
        {
            NoteAt("早上", 2026, 3, 4, 8, 0, 0),
            NoteAt("下午", 2026, 3, 4, 15, 30, 0)
        };

        var stops = ReplayTimeline.Build(notes);

        Assert.Equal(2, stops.Count);
        Assert.Equal(At(2026, 3, 4, 8, 0, 0), stops[0].At);
        Assert.Equal(At(2026, 3, 4, 15, 30, 0), stops[1].At);
    }

    [Fact]
    public void Build_MergesNotesCreatedWithinTheSameSecond()
    {
        // 同一秒里的几百毫秒差算同一个停靠点：回放粒度到秒，再细下去界面上挑不过来。
        var notes = new[]
        {
            NoteAt("一", 2026, 3, 4, 8, 30, 0, 100),
            NoteAt("二", 2026, 3, 4, 8, 30, 0, 900)
        };

        var stops = ReplayTimeline.Build(notes);

        var stop = Assert.Single(stops);
        Assert.Equal(2, stop.NoteCount);
        Assert.Equal(At(2026, 3, 4, 8, 30, 0), stop.At);
    }

    [Fact]
    public void Build_AccumulatesCountsAcrossStops()
    {
        // 停靠点上的数量是「到那一刻为止」的累计值，不是那一刻新增数。
        var notes = new[]
        {
            NoteAt("一", 2026, 3, 4, 8, 0, 0),
            NoteAt("二", 2026, 3, 4, 8, 0, 0),
            NoteAt("三", 2026, 3, 4, 9, 0, 0),
            NoteAt("四", 2026, 3, 6, 9, 0, 0)
        };

        var stops = ReplayTimeline.Build(notes);

        Assert.Equal([2, 3, 4], stops.Select(stop => stop.NoteCount));
    }

    [Fact]
    public void Build_StopCountsMatchSelectResults()
    {
        // 停靠点上写「共 N 项」，点进去必须正好筛出 N 项，两处不能各算各的。
        var notes = new[]
        {
            NoteAt("一", 2026, 3, 4, 8, 0, 0),
            NoteAt("二", 2026, 3, 4, 8, 0, 0),
            NoteAt("三", 2026, 3, 5, 23, 59, 59),
            NoteAt("四", 2026, 3, 6, 0, 0, 0)
        };

        foreach (var stop in ReplayTimeline.Build(notes))
            Assert.Equal(stop.NoteCount, ReplayTimeline.Select(notes, stop.At).Count);
    }

    [Fact]
    public void Select_IncludesNotesUpToAndIncludingTheGivenSecond()
    {
        var notes = new[]
        {
            NoteAt("早", 2026, 3, 4, 8, 0, 0),
            NoteAt("同一秒晚些", 2026, 3, 4, 8, 30, 0, 900),
            NoteAt("下一秒", 2026, 3, 4, 8, 30, 1)
        };

        var selected = ReplayTimeline.Select(notes, At(2026, 3, 4, 8, 30, 0));

        // 「截到秒」比较：8:30:00.900 与 8:30:00 是同一秒，必须包含它。
        Assert.Equal(["早", "同一秒晚些"], selected.Select(note => note.Content));
    }

    [Fact]
    public void Select_AcrossDaysUsesTimeNotCalendarDay()
    {
        // 跨天时也得按时刻比：前一天 23:59:59 的停在 23:59:59 时不该看到第二天 0 点那条。
        var notes = new[]
        {
            NoteAt("前一天最后一秒", 2026, 3, 4, 23, 59, 59),
            NoteAt("第二天零点", 2026, 3, 5, 0, 0, 0)
        };

        var selected = ReplayTimeline.Select(notes, At(2026, 3, 4, 23, 59, 59));

        Assert.Equal(["前一天最后一秒"], selected.Select(note => note.Content));
    }

    [Fact]
    public void Select_BeforeEarliestSecond_ReturnsNothing()
    {
        var notes = new[] { NoteAt("一", 2026, 3, 4, 8, 0, 0) };

        Assert.Empty(ReplayTimeline.Select(notes, At(2026, 3, 4, 7, 59, 59)));
    }

    [Fact]
    public void Select_OnLastSecond_ReturnsEverything()
    {
        var notes = new[]
        {
            NoteAt("一", 2026, 3, 4, 8, 0, 0),
            NoteAt("二", 2026, 3, 6, 9, 0, 0)
        };

        var selected = ReplayTimeline.Select(notes, At(2026, 3, 6, 9, 0, 0));

        Assert.Equal(2, selected.Count);
    }

    [Fact]
    public void Build_OnEmptyInput_ProducesNoStops() => Assert.Empty(ReplayTimeline.Build([]));

    [Fact]
    public void ToLocalSecond_TruncatesMilliseconds()
    {
        var createdAt = At(2026, 3, 4, 8, 30, 0, 900);

        var second = ReplayTimeline.ToLocalSecond(createdAt);

        Assert.Equal(At(2026, 3, 4, 8, 30, 0), second);
        Assert.Equal(0, second.Millisecond);
    }
}
