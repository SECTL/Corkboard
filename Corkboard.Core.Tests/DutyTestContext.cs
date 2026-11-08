using Corkboard.Core.DutyRoster;

namespace Corkboard.Core.Tests;

/// <summary>值日表测试用的文档搭建器：小组、职务、成员、分配都走真实模型。</summary>
internal sealed class DutyTestContext
{
    public DutyRosterDocument Document { get; } = new();

    public DutyScheduleConfig Schedule => Document.Schedule;

    public static DateOnly Day(int month, int day) => new(2026, month, day);

    public DutyMember AddMember(string name)
    {
        var member = new DutyMember { Id = name, Name = name };
        Document.Members.Add(member);
        return member;
    }

    public DutyGroup AddGroup(string name, int order, params DutyMember[] members)
    {
        var group = new DutyGroup { Name = name, Order = order };
        group.MemberIds.AddRange(members.Select(member => member.RecordId));
        Document.Groups.Add(group);
        return group;
    }

    public DutyPost AddPost(string name, int order)
    {
        var post = new DutyPost { Name = name, Order = order };
        Document.Posts.Add(post);
        return post;
    }

    public void Assign(DutyGroup group, DutyPost post, params DutyMember[] members)
    {
        group.PositionAssignments.Add(new DutyPositionAssignment
        {
            PostId = post.Id,
            MemberIds = members.Select(member => member.RecordId).ToList()
        });
    }

    public DutyScheduler CreateScheduler(IDutyHolidayCalendar? calendar = null) =>
        new(Document, calendar);

    public static List<string> GroupNames(DutyScheduleView view) =>
        view.Days.Where(day => day.HasDuty).Select(day => day.GroupName).ToList();
}

/// <summary>可控的节假日日历：测试里直接往集合里塞日期。</summary>
internal sealed class TestDutyHolidayCalendar : IDutyHolidayCalendar
{
    public HashSet<DateOnly> LegalHolidays { get; } = [];

    public HashSet<DateOnly> MakeupWorkdays { get; } = [];

    public HashSet<DateOnly> Vacations { get; } = [];

    public bool IsLegalHoliday(DateOnly date) => LegalHolidays.Contains(date);

    public bool IsMakeupWorkday(DateOnly date) => MakeupWorkdays.Contains(date);

    public bool IsVacation(DateOnly date) => Vacations.Contains(date);
}
