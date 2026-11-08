using Corkboard.Core.DutyRoster;

namespace Corkboard.Core.Tests;

/// <summary>小组轮流：正/倒序、每段天数、加罚的「顺延」与「吞掉」两种处理。</summary>
public class DutyRotationTests
{
    private static readonly DateOnly Monday = DutyTestContext.Day(1, 5);

    private static (DutyTestContext Context, DutyGroup A, DutyGroup B, DutyGroup C) CreateRotation(
        int daysPerGroup = 2,
        int direction = 1,
        Guid? startGroupId = null,
        DateOnly? startDate = null)
    {
        var context = new DutyTestContext();
        var a = context.AddGroup("A", 0);
        var b = context.AddGroup("B", 1);
        var c = context.AddGroup("C", 2);
        context.Schedule.Mode = DutyScheduleMode.Rotation;
        context.Schedule.Weekdays = DutyWeekdays.Workdays;
        context.Schedule.Rotation.StartDate = startDate ?? Monday;
        context.Schedule.Rotation.StartGroupId = startGroupId;
        context.Schedule.Rotation.DaysPerGroup = daysPerGroup;
        context.Schedule.Rotation.Direction = direction;
        return (context, a, b, c);
    }

    private static List<string> GroupsFrom(DutyScheduleView view) =>
        DutyTestContext.GroupNames(view);

    [Fact]
    public void Rotation_CyclesGroupsByDutyDays()
    {
        var (context, a, _, _) = CreateRotation(startGroupId: null);
        var view = context.CreateScheduler().BuildRange(Monday, DutyTestContext.Day(1, 14));

        Assert.Equal(new[] { "A", "A", "B", "B", "C", "C", "A", "A" }, GroupsFrom(view));
        Assert.Equal(a.Id, view.Days[0].GroupId);
    }

    [Fact]
    public void Rotation_ReverseDirectionWalksBackwards()
    {
        var (context, _, b, _) = CreateRotation(direction: -1, startGroupId: null);
        context.Schedule.Rotation.StartGroupId = b.Id;
        var view = context.CreateScheduler().BuildRange(Monday, DutyTestContext.Day(1, 14));

        Assert.Equal(new[] { "B", "B", "A", "A", "C", "C", "B", "B" }, GroupsFrom(view));
    }

    [Fact]
    public void Rotation_OneGroupPerDay()
    {
        var (context, _, _, _) = CreateRotation(daysPerGroup: 1);
        var view = context.CreateScheduler().BuildRange(Monday, DutyTestContext.Day(1, 9));

        Assert.Equal(new[] { "A", "B", "C", "A", "B" }, GroupsFrom(view));
    }

    [Fact]
    public void Rotation_StartDateBeforeFirstDutyDay_LeavesEarlierDaysEmpty()
    {
        var (context, _, _, _) = CreateRotation(startDate: DutyTestContext.Day(1, 7));
        var view = context.CreateScheduler().BuildRange(Monday, DutyTestContext.Day(1, 9));

        Assert.Null(view.Days.Single(day => day.Date == Monday).GroupId);
        Assert.Equal(new[] { "A", "A", "B" }, GroupsFrom(view));
    }

    [Fact]
    public void Penalty_PushBack_ExtendsThePenalizedSegmentAndShiftsTheRest()
    {
        var (context, a, _, _) = CreateRotation();
        context.Document.Adjustments.Add(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.GroupPenalty,
            GroupId = a.Id,
            AnchorDate = Monday,
            Days = 2,
            PenaltyMode = DutyPenaltyMode.PushBack
        });

        var view = context.CreateScheduler().BuildRange(Monday, DutyTestContext.Day(1, 14));

        Assert.Equal(new[] { "A", "A", "A", "A", "B", "B", "C", "C" }, GroupsFrom(view));
        Assert.True(view.Days.Single(day => day.Date == DutyTestContext.Day(1, 7)).HasMarker(DutyDayMarker.Penalty));
    }

    [Fact]
    public void Penalty_Swallow_EatsIntoTheFollowingGroupsDays()
    {
        var (context, a, _, _) = CreateRotation();
        context.Document.Adjustments.Add(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.GroupPenalty,
            GroupId = a.Id,
            AnchorDate = Monday,
            Days = 2,
            PenaltyMode = DutyPenaltyMode.Swallow
        });

        var view = context.CreateScheduler().BuildRange(Monday, DutyTestContext.Day(1, 14));

        // A 延长 2 天，紧跟着的 B 段被整段吞掉，C 提前顶上，总周期不变。
        Assert.Equal(new[] { "A", "A", "A", "A", "C", "C", "A", "A" }, GroupsFrom(view));
        Assert.True(view.Days.Single(day => day.Date == DutyTestContext.Day(1, 8)).HasMarker(DutyDayMarker.Penalty));
    }

    [Fact]
    public void Penalty_UsesScheduleDefaultModeWhenAdjustmentDoesNotOverrideIt()
    {
        var (context, a, _, _) = CreateRotation();
        context.Schedule.PenaltyMode = DutyPenaltyMode.PushBack;
        context.Document.Adjustments.Add(new DutyAdjustment
        {
            Kind = DutyAdjustmentKind.GroupPenalty,
            GroupId = a.Id,
            AnchorDate = Monday,
            Days = 1
        });

        var view = context.CreateScheduler().BuildRange(Monday, DutyTestContext.Day(1, 9));

        Assert.Equal(new[] { "A", "A", "A", "B", "B" }, GroupsFrom(view));
    }

    [Fact]
    public void PreviewRotation_StartsFromTheRequestedDate()
    {
        var (context, _, _, _) = CreateRotation();
        var segments = context.CreateScheduler().PreviewRotation(DutyTestContext.Day(1, 12), 2);

        Assert.Equal(2, segments.Count);
        Assert.Equal("C", segments[0].GroupName);
        Assert.Equal(DutyTestContext.Day(1, 12), segments[0].End);
        Assert.Equal("A", segments[1].GroupName);
    }
}
