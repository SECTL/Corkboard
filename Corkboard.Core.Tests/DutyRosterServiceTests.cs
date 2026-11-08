using Corkboard.Core.Abstraction;
using Corkboard.Core.DutyRoster;
using Corkboard.Shared.Abstraction;

namespace Corkboard.Core.Tests;

/// <summary>
///     值日表名单管理的服务层：一次提交成员资料只落一次盘、改小组走组内名单、
///     清空名单不留下悬空的组内分工。用内存配置服务替换磁盘实现，只验证行为与落盘次数。
/// </summary>
public class DutyRosterServiceTests
{
    private sealed class InMemoryConfigService : ConfigServiceBase
    {
        public Dictionary<string, ConfigBase> Saved { get; } = [];

        public int SaveCount { get; private set; }

        public override bool IsConfigExists<T>(T fallback) => Saved.ContainsKey(fallback.ConfigFilePath);

        public override T LoadConfig<T>(T fallback)
        {
            return Saved.TryGetValue(fallback.ConfigFilePath, out var stored) ? (T)stored : fallback;
        }

        public override void SaveConfig<T>(T config)
        {
            SaveCount++;
            Saved[config.ConfigFilePath] = config;
        }

        public override void DeleteConfig<T>(T config)
        {
            Saved.Remove(config.ConfigFilePath);
        }
    }

    private static DutyRosterService CreateService(out InMemoryConfigService config)
    {
        config = new InMemoryConfigService();
        return new DutyRosterService(config);
    }

    [Fact]
    public void AddMember_StoresTags()
    {
        var service = CreateService(out _);
        var member = service.AddMember("张三", "01", "男", "走读, 班长");

        Assert.Equal("走读, 班长", service.FindMember(member.RecordId)!.Tags);
    }

    [Fact]
    public void UpdateMemberProfile_WritesEveryFieldAndSavesOnce()
    {
        var service = CreateService(out var config);
        var group = service.AddGroup("一组");
        var member = service.AddMember("张三", "01", "男");
        var savesBefore = config.SaveCount;

        service.UpdateMemberProfile(member.RecordId, "张三", "02", "女", "走读", false, group.Id);

        Assert.Equal(savesBefore + 1, config.SaveCount);
        var updated = service.FindMember(member.RecordId)!;
        Assert.Equal("02", updated.Id);
        Assert.Equal("女", updated.Gender);
        Assert.Equal("走读", updated.Tags);
        Assert.False(updated.IsActive);
        Assert.Contains(member.RecordId, group.MemberIds);
        // GroupId 是 Normalize 反填的冗余字段，也要跟着对。
        Assert.Equal(group.Id, updated.GroupId);
    }

    [Fact]
    public void UpdateMemberProfile_MovingGroup_ClearsOldGroupAssignments()
    {
        var service = CreateService(out _);
        var first = service.AddGroup("一组");
        var second = service.AddGroup("二组");
        var post = service.AddPost("扫地");
        var member = service.AddMember("张三", "01");
        service.SetGroupMembers(first.Id, [member.RecordId]);
        service.SetPositionMembers(first.Id, post.Id, [member.RecordId]);

        service.UpdateMemberProfile(member.RecordId, "张三", "01", "", "", true, second.Id);

        Assert.DoesNotContain(member.RecordId, first.MemberIds);
        Assert.DoesNotContain(member.RecordId, first.PositionAssignments.Single().MemberIds);
        Assert.Contains(member.RecordId, second.MemberIds);
        Assert.Equal(second.Id, service.FindMember(member.RecordId)!.GroupId);
    }

    [Fact]
    public void UpdateMemberProfile_UnassigningGroup_RemovesMembership()
    {
        var service = CreateService(out _);
        var group = service.AddGroup("一组");
        var member = service.AddMember("张三", "01");
        service.SetGroupMembers(group.Id, [member.RecordId]);

        service.UpdateMemberProfile(member.RecordId, "张三", "01", "", "", true, null);

        Assert.DoesNotContain(member.RecordId, group.MemberIds);
        Assert.Null(service.FindMember(member.RecordId)!.GroupId);
    }

    [Fact]
    public void SetMemberGroup_OnlyTouchesGroupRelation()
    {
        var service = CreateService(out _);
        var group = service.AddGroup("一组");
        var member = service.AddMember("张三", "01", "男", "标签");

        service.SetMemberGroup(member.RecordId, group.Id);

        var updated = service.FindMember(member.RecordId)!;
        Assert.Equal("男", updated.Gender);
        Assert.Equal("标签", updated.Tags);
        Assert.Equal(group.Id, updated.GroupId);
    }

    [Fact]
    public void SetMemberActive_NoOpWhenValueUnchanged_AndSavesWhenChanged()
    {
        var service = CreateService(out var config);
        var member = service.AddMember("张三", "01");
        var savesBefore = config.SaveCount;

        service.SetMemberActive(member.RecordId, true);
        Assert.Equal(savesBefore, config.SaveCount);

        service.SetMemberActive(member.RecordId, false);
        Assert.Equal(savesBefore + 1, config.SaveCount);
        Assert.False(service.FindMember(member.RecordId)!.IsActive);
    }

    [Fact]
    public void RenameRoster_WritesSourceName()
    {
        var service = CreateService(out _);

        service.RenameRoster("  高二(3)班  ");

        Assert.Equal("高二(3)班", service.Document.Source.RosterName);
    }

    [Fact]
    public void ClearMembers_ClearsGroupsPositionAssignmentsAndFixedAssignments()
    {
        var service = CreateService(out _);
        var group = service.AddGroup("一组");
        var post = service.AddPost("扫地");
        var member = service.AddMember("张三", "01");
        service.SetGroupMembers(group.Id, [member.RecordId]);
        service.SetPositionMembers(group.Id, post.Id, [member.RecordId]);
        service.AddFixedAssignment(new DutyFixedAssignment
        {
            PostId = post.Id,
            MemberId = member.RecordId,
            Weekdays = DutyWeekdays.ToBit(DayOfWeek.Monday)
        });

        service.ClearMembers();

        Assert.Empty(service.Document.Members);
        Assert.Empty(group.MemberIds);
        Assert.Empty(group.PositionAssignments.Single().MemberIds);
        Assert.Empty(service.Document.FixedAssignments);
    }
}