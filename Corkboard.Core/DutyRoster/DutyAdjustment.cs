namespace Corkboard.Core.DutyRoster;

/// <summary>
///     一条手动调整。所有兑换、加罚、指定都落在这里，排班时叠加到基线之上；
///     停用（<see cref="IsEnabled" /> = false）而不是删除，方便撤销与追溯。
/// </summary>
public sealed class DutyAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DutyAdjustmentKind Kind { get; set; }

    public DutyLifetime Lifetime { get; set; } = DutyLifetime.Temporary;

    /// <summary>生效起日（含）。</summary>
    public DateOnly Start { get; set; }

    /// <summary>生效止日（含；永久为 null）。</summary>
    public DateOnly? End { get; set; }

    /// <summary>被代班 / 被罚 / 被换人的小组。</summary>
    public Guid? GroupId { get; set; }

    /// <summary>代班组 / 对调组 / 加罚时不受罚的对照（按种类解释）。</summary>
    public Guid? TargetGroupId { get; set; }

    /// <summary>加罚锚点：这一组从哪个值日段开始延长（空 = 该组下一个值日段）。</summary>
    public DateOnly? AnchorDate { get; set; }

    /// <summary>加罚天数（1–14）。</summary>
    public int Days { get; set; }

    /// <summary>加罚处理方式（空 = 跟随 <see cref="DutyScheduleConfig.PenaltyMode" />）。</summary>
    public DutyPenaltyMode? PenaltyMode { get; set; }

    /// <summary>个人兑换/增设针对的职务。</summary>
    public Guid? PostId { get; set; }

    /// <summary>个人兑换：被换下的人。</summary>
    public Guid? FromMemberId { get; set; }

    /// <summary>个人兑换/增设：换上去 / 追加的人。</summary>
    public Guid? ToMemberId { get; set; }

    public string Note { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public bool IsEnabled { get; set; } = true;

    /// <summary>某一天这条调整是否生效（只看启停与起止日期）。</summary>
    public bool IsEffectiveOn(DateOnly date) =>
        IsEnabled && date >= Start && (End is not { } end || date <= end);
}

/// <summary>某一天的值日/不值日强制覆盖，优先级最高。</summary>
public sealed class DutyDayOverride
{
    public DateOnly Date { get; set; }

    public DutyDayOverrideState State { get; set; } = DutyDayOverrideState.Auto;

    public string Note { get; set; } = string.Empty;
}

/// <summary>
///     名单来源信息：界面上要明确写「来自 SecRandom、同组织内使用」，
///     并带上导入方式与时间，便于追溯。
/// </summary>
public sealed class DutyRosterSource
{
    public string Provider { get; set; } = "SecRandom";

    /// <summary>原名单位置（SR 的名单名 / 导入文件名）。</summary>
    public string RosterName { get; set; } = string.Empty;

    /// <summary>导入方式：File / QuickQr / OfflineQr / SessionCode / Manual。</summary>
    public string ImportMethod { get; set; } = string.Empty;

    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>导入时的值日表数据版本。</summary>
    public int DocumentVersion { get; set; }

    /// <summary>离线二维码的会话 ID（有就记，便于核对）。</summary>
    public string? SessionId { get; set; }

    /// <summary>名单内容 SHA-256（十六进制，可空）。</summary>
    public string? Checksum { get; set; }

    public string Note { get; set; } = string.Empty;
}
