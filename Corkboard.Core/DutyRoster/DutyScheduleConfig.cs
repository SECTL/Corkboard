namespace Corkboard.Core.DutyRoster;

/// <summary>排班规则（存在 <c>data/duty/duty.json</c>，不是 settings.json）。</summary>
public sealed class DutyScheduleConfig
{
    public DutyScheduleMode Mode { get; set; } = DutyScheduleMode.FixedWeekday;

    /// <summary>哪些星期值日（位掩码，默认周一至周五）。</summary>
    public int Weekdays { get; set; } = DutyWeekdays.Workdays;

    /// <summary>固定周几模式：<c>DayOfWeek</c> 的编号（周日=0 … 周六=6）→ 小组 ID。</summary>
    public Dictionary<int, Guid> FixedWeekdayMap { get; set; } = [];

    public DutyRotationConfig Rotation { get; set; } = new();

    /// <summary>跳过法定节假日（数据来自节假日模块）。</summary>
    public bool SkipLegalHolidays { get; set; } = true;

    /// <summary>调休补班日照常值日（即使落在周末）。</summary>
    public bool WorkOnMakeupDays { get; set; } = true;

    /// <summary>加罚的处理方式；固定周几模式只使用 <see cref="DutyPenaltyMode.Swallow" />。</summary>
    public DutyPenaltyMode PenaltyMode { get; set; } = DutyPenaltyMode.Swallow;

    /// <summary>周视图里给固定人员画「固」徽标。</summary>
    public bool ShowFixedBadge { get; set; } = true;
}

/// <summary>小组轮流模式的参数。</summary>
public sealed class DutyRotationConfig
{
    /// <summary>+1 = 按 <see cref="DutyGroup.Order" /> 正序，-1 = 倒序。</summary>
    public int Direction { get; set; } = 1;

    /// <summary>从哪个小组开始（空 = 顺序第一组）。</summary>
    public Guid? StartGroupId { get; set; }

    /// <summary>从哪天开始算（空 = 第一个值日日）。</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>一个小组连续打扫几个值日日（不是自然日，跳过节假日后的值日日）。</summary>
    public int DaysPerGroup { get; set; } = 5;
}
