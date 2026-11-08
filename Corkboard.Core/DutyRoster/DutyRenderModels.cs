namespace Corkboard.Core.DutyRoster;

/// <summary>值日表某一天要画的徽标。多个标记可以同时存在。</summary>
[Flags]
public enum DutyDayMarker
{
    None = 0,

    /// <summary>今天。</summary>
    Today = 1 << 0,

    /// <summary>法定节假日（不值日）。</summary>
    LegalHoliday = 1 << 1,

    /// <summary>调休补班日（照常值日）。</summary>
    MakeupWorkday = 1 << 2,

    /// <summary>用户自建假期区间（不值日）。</summary>
    Vacation = 1 << 3,

    /// <summary>有小组代班 / 对调。</summary>
    Exchange = 1 << 4,

    /// <summary>这一天被加罚延长占用。</summary>
    Penalty = 1 << 5,

    /// <summary>手动强制不值日。</summary>
    DayOff = 1 << 6,

    /// <summary>手动强制值日。</summary>
    DayOn = 1 << 7,

    /// <summary>该值日但没配值日小组（固定周几漏配）。</summary>
    NoGroupConfigured = 1 << 8,

    /// <summary>有职务没分到人。</summary>
    Unassigned = 1 << 9,

    /// <summary>个人兑换 / 增设。</summary>
    PersonExchange = 1 << 10,

    /// <summary>有人员冲突（同一人同一天被排到多个职务）。</summary>
    Conflict = 1 << 11
}

/// <summary>单元格里一个人是以什么身份出现的。</summary>
public enum DutyEntryKind
{
    /// <summary>组内正常分配。</summary>
    Member = 0,

    /// <summary>固定人员（「固」徽标）。</summary>
    Fixed = 1,

    /// <summary>个人兑换顶上来的人（「换」徽标）。</summary>
    Substitute = 2,

    /// <summary>个人增设追加的人（「加」徽标）。</summary>
    Added = 3
}

/// <summary>单元格里的一个人。名字是渲染时快照，历史记录不受后来改名影响。</summary>
public sealed class DutyEntry
{
    public Guid MemberId { get; init; }

    public string Name { get; init; } = string.Empty;

    public DutyEntryKind Kind { get; init; }
}

/// <summary>周视图里「某职务 × 某天」的格子。</summary>
public sealed class DutyPostCell
{
    public Guid PostId { get; init; }

    public string PostName { get; init; } = string.Empty;

    public IReadOnlyList<DutyEntry> Entries { get; init; } = [];
}

/// <summary>某一天的值日安排（渲染用，不落盘）。</summary>
public sealed class DutyDayPlan
{
    public DateOnly Date { get; init; }

    /// <summary>这一天是否真的值日（有小组且不是假期）。</summary>
    public bool HasDuty { get; init; }

    public Guid? GroupId { get; init; }

    public string GroupName { get; init; } = string.Empty;

    public DutyDayMarker Markers { get; init; }

    public IReadOnlyList<DutyPostCell> Cells { get; init; } = [];

    public bool HasMarker(DutyDayMarker marker) => (Markers & marker) == marker;
}

/// <summary>一段连续的小组值日（轮换模式的预览用）。</summary>
public sealed class DutyRotationSegment
{
    public Guid? GroupId { get; init; }

    public string GroupName { get; init; } = string.Empty;

    public DateOnly Start { get; init; }

    public DateOnly End { get; init; }

    public int Days { get; init; }
}

/// <summary>一段区间内的完整排班结果。</summary>
public sealed class DutyScheduleView
{
    public DateOnly RangeStart { get; init; }

    public DateOnly RangeEnd { get; init; }

    public IReadOnlyList<DutyPost> Posts { get; init; } = [];

    public IReadOnlyList<DutyDayPlan> Days { get; init; } = [];

    public DutyDayPlan? FindDay(DateOnly date) => Days.FirstOrDefault(day => day.Date == date);
}
