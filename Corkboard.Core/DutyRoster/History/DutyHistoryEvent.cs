namespace Corkboard.Core.DutyRoster.History;

/// <summary>历史事件类型。数字落盘，只能在末尾增删。</summary>
public enum DutyHistoryEventKind
{
    /// <summary>导入/替换了整份名单。</summary>
    RosterImported = 0,

    /// <summary>小组代班（某天换成另一个小组）。</summary>
    GroupAssign = 1,

    /// <summary>小组对调（两个小组互换某段日期）。</summary>
    GroupSwap = 2,

    /// <summary>小组加罚（延长若干值日天）。</summary>
    GroupPenalty = 3,

    /// <summary>个人兑换（换掉某天某个职务的人）。</summary>
    PersonSwap = 4,

    /// <summary>个人增设（某天某个职务加一个人）。</summary>
    PersonAdd = 5,

    /// <summary>某天强制值日 / 强制不值日。</summary>
    DayOverride = 6,

    /// <summary>新增固定人员。</summary>
    FixedAssignmentAdded = 7,

    /// <summary>删除固定人员。</summary>
    FixedAssignmentRemoved = 8,

    /// <summary>撤销（停用）一条调整。</summary>
    AdjustmentRevoked = 9
}

/// <summary>
///     值日表历史里的一条事件：一行一条，落 <c>data/history/duty/&lt;yyyy-MM-dd&gt;.jsonl</c>。
///     <para>
///         除了原始对象（<see cref="Adjustment" /> 等）还留了当时的名称快照——名单被重新导入后，
///         历史里仍然能显示「A 组」「擦柜子」，不用去追已经删掉的人。
///     </para>
/// </summary>
public sealed class DutyHistoryEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DutyHistoryEventKind Kind { get; set; }

    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;

    /// <summary>事件针对的日期（导入事件为 <see cref="DateOnly.MinValue" />）。</summary>
    public DateOnly Date { get; set; }

    public string GroupName { get; set; } = string.Empty;

    public string TargetGroupName { get; set; } = string.Empty;

    public string PostName { get; set; } = string.Empty;

    public string FromMemberName { get; set; } = string.Empty;

    public string ToMemberName { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    /// <summary>导入人数（只对 <see cref="DutyHistoryEventKind.RosterImported" /> 有意义）。</summary>
    public int MemberCount { get; set; }

    /// <summary>调整的原始对象（撤销、加罚、兑换、指定）。</summary>
    public DutyAdjustment? Adjustment { get; set; }

    public DutyDayOverride? DayOverride { get; set; }

    public DutyFixedAssignment? FixedAssignment { get; set; }

    public DutyRosterSource? Source { get; set; }
}
