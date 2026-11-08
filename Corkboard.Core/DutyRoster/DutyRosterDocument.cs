using System.Text.Json.Serialization;
using Corkboard.Shared;
using Corkboard.Shared.Abstraction;

namespace Corkboard.Core.DutyRoster;

/// <summary>
///     值日表的**全部业务数据**：<c>data/duty/duty.json</c>，一份文档。
///     <para>
///         名单是学生姓名，属于本机数据，不放进 <c>settings.json</c>，也不默认进云同步
///         （见 <c>CloudSyncExclusions</c> 的约定）。历史事件另存
///         <c>data/history/duty/&lt;yyyy-MM-dd&gt;.jsonl</c>。
///     </para>
/// </summary>
public sealed class DutyRosterDocument : ConfigBase
{
    /// <summary>数据根目录名，位于 <c>data/</c> 之下、与 <c>config/</c> 平级。</summary>
    public const string StorageDirectoryName = "duty";

    public const string StorageFileName = "duty.json";

    public const int CurrentVersion = 1;

    /// <summary>文档版本，用于以后迁移。只增不改语义。</summary>
    public int Version { get; set; } = CurrentVersion;

    public DutyRosterSource Source { get; set; } = new();

    public List<DutyMember> Members { get; set; } = [];

    public List<DutyGroup> Groups { get; set; } = [];

    public List<DutyPost> Posts { get; set; } = [];

    public List<DutyFixedAssignment> FixedAssignments { get; set; } = [];

    public DutyScheduleConfig Schedule { get; set; } = new();

    /// <summary>当前生效与历史的手动调整（停用而不删）。</summary>
    public List<DutyAdjustment> Adjustments { get; set; } = [];

    /// <summary>某天值日/不值日的强制覆盖。</summary>
    public List<DutyDayOverride> DayOverrides { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore]
    public override string ConfigFilePath => Utils.GetFilePath(StorageDirectoryName, StorageFileName);

    /// <summary>按 <see cref="DutyGroup.Order" /> 排好的小组（相同时按名称）。</summary>
    [JsonIgnore]
    public IReadOnlyList<DutyGroup> OrderedGroups =>
        Groups.OrderBy(group => group.Order).ThenBy(group => group.Name, StringComparer.CurrentCulture).ToList();

    /// <summary>按 <see cref="DutyPost.Order" /> 排好的职务。</summary>
    [JsonIgnore]
    public IReadOnlyList<DutyPost> OrderedPosts =>
        Posts.OrderBy(post => post.Order).ThenBy(post => post.Name, StringComparer.CurrentCulture).ToList();

    /// <summary>
    ///     读盘后统一收拾：去重、清理悬空引用、夹取越界值、重建冗余字段。
    ///     <para>
    ///         只在 <see cref="DutyRosterService" /> 加载后调用；保存前也调一次，
    ///         保证写到磁盘上的永远是自洽的文档。坏元素只做最小修剪，不整份丢弃。
    ///     </para>
    /// </summary>
    public void Normalize()
    {
        if (Version <= 0 || Version > CurrentVersion)
            Version = CurrentVersion;

        Source ??= new DutyRosterSource();

        Members ??= [];
        Groups ??= [];
        Posts ??= [];
        FixedAssignments ??= [];
        Adjustments ??= [];
        DayOverrides ??= [];
        Schedule ??= new DutyScheduleConfig();
        Schedule.Rotation ??= new DutyRotationConfig();

        NormalizeMembers();
        NormalizeGroups();
        NormalizePosts();
        NormalizeSchedule();
        NormalizeFixedAssignments();
        NormalizeAdjustments();
        NormalizeDayOverrides();
        RebuildMemberGroupIds();
    }

    private void NormalizeMembers()
    {
        var seen = new HashSet<Guid>();
        for (var index = Members.Count - 1; index >= 0; index--)
        {
            var member = Members[index];
            if (member is null || !seen.Add(member.RecordId))
            {
                Members.RemoveAt(index);
                continue;
            }

            if (member.RecordId == Guid.Empty)
            {
                member.RecordId = Guid.NewGuid();
                seen.Add(member.RecordId);
            }

            member.Id = member.Id?.Trim() ?? string.Empty;
            member.Name = member.Name?.Trim() ?? string.Empty;
            member.Gender = member.Gender?.Trim() ?? string.Empty;
            member.Tags = member.Tags?.Trim() ?? string.Empty;
            member.GroupId = null;
        }
    }

    private void NormalizeGroups()
    {
        var memberIds = Members.Select(member => member.RecordId).ToHashSet();
        var postIds = Posts.Select(post => post.Id).ToHashSet();
        var seen = new HashSet<Guid>();

        for (var index = Groups.Count - 1; index >= 0; index--)
        {
            var group = Groups[index];
            if (group is null || !seen.Add(group.Id))
            {
                Groups.RemoveAt(index);
                continue;
            }

            if (group.Id == Guid.Empty)
            {
                group.Id = Guid.NewGuid();
                seen.Add(group.Id);
            }

            group.Name = group.Name?.Trim() ?? string.Empty;
            group.Order = Math.Max(0, group.Order);
            group.MemberIds ??= [];
            group.PositionAssignments ??= [];

            group.MemberIds = group.MemberIds
                .Where(memberIds.Contains)
                .Distinct()
                .ToList();

            var assignmentSeen = new HashSet<Guid>();
            group.PositionAssignments = group.PositionAssignments
                .Where(assignment => assignment is not null && assignmentSeen.Add(assignment.PostId))
                .Where(assignment => postIds.Contains(assignment.PostId))
                .Select(assignment => new DutyPositionAssignment
                {
                    PostId = assignment.PostId,
                    MemberIds = (assignment.MemberIds ?? [])
                        .Where(group.MemberIds.Contains)
                        .Distinct()
                        .ToList()
                })
                .ToList();
        }
    }

    private void NormalizePosts()
    {
        var seen = new HashSet<Guid>();
        for (var index = Posts.Count - 1; index >= 0; index--)
        {
            var post = Posts[index];
            if (post is null || !seen.Add(post.Id))
            {
                Posts.RemoveAt(index);
                continue;
            }

            if (post.Id == Guid.Empty)
            {
                post.Id = Guid.NewGuid();
                seen.Add(post.Id);
            }

            post.Name = post.Name?.Trim() ?? string.Empty;
            post.Order = Math.Max(0, post.Order);
            post.DefaultMemberCount = Math.Clamp(post.DefaultMemberCount, 1, 20);
        }
    }

    private void NormalizeSchedule()
    {
        var schedule = Schedule;
        if (schedule.Mode is not (DutyScheduleMode.FixedWeekday or DutyScheduleMode.Rotation))
            schedule.Mode = DutyScheduleMode.FixedWeekday;

        schedule.Weekdays = DutyWeekdays.Normalize(schedule.Weekdays);

        if (schedule.PenaltyMode is not (DutyPenaltyMode.Swallow or DutyPenaltyMode.PushBack))
            schedule.PenaltyMode = DutyPenaltyMode.Swallow;

        var groupIds = Groups.Select(group => group.Id).ToHashSet();
        schedule.FixedWeekdayMap = (schedule.FixedWeekdayMap ?? [])
            .Where(pair => pair.Key is >= 0 and <= 6 && groupIds.Contains(pair.Value))
            .GroupBy(pair => pair.Key)
            .ToDictionary(entry => entry.Key, entry => entry.Last().Value);

        var rotation = schedule.Rotation;
        rotation.Direction = rotation.Direction < 0 ? -1 : 1;
        rotation.DaysPerGroup = Math.Clamp(rotation.DaysPerGroup, 1, 60);
        if (rotation.StartGroupId is { } start && !groupIds.Contains(start))
            rotation.StartGroupId = null;
    }

    private void NormalizeFixedAssignments()
    {
        var memberIds = Members.Select(member => member.RecordId).ToHashSet();
        var postIds = Posts.Select(post => post.Id).ToHashSet();
        var seen = new HashSet<Guid>();

        for (var index = FixedAssignments.Count - 1; index >= 0; index--)
        {
            var assignment = FixedAssignments[index];
            if (assignment is null
                || !memberIds.Contains(assignment.MemberId)
                || !postIds.Contains(assignment.PostId))
            {
                FixedAssignments.RemoveAt(index);
                continue;
            }

            if (assignment.Id == Guid.Empty || !seen.Add(assignment.Id))
                assignment.Id = Guid.NewGuid();

            assignment.Weekdays = DutyWeekdays.Normalize(assignment.Weekdays);

            if (assignment.StartDate is { } start && assignment.EndDate is { } end && end < start)
                assignment.EndDate = null;

            if (assignment.Relation is not (DutyFixedRelation.Replace or DutyFixedRelation.Additional))
                assignment.Relation = DutyFixedRelation.Replace;
        }
    }

    private void NormalizeAdjustments()
    {
        var seen = new HashSet<Guid>();
        for (var index = Adjustments.Count - 1; index >= 0; index--)
        {
            var adjustment = Adjustments[index];
            if (adjustment is null || !seen.Add(adjustment.Id))
            {
                Adjustments.RemoveAt(index);
                continue;
            }

            if (adjustment.Id == Guid.Empty)
            {
                adjustment.Id = Guid.NewGuid();
                seen.Add(adjustment.Id);
            }

            if (adjustment.Kind is < DutyAdjustmentKind.GroupAssign or > DutyAdjustmentKind.PersonAdd)
                adjustment.Kind = DutyAdjustmentKind.GroupAssign;

            if (adjustment.Lifetime is not (DutyLifetime.Temporary or DutyLifetime.Permanent))
                adjustment.Lifetime = DutyLifetime.Temporary;

            if (adjustment.Lifetime == DutyLifetime.Permanent)
                adjustment.End = null;
            else if (adjustment.End is { } end && end < adjustment.Start)
                adjustment.End = adjustment.Start;

            if (adjustment.Kind == DutyAdjustmentKind.GroupPenalty)
                adjustment.Days = Math.Clamp(adjustment.Days, 1, 14);
            else
                adjustment.Days = Math.Max(0, adjustment.Days);

            if (adjustment.PenaltyMode is not (null or DutyPenaltyMode.Swallow or DutyPenaltyMode.PushBack))
                adjustment.PenaltyMode = null;

            adjustment.Note = adjustment.Note?.Trim() ?? string.Empty;
        }
    }

    private void NormalizeDayOverrides()
    {
        var byDate = new Dictionary<DateOnly, DutyDayOverride>();
        foreach (var item in DayOverrides)
        {
            if (item is null || item.Date == default)
                continue;

            if (item.State is not (DutyDayOverrideState.Auto or DutyDayOverrideState.Duty or DutyDayOverrideState.Off))
                item.State = DutyDayOverrideState.Auto;

            item.Note = item.Note?.Trim() ?? string.Empty;
            byDate[item.Date] = item;
        }

        DayOverrides = byDate.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList();
    }

    private void RebuildMemberGroupIds()
    {
        var memberToGroup = new Dictionary<Guid, Guid>();
        foreach (var group in Groups)
        {
            foreach (var memberId in group.MemberIds)
                memberToGroup[memberId] = group.Id;
        }

        foreach (var member in Members)
            member.GroupId = memberToGroup.TryGetValue(member.RecordId, out var groupId) ? groupId : null;
    }
}
