using Corkboard.Core.DutyRoster;

namespace Corkboard.Core.Tests;

/// <summary>固定周几模式、小组代班/对调、固定人员、个人兑换与视图标记。</summary>
public class DutySchedulerTests
{
    private static readonly DateOnly Monday = DutyTestContext.Day(1, 5);
    private static readonly DateOnly Tuesday = DutyTestContext.Day(1, 6);
    private static readonly DateOnly Wednesday = DutyTestContext.Day(1, 7);
    private static readonly DateOnly Thursday = DutyTestContext.Day(1, 8);
    private static readonly DateOnly Friday = DutyTestContext.Day(1, 9);

    private static (DutyTestContext Context, DutyGroup A, DutyGroup B, DutyGroup C) CreateFixed()
    {
        var context = new DutyTestContext();
        var a = context.AddGroup("A", 0);
        var b = context.AddGroup("B", 1);
        var c = context.AddGroup("C", 2);
        context.Schedule.Mode = DutyScheduleMode.FixedWeekday;
        context.Schedule.Weekdays = DutyWeekdays.Workdays;
        context.Schedule.FixedWeekdayMap[(int)DayOfWeek.Monday] = a.Id;
        context.Schedule.FixedWeekdayMap[(int)DayOfWeek.Tuesday] = b.Id;
        context.Schedule.FixedWeekdayMap[(int)DayOfWeek.Wednesday] = a.Id;
        context.Schedule.FixedWeekdayMap[(int)DayOfWeek.Thursday] = c.Id;
        context.Schedule.FixedWeekdayMap[(int)DayOfWeek.Friday] = b.Id;
        return (context, a, b, c);
    }

    [Fact]
    public void FixedMode_PenaltySwallowsFollowingDutyDays()
    {
        var (context, a, _, _) = CreateFixed();
        context.Document.Adjustments.Add(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.GroupPenalty,
            GroupId = a.Id,
            AnchorDate = Monday,
            Days = 1
        });

        var view = context.CreateScheduler().BuildRange(Monday, Tuesday);

        Assert.Equal("A", view.Days.Single(day => day.Date == Tuesday).GroupName);
        Assert.True(view.Days.Single(day => day.Date == Tuesday).HasMarker(DutyDayMarker.Penalty));
    }

    [Fact]
    public void GroupAssign_OverridesOneDay()
    {
        var (context, _, b, _) = CreateFixed();
        context.Document.Adjustments.Add(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.GroupAssign,
            GroupId = b.Id,
            Start = Monday,
            End = Monday
        });

        var view = context.CreateScheduler().BuildRange(Monday, Tuesday);

        Assert.Equal("B", view.Days.Single(day => day.Date == Monday).GroupName);
        Assert.True(view.Days.Single(day => day.Date == Monday).HasMarker(DutyDayMarker.Exchange));
        Assert.Equal("B", view.Days.Single(day => day.Date == Tuesday).GroupName);
    }

    [Fact]
    public void GroupSwap_ExchangesTwoGroups()
    {
        var (context, a, b, _) = CreateFixed();
        context.Document.Adjustments.Add(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.GroupSwap,
            GroupId = a.Id,
            TargetGroupId = b.Id,
            Start = Monday,
            End = Tuesday
        });

        var view = context.CreateScheduler().BuildRange(Monday, Tuesday);

        Assert.Equal("B", view.Days.Single(day => day.Date == Monday).GroupName);
        Assert.Equal("A", view.Days.Single(day => day.Date == Tuesday).GroupName);
    }

    [Fact]
    public void FixedAssignment_ReplacesTheGroupMemberOnItsWeekdays()
    {
        var (context, a, _, _) = CreateFixed();
        var post = context.AddPost("Wipe cabinet", 0);
        var inside = context.AddMember("Inside");
        var fixedMember = context.AddMember("Fixed");
        context.Assign(a, post, inside);
        context.Document.FixedAssignments.Add(new DutyFixedAssignment
        {
            PostId = post.Id,
            MemberId = fixedMember.RecordId,
            Weekdays = DutyWeekdays.ToBit(DayOfWeek.Monday),
            Relation = DutyFixedRelation.Replace
        });

        var view = context.CreateScheduler().BuildRange(Monday, Wednesday);
        var monday = view.Days.Single(day => day.Date == Monday).Cells.Single();
        var wednesday = view.Days.Single(day => day.Date == Wednesday).Cells.Single();
        var tuesday = view.Days.Single(day => day.Date == Tuesday);

        Assert.Equal("Fixed", monday.Entries.Single().Name);
        Assert.Equal(DutyEntryKind.Fixed, monday.Entries.Single().Kind);
        Assert.Equal("A", view.Days.Single(day => day.Date == Monday).GroupName);
        Assert.Equal("A", view.Days.Single(day => day.Date == Wednesday).GroupName);
        // 固定人员只作用在指定的星期一，星期三仍回到组内成员。
        Assert.Equal("Inside", wednesday.Entries.Single().Name);
        Assert.Equal(DutyEntryKind.Member, wednesday.Entries.Single().Kind);
        Assert.Equal("B", tuesday.GroupName);
    }

    [Fact]
    public void FixedAssignment_AdditionalKeepsTheGroupMember()
    {
        var (context, a, _, _) = CreateFixed();
        var post = context.AddPost("Sweep", 0);
        var inside = context.AddMember("Inside");
        var extra = context.AddMember("Extra");
        context.Assign(a, post, inside);
        context.Document.FixedAssignments.Add(new DutyFixedAssignment
        {
            PostId = post.Id,
            MemberId = extra.RecordId,
            Weekdays = DutyWeekdays.All,
            Relation = DutyFixedRelation.Additional
        });

        var cell = context.CreateScheduler().BuildDay(Monday)!.Cells.Single();

        Assert.Equal(new[] { "Inside", "Extra" }, cell.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public void PersonSwap_ReplacesOnlyTheNamedMemberForTheDay()
    {
        var (context, a, _, _) = CreateFixed();
        var post = context.AddPost("Sweep", 0);
        var inside = context.AddMember("Inside");
        var substitute = context.AddMember("Substitute");
        context.Assign(a, post, inside);
        context.Document.Adjustments.Add(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.PersonSwap,
            PostId = post.Id,
            FromMemberId = inside.RecordId,
            ToMemberId = substitute.RecordId,
            Start = Monday,
            End = Monday
        });

        var scheduler = context.CreateScheduler();
        var monday = scheduler.BuildDay(Monday)!.Cells.Single();
        var wednesday = scheduler.BuildDay(Wednesday)!.Cells.Single();

        Assert.Equal("Substitute", monday.Entries.Single().Name);
        Assert.Equal(DutyEntryKind.Substitute, monday.Entries.Single().Kind);
        Assert.Equal("Inside", wednesday.Entries.Single().Name);
    }

    [Fact]
    public void PersonAdd_AppendsForTheDay()
    {
        var (context, a, _, _) = CreateFixed();
        var post = context.AddPost("Sweep", 0);
        var inside = context.AddMember("Inside");
        var added = context.AddMember("Added");
        context.Assign(a, post, inside);
        context.Document.Adjustments.Add(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.PersonAdd,
            PostId = post.Id,
            ToMemberId = added.RecordId,
            Start = Monday,
            End = Monday
        });

        var cell = context.CreateScheduler().BuildDay(Monday)!.Cells.Single();

        Assert.Equal(new[] { "Inside", "Added" }, cell.Entries.Select(entry => entry.Name));
        Assert.Equal(DutyEntryKind.Added, cell.Entries[1].Kind);
    }

    [Fact]
    public void DayWithoutAssignments_IsMarkedUnassigned()
    {
        var (context, a, _, _) = CreateFixed();
        var post = context.AddPost("Sweep", 0);
        context.AddMember("Inside");
        context.Assign(a, post);

        var day = context.CreateScheduler().BuildDay(Monday)!;

        Assert.Empty(day.Cells.Single().Entries);
        Assert.True(day.HasMarker(DutyDayMarker.Unassigned));
    }

    [Fact]
    public void SameMemberInTwoPosts_IsMarkedConflict()
    {
        var (context, a, _, _) = CreateFixed();
        var first = context.AddPost("Sweep", 0);
        var second = context.AddPost("Mop", 1);
        var member = context.AddMember("Inside");
        context.Assign(a, first, member);
        context.Assign(a, second, member);

        var day = context.CreateScheduler().BuildDay(Monday)!;

        Assert.Equal(2, day.Cells.Count);
        Assert.True(day.HasMarker(DutyDayMarker.Conflict));
    }

    [Fact]
    public void TodayMarker_IsSetOnlyForTheGivenDate()
    {
        var (context, _, _, _) = CreateFixed();
        var scheduler = context.CreateScheduler();

        Assert.True(scheduler.BuildDay(Monday, Monday)!.HasMarker(DutyDayMarker.Today));
        Assert.False(scheduler.BuildDay(Monday, Tuesday)!.HasMarker(DutyDayMarker.Today));
    }

    [Fact]
    public void NonDutyDay_CarriesHolidayMarkerAndNoGroup()
    {
        var (context, _, _, _) = CreateFixed();
        var calendar = new TestDutyHolidayCalendar();
        calendar.LegalHolidays.Add(Monday);

        var day = context.CreateScheduler(calendar).BuildDay(Monday)!;

        Assert.False(day.HasDuty);
        Assert.Null(day.GroupId);
        Assert.True(day.HasMarker(DutyDayMarker.LegalHoliday));
    }
}
