using Corkboard.Core.DutyRoster;

namespace Corkboard.Core.Tests;

/// <summary>值日日期判定：星期规则、节假日、补班、手动覆盖的优先级。</summary>
public class DutyCalendarTests
{
    private static readonly DateOnly Monday = DutyTestContext.Day(1, 5);
    private static readonly DateOnly Tuesday = DutyTestContext.Day(1, 6);
    private static readonly DateOnly Saturday = DutyTestContext.Day(1, 10);

    [Fact]
    public void FixedWeekdayMode_OnlyDutyOnMappedWeekdays()
    {
        var context = new DutyTestContext();
        var group = context.AddGroup("A", 0);
        context.Schedule.Mode = DutyScheduleMode.FixedWeekday;
        context.Schedule.Weekdays = DutyWeekdays.ToBit(DayOfWeek.Monday);
        context.Schedule.FixedWeekdayMap[(int)DayOfWeek.Monday] = group.Id;

        var scheduler = context.CreateScheduler();

        Assert.True(scheduler.IsDutyDay(Monday));
        Assert.False(scheduler.IsDutyDay(Tuesday));
        Assert.False(scheduler.IsDutyDay(Saturday));
    }

    [Fact]
    public void LegalHoliday_IsSkippedOnlyWhenSwitchIsOn()
    {
        var context = new DutyTestContext();
        context.AddGroup("A", 0);
        var calendar = new TestDutyHolidayCalendar();
        calendar.LegalHolidays.Add(Monday);

        context.Schedule.SkipLegalHolidays = true;
        Assert.False(context.CreateScheduler(calendar).IsDutyDay(Monday));

        context.Schedule.SkipLegalHolidays = false;
        Assert.True(context.CreateScheduler(calendar).IsDutyDay(Monday));
    }

    [Fact]
    public void MakeupWorkday_IsDutyEvenOnWeekend_UnlessSwitchIsOff()
    {
        var context = new DutyTestContext();
        context.AddGroup("A", 0);
        var calendar = new TestDutyHolidayCalendar();
        calendar.MakeupWorkdays.Add(Saturday);

        context.Schedule.WorkOnMakeupDays = true;
        Assert.True(context.CreateScheduler(calendar).IsDutyDay(Saturday));

        context.Schedule.WorkOnMakeupDays = false;
        Assert.False(context.CreateScheduler(calendar).IsDutyDay(Saturday));
    }

    [Fact]
    public void Vacation_IsAlwaysSkipped()
    {
        var context = new DutyTestContext();
        context.AddGroup("A", 0);
        var calendar = new TestDutyHolidayCalendar();
        calendar.Vacations.Add(Monday);

        Assert.False(context.CreateScheduler(calendar).IsDutyDay(Monday));
    }

    [Fact]
    public void DayOverride_WinsOverEverything()
    {
        var context = new DutyTestContext();
        context.AddGroup("A", 0);
        var calendar = new TestDutyHolidayCalendar();
        calendar.LegalHolidays.Add(Monday);
        context.Document.DayOverrides.Add(new DutyDayOverride
        {
            Date = Monday,
            State = DutyDayOverrideState.Duty
        });
        context.Document.DayOverrides.Add(new DutyDayOverride
        {
            Date = Saturday,
            State = DutyDayOverrideState.Off
        });

        var scheduler = context.CreateScheduler(calendar);

        Assert.True(scheduler.IsDutyDay(Monday));
        Assert.False(scheduler.IsDutyDay(Saturday));
    }

    [Fact]
    public void EnumerateDutyDays_SkipsWeekendsAndHolidays()
    {
        var context = new DutyTestContext();
        context.AddGroup("A", 0);
        var calendar = new TestDutyHolidayCalendar();
        calendar.LegalHolidays.Add(DutyTestContext.Day(1, 7));

        var days = DutyCalendar.EnumerateDutyDays(
            DutyTestContext.Day(1, 5),
            DutyTestContext.Day(1, 9),
            context.Schedule,
            calendar);

        Assert.Equal(
            new[] { DutyTestContext.Day(1, 5), DutyTestContext.Day(1, 6), DutyTestContext.Day(1, 8), DutyTestContext.Day(1, 9) },
            days);
    }

    [Fact]
    public void WeekdaysMask_SupportsCustomWeekdaySets()
    {
        var context = new DutyTestContext();
        context.AddGroup("A", 0);
        context.Schedule.Weekdays = DutyWeekdays.ToBit(DayOfWeek.Monday)
                                    | DutyWeekdays.ToBit(DayOfWeek.Wednesday);

        var scheduler = context.CreateScheduler();

        Assert.True(scheduler.IsDutyDay(Monday));
        Assert.False(scheduler.IsDutyDay(Tuesday));
        Assert.True(scheduler.IsDutyDay(DutyTestContext.Day(1, 7)));
    }
}
