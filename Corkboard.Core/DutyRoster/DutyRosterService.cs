using Corkboard.Core.DutyRoster.History;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;

namespace Corkboard.Core.DutyRoster;

/// <summary>
///     值日表业务数据的读写入口：持有唯一一份 <see cref="DutyRosterDocument" />，
///     所有增删改都走这里，改完显式 <see cref="Save" />。
///     <para>
///         和设置不同：设置项由 <c>ConfigHandlerBase</c> 自动落盘，这份文档不自动保存——
///         表格编辑通常一次改多条，攒完一次写盘更省、也避免半成品落盘。
///     </para>
///     <para>
///         兑换、加罚、指定、导入这些「事后能追溯」的动作除了写文档，还会往
///         <see cref="IDutyHistoryStore" /> 追加一条事件（临时层文件）。
///     </para>
/// </summary>
public sealed class DutyRosterService
{
    private readonly ConfigServiceBase _configService;
    private readonly IDutyHistoryStore? _history;
    private readonly ILogger<DutyRosterService>? _logger;

    public DutyRosterService(
        ConfigServiceBase configService,
        IDutyHistoryStore? history = null,
        ILogger<DutyRosterService>? logger = null)
    {
        _configService = configService;
        _history = history;
        _logger = logger;

        DocumentExists = _configService.IsConfigExists(new DutyRosterDocument());
        Document = _configService.LoadConfig(new DutyRosterDocument());
        Document.Normalize();
        if (Document.UpdatedAt == default)
            Document.UpdatedAt = DateTimeOffset.Now;
    }

    /// <summary>当前文档。只通过本服务修改，不要直接替换引用。</summary>
    public DutyRosterDocument Document { get; private set; }

    /// <summary>构造时数据文件是否已经存在（区分「用户建的」与「刚打底的」）。</summary>
    public bool DocumentExists { get; private set; }

    /// <summary>文档内容变化（保存后触发），供界面与服务清缓存。</summary>
    public event EventHandler? Changed;

    public DutyMember? FindMember(Guid recordId) =>
        Document.Members.Find(member => member.RecordId == recordId);

    public DutyGroup? FindGroup(Guid groupId) =>
        Document.Groups.Find(group => group.Id == groupId);

    public DutyPost? FindPost(Guid postId) =>
        Document.Posts.Find(post => post.Id == postId);

    /// <summary>有效成员（未停用、姓名非空）。</summary>
    public IReadOnlyList<DutyMember> ActiveMembers =>
        Document.Members.Where(member => member.IsActive && !string.IsNullOrWhiteSpace(member.Name)).ToList();

    /// <summary>某个小组的成员（按名单顺序）。</summary>
    public IReadOnlyList<DutyMember> MembersOf(Guid groupId)
    {
        var group = FindGroup(groupId);
        if (group is null)
            return [];

        var byId = Document.Members.ToDictionary(member => member.RecordId);
        return group.MemberIds
            .Where(byId.ContainsKey)
            .Select(memberId => byId[memberId])
            .ToList();
    }

    /// <summary>规范化 + 落盘 + 通知。写盘走 <c>FileConfigService</c> 的原子替换。</summary>
    public void Save()
    {
        Document.Normalize();
        Document.UpdatedAt = DateTimeOffset.Now;
        Backup();
        _configService.SaveConfig(Document);
        DocumentExists = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>重新从磁盘加载（导入/外部修改后使用）。</summary>
    public void Reload()
    {
        Document = _configService.LoadConfig(new DutyRosterDocument());
        Document.Normalize();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public DutyGroup AddGroup(string name)
    {
        var group = new DutyGroup
        {
            Name = name?.Trim() ?? string.Empty,
            Order = Document.Groups.Count == 0 ? 0 : Document.Groups.Max(item => item.Order) + 1
        };
        Document.Groups.Add(group);
        Save();
        return group;
    }

    public void RemoveGroup(Guid groupId)
    {
        var group = FindGroup(groupId);
        if (group is null)
            return;

        Document.Groups.Remove(group);
        Document.Schedule.FixedWeekdayMap = Document.Schedule.FixedWeekdayMap
            .Where(pair => pair.Value != groupId)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (Document.Schedule.Rotation.StartGroupId == groupId)
            Document.Schedule.Rotation.StartGroupId = null;

        Save();
    }

    public void RenameGroup(Guid groupId, string name)
    {
        if (FindGroup(groupId) is not { } group)
            return;

        group.Name = name?.Trim() ?? string.Empty;
        Save();
    }

    /// <summary>按给定顺序重排小组（写入 0..n-1 的 <see cref="DutyGroup.Order" />）。</summary>
    public void ReorderGroups(IReadOnlyList<Guid> orderedGroupIds)
    {
        var order = 0;
        foreach (var groupId in orderedGroupIds)
        {
            if (FindGroup(groupId) is { } group)
                group.Order = order++;
        }

        foreach (var group in Document.Groups.Where(group => !orderedGroupIds.Contains(group.Id)))
            group.Order = order++;

        Save();
    }

    public void SetGroupMembers(Guid groupId, IEnumerable<Guid> memberIds)
    {
        var group = FindGroup(groupId);
        if (group is null)
            return;

        var known = Document.Members.Select(member => member.RecordId).ToHashSet();
        group.MemberIds = memberIds.Where(known.Contains).Distinct().ToList();
        Save();
    }

    public DutyPost AddPost(string name)
    {
        var post = new DutyPost
        {
            Name = name?.Trim() ?? string.Empty,
            Order = Document.Posts.Count == 0 ? 0 : Document.Posts.Max(item => item.Order) + 1
        };
        Document.Posts.Add(post);
        Save();
        return post;
    }

    public void RemovePost(Guid postId)
    {
        var post = FindPost(postId);
        if (post is null)
            return;

        Document.Posts.Remove(post);
        foreach (var group in Document.Groups)
            group.PositionAssignments.RemoveAll(assignment => assignment.PostId == postId);

        Document.FixedAssignments.RemoveAll(assignment => assignment.PostId == postId);
        Document.Adjustments.RemoveAll(adjustment =>
            adjustment.PostId == postId
            && adjustment.Kind is DutyAdjustmentKind.PersonSwap or DutyAdjustmentKind.PersonAdd);

        Save();
    }

    public void RenamePost(Guid postId, string name)
    {
        if (FindPost(postId) is not { } post)
            return;

        post.Name = name?.Trim() ?? string.Empty;
        Save();
    }

    /// <summary>按给定顺序重排职务（写入 0..n-1 的 <see cref="DutyPost.Order" />）。</summary>
    public void ReorderPosts(IReadOnlyList<Guid> orderedPostIds)
    {
        var order = 0;
        foreach (var postId in orderedPostIds)
        {
            if (FindPost(postId) is { } post)
                post.Order = order++;
        }

        foreach (var post in Document.Posts.Where(post => !orderedPostIds.Contains(post.Id)))
            post.Order = order++;

        Save();
    }

    public void SetPostDefaultMemberCount(Guid postId, int count)
    {
        if (FindPost(postId) is not { } post)
            return;

        post.DefaultMemberCount = Math.Clamp(count, 1, 20);
        Save();
    }

    public void SetPositionMembers(Guid groupId, Guid postId, IEnumerable<Guid> memberIds)
    {
        var group = FindGroup(groupId);
        if (group is null || FindPost(postId) is null)
            return;

        var known = group.MemberIds.ToHashSet();
        var cleaned = memberIds.Where(known.Contains).Distinct().ToList();
        var assignment = group.PositionAssignments.Find(item => item.PostId == postId);
        if (assignment is null)
        {
            group.PositionAssignments.Add(new DutyPositionAssignment
            {
                PostId = postId,
                MemberIds = cleaned
            });
        }
        else
        {
            assignment.MemberIds = cleaned;
        }

        Save();
    }

    /// <summary>手工加一个人（导入之外唯一的人口来源）。</summary>
    public DutyMember AddMember(string name, string id = "", string gender = "", string tags = "")
    {
        var member = new DutyMember
        {
            Name = name?.Trim() ?? string.Empty,
            Id = id?.Trim() ?? string.Empty,
            Gender = gender?.Trim() ?? string.Empty,
            Tags = tags?.Trim() ?? string.Empty
        };
        Document.Members.Add(member);
        Save();
        return member;
    }

    public void UpdateMember(Guid recordId, string name, string id, string gender, bool isActive)
    {
        if (FindMember(recordId) is not { } member)
            return;

        member.Name = name?.Trim() ?? string.Empty;
        member.Id = id?.Trim() ?? string.Empty;
        member.Gender = gender?.Trim() ?? string.Empty;
        member.IsActive = isActive;
        Save();
    }

    /// <summary>
    ///     一次提交成员的全部可编辑字段（序号、姓名、性别、标签、是否参加值日、所属小组），只落一次盘。
    ///     <para>
    ///         小组归属的权威在 <see cref="DutyGroup.MemberIds" />，<see cref="DutyMember.GroupId" /> 只是由
    ///         <see cref="DutyRosterDocument.Normalize" /> 反填的冗余字段——所以**不能**直接改
    ///         <c>member.GroupId</c> 再保存（会被 Normalize 清掉），必须走这里改组的成员名单。
    ///     </para>
    /// </summary>
    public void UpdateMemberProfile(
        Guid recordId,
        string name,
        string id,
        string gender,
        string tags,
        bool isActive,
        Guid? groupId)
    {
        if (FindMember(recordId) is not { } member)
            return;

        member.Name = name?.Trim() ?? string.Empty;
        member.Id = id?.Trim() ?? string.Empty;
        member.Gender = gender?.Trim() ?? string.Empty;
        member.Tags = tags?.Trim() ?? string.Empty;
        member.IsActive = isActive;
        AssignGroupMembership(recordId, groupId);
        Save();
    }

    /// <summary>把成员挪到另一个小组；<paramref name="groupId" /> 为空表示取消分组。</summary>
    public void SetMemberGroup(Guid recordId, Guid? groupId)
    {
        if (FindMember(recordId) is null)
            return;

        AssignGroupMembership(recordId, groupId);
        Save();
    }

    /// <summary>只切「是否参加值日」这一个开关（表格里的勾选框走这里）。</summary>
    public void SetMemberActive(Guid recordId, bool isActive)
    {
        if (FindMember(recordId) is not { } member)
            return;

        if (member.IsActive == isActive)
            return;

        member.IsActive = isActive;
        Save();
    }

    /// <summary>内部：改成员的小组归属（含清掉旧组的分工引用），不落盘。</summary>
    private void AssignGroupMembership(Guid recordId, Guid? groupId)
    {
        var current = Document.Groups.FirstOrDefault(group => group.MemberIds.Contains(recordId));
        if (current?.Id == groupId)
            return;

        foreach (var group in Document.Groups)
        {
            group.MemberIds.Remove(recordId);
            if (group.Id == groupId)
                continue;

            // 换了小组，旧组里的组内分工也要把这个人摘掉（组内职务是「本组成员」的子集）。
            foreach (var assignment in group.PositionAssignments)
                assignment.MemberIds.Remove(recordId);
        }

        if (groupId is { } targetId && FindGroup(targetId) is { } target)
            target.MemberIds.Add(recordId);
    }

    /// <summary>改「当前名单」的名字（写在来源信息的 <see cref="DutyRosterSource.RosterName" /> 里）。</summary>
    public void RenameRoster(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (Document.Source.RosterName == trimmed)
            return;

        Document.Source.RosterName = trimmed;
        Save();
    }

    /// <summary>清空名单（连同各组的人数与分工），只落一次盘。</summary>
    public void ClearMembers()
    {
        Document.Members.Clear();
        foreach (var group in Document.Groups)
        {
            group.MemberIds.Clear();
            foreach (var assignment in group.PositionAssignments)
                assignment.MemberIds.Clear();
        }

        Document.FixedAssignments.Clear();
        Save();
    }

    public void RemoveMember(Guid recordId)
    {
        var member = FindMember(recordId);
        if (member is null)
            return;

        Document.Members.Remove(member);
        foreach (var group in Document.Groups)
        {
            group.MemberIds.Remove(recordId);
            foreach (var assignment in group.PositionAssignments)
                assignment.MemberIds.Remove(recordId);
        }

        Document.FixedAssignments.RemoveAll(assignment => assignment.MemberId == recordId);
        Save();
    }

    /// <summary>
    ///     导入整份名单（覆盖）。SR 的 <c>record_id</c> 会原样保留，
    ///     所以同一份名单再次导入时，小组归属与历史引用都还能对上。
    /// </summary>
    public void ApplyRoster(IReadOnlyList<DutyMember> members, DutyRosterSource source)
    {
        Document.Members = members
            .Where(member => member is not null)
            .GroupBy(member => member.RecordId)
            .Select(group => group.First())
            .ToList();

        source.DocumentVersion = DutyRosterDocument.CurrentVersion;
        if (source.ImportedAt == default)
            source.ImportedAt = DateTimeOffset.Now;

        Document.Source = source;
        Save();
        AppendHistory(new DutyHistoryEvent
        {
            Kind = DutyHistoryEventKind.RosterImported,
            MemberCount = Document.Members.Count,
            Note = source.RosterName,
            Source = source
        });
    }

    /// <summary>
    ///     给还没分组的成员按顺序均摊到各组（只在小组已存在、成员未分组时使用）。
    ///     返回实际分配的人和组。
    /// </summary>
    public IReadOnlyDictionary<Guid, Guid> AutoAssignUngroupedMembers()
    {
        var groups = Document.OrderedGroups;
        var result = new Dictionary<Guid, Guid>();
        if (groups.Count == 0)
            return result;

        var cursor = 0;
        foreach (var member in ActiveMembers.Where(member => member.GroupId is null))
        {
            var group = groups[cursor % groups.Count];
            group.MemberIds.Add(member.RecordId);
            result[member.RecordId] = group.Id;
            cursor++;
        }

        Save();
        return result;
    }

    /// <summary>改排班规则（模式、值日星期、轮流参数、节假日开关），改完统一保存。</summary>
    public void UpdateSchedule(Action<DutyScheduleConfig> mutation)
    {
        mutation?.Invoke(Document.Schedule);
        Save();
    }

    /// <summary>固定周几模式：给某个星期指定小组；<paramref name="groupId" /> 为空表示清掉。</summary>
    public void SetFixedWeekday(DayOfWeek weekday, Guid? groupId)
    {
        var key = (int)weekday;
        if (groupId is { } id && FindGroup(id) is not null)
            Document.Schedule.FixedWeekdayMap[key] = id;
        else
            Document.Schedule.FixedWeekdayMap.Remove(key);

        Save();
    }

    /// <summary>某天强制值日 / 强制不值日；<see cref="DutyDayOverrideState.Auto" /> 表示恢复自动判定。</summary>
    public DutyDayOverride? SetDayOverride(DateOnly date, DutyDayOverrideState state, string note = "")
    {
        var existing = Document.DayOverrides.Find(item => item.Date == date);
        if (state == DutyDayOverrideState.Auto)
        {
            if (existing is not null)
            {
                Document.DayOverrides.Remove(existing);
                Save();
                AppendHistory(new DutyHistoryEvent
                {
                    Kind = DutyHistoryEventKind.DayOverride,
                    Date = date,
                    Note = note,
                    DayOverride = new DutyDayOverride { Date = date, State = state, Note = note }
                });
            }

            return null;
        }

        var overridden = existing ?? new DutyDayOverride { Date = date };
        overridden.State = state;
        overridden.Note = note?.Trim() ?? string.Empty;
        if (existing is null)
            Document.DayOverrides.Add(overridden);

        Save();
        AppendHistory(new DutyHistoryEvent
        {
            Kind = DutyHistoryEventKind.DayOverride,
            Date = date,
            Note = overridden.Note,
            DayOverride = new DutyDayOverride
            {
                Date = date,
                State = overridden.State,
                Note = overridden.Note
            }
        });

        return overridden;
    }

    /// <summary>新增一条兑换 / 加罚 / 指定；只写这一条，排班时叠加。</summary>
    public DutyAdjustment AddAdjustment(DutyAdjustment adjustment)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

        adjustment.Id = adjustment.Id == Guid.Empty ? Guid.NewGuid() : adjustment.Id;
        if (adjustment.CreatedAt == default)
            adjustment.CreatedAt = DateTimeOffset.Now;

        Document.Adjustments.Add(adjustment);
        Save();
        AppendHistory(CreateHistoryEvent(adjustment));
        return adjustment;
    }

    /// <summary>停用（撤销）一条调整；不删除，历史还能查到。</summary>
    public void SetAdjustmentEnabled(Guid adjustmentId, bool enabled)
    {
        var adjustment = Document.Adjustments.Find(item => item.Id == adjustmentId);
        if (adjustment is null || adjustment.IsEnabled == enabled)
            return;

        adjustment.IsEnabled = enabled;
        Save();
        if (!enabled)
        {
            var historyEvent = CreateHistoryEvent(adjustment);
            historyEvent.Kind = DutyHistoryEventKind.AdjustmentRevoked;
            AppendHistory(historyEvent);
        }
    }

    /// <summary>新增固定人员（某职务在指定星期固定由某人打扫）。</summary>
    public DutyFixedAssignment AddFixedAssignment(DutyFixedAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        assignment.Id = assignment.Id == Guid.Empty ? Guid.NewGuid() : assignment.Id;
        Document.FixedAssignments.Add(assignment);
        Save();
        AppendHistory(new DutyHistoryEvent
        {
            Kind = DutyHistoryEventKind.FixedAssignmentAdded,
            Date = assignment.StartDate ?? DateOnly.MinValue,
            PostName = FindPost(assignment.PostId)?.Name ?? string.Empty,
            ToMemberName = FindMember(assignment.MemberId)?.Name ?? string.Empty,
            FixedAssignment = assignment
        });
        return assignment;
    }

    /// <summary>删除固定人员。</summary>
    public void RemoveFixedAssignment(Guid assignmentId)
    {
        var assignment = Document.FixedAssignments.Find(item => item.Id == assignmentId);
        if (assignment is null)
            return;

        Document.FixedAssignments.Remove(assignment);
        Save();
        AppendHistory(new DutyHistoryEvent
        {
            Kind = DutyHistoryEventKind.FixedAssignmentRemoved,
            Date = assignment.StartDate ?? DateOnly.MinValue,
            PostName = FindPost(assignment.PostId)?.Name ?? string.Empty,
            ToMemberName = FindMember(assignment.MemberId)?.Name ?? string.Empty,
            FixedAssignment = assignment
        });
    }

    private DutyHistoryEvent CreateHistoryEvent(DutyAdjustment adjustment)
    {
        var kind = adjustment.Kind switch
        {
            DutyAdjustmentKind.GroupAssign => DutyHistoryEventKind.GroupAssign,
            DutyAdjustmentKind.GroupSwap => DutyHistoryEventKind.GroupSwap,
            DutyAdjustmentKind.GroupPenalty => DutyHistoryEventKind.GroupPenalty,
            DutyAdjustmentKind.PersonSwap => DutyHistoryEventKind.PersonSwap,
            DutyAdjustmentKind.PersonAdd => DutyHistoryEventKind.PersonAdd,
            _ => DutyHistoryEventKind.GroupAssign
        };

        return new DutyHistoryEvent
        {
            Kind = kind,
            Date = adjustment.AnchorDate ?? adjustment.Start,
            GroupName = GroupNameOf(adjustment.GroupId),
            TargetGroupName = GroupNameOf(adjustment.TargetGroupId),
            PostName = PostNameOf(adjustment.PostId),
            FromMemberName = MemberNameOf(adjustment.FromMemberId),
            ToMemberName = MemberNameOf(adjustment.ToMemberId),
            Note = adjustment.Note,
            Adjustment = adjustment
        };
    }

    private string GroupNameOf(Guid? groupId) =>
        groupId is { } value ? FindGroup(value)?.Name ?? string.Empty : string.Empty;

    private string PostNameOf(Guid? postId) =>
        postId is { } value ? FindPost(value)?.Name ?? string.Empty : string.Empty;

    private string MemberNameOf(Guid? recordId) =>
        recordId is { } value ? FindMember(value)?.Name ?? string.Empty : string.Empty;

    private void AppendHistory(DutyHistoryEvent historyEvent)
    {
        if (_history is null)
            return;

        try
        {
            _history.Append(historyEvent);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "写值日表历史失败");
        }
    }

    private void Backup()
    {
        try
        {
            var path = Document.ConfigFilePath;
            if (!File.Exists(path))
                return;

            File.Copy(path, path + ".bak", overwrite: true);
        }
        catch (Exception ex)
        {
            // 备份失败不能挡住保存本身。
            _logger?.LogWarning(ex, "值日表备份失败，继续保存");
        }
    }
}
