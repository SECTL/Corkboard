namespace Corkboard.Core.DutyRoster;

/// <summary>
///     值日小组。<see cref="Order" /> 就是「顺序」字段：轮流模式按它排队，
///     正序从这里正着走、倒序反着走。
/// </summary>
public sealed class DutyGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>排序值（0 起）。相同值时按名称稳定排序。</summary>
    public int Order { get; set; }

    /// <summary>组内成员（<see cref="DutyMember.RecordId" />）。权威关系在这里。</summary>
    public List<Guid> MemberIds { get; set; } = [];

    /// <summary>每个职务在这个小组里由谁负责。</summary>
    public List<DutyPositionAssignment> PositionAssignments { get; set; } = [];

    /// <summary>取某个职务在本组的成员（没配时返回空，由排班引擎决定怎么兜底）。</summary>
    public IReadOnlyList<Guid> GetAssignedMembers(Guid postId)
    {
        var assignment = PositionAssignments.Find(item => item.PostId == postId);
        return assignment?.MemberIds ?? [];
    }
}

/// <summary>小组内「职务 → 成员」的分配。</summary>
public sealed class DutyPositionAssignment
{
    public Guid PostId { get; set; }

    public List<Guid> MemberIds { get; set; } = [];
}
