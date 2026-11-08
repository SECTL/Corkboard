using System.Text.Json.Serialization;

namespace Corkboard.Core.Actions;

/// <summary>
///     一条行动什么时候触发。
///     <para>
///         枚举<b>按数字</b>落盘以外的地方都走字符串（见 <see cref="ActionJson" />），
///         但枚举成员的数字同样冻结：只能往末尾追加。
///     </para>
/// </summary>
public enum ActionTriggerKind
{
    /// <summary>只触发一次（<see cref="ActionTrigger.Date" /> 必填）。</summary>
    Once = 0,

    /// <summary>每天在 <see cref="ActionTrigger.Time" /> 触发。</summary>
    Daily = 1,

    /// <summary>每周在 <see cref="ActionTrigger.Days" /> 选中的那几天、<see cref="ActionTrigger.Time" /> 触发。</summary>
    Weekly = 2
}

/// <summary>
///     「星期几」位掩码（与看板模式时段的 <c>DisplayDayFlags</c> 一一对应，但那是配置类、这是行动纯模型，
///     两者刻意分开以免行动域被 UI 配置模型的演进拖着走）。
///     <para>
///         落盘是字符串（<c>"Monday, Tuesday"</c>），成员数值仍然冻结，只能往末尾追加。
///     </para>
/// </summary>
[Flags]
public enum ActionDayFlags
{
    /// <summary>一天都不选（每周规则必须至少选一天，否则校验器报错）。</summary>
    None = 0,

    /// <summary>周一。</summary>
    Monday = 1 << 0,

    /// <summary>周二。</summary>
    Tuesday = 1 << 1,

    /// <summary>周三。</summary>
    Wednesday = 1 << 2,

    /// <summary>周四。</summary>
    Thursday = 1 << 3,

    /// <summary>周五。</summary>
    Friday = 1 << 4,

    /// <summary>周六。</summary>
    Saturday = 1 << 5,

    /// <summary>周日。</summary>
    Sunday = 1 << 6,

    /// <summary>周一到周五。</summary>
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,

    /// <summary>周六、周日。</summary>
    Weekend = Saturday | Sunday,

    /// <summary>每一天。</summary>
    EveryDay = Weekdays | Weekend
}

/// <summary>
///     一条规则的触发描述：单次（日期 + 时刻）、每天（时刻）、每周（星期 + 时刻）。
///     <para>
///         三者共用同一个对象，<see cref="Kind" /> 决定哪些字段有意义：
///         单次看 <see cref="Date" />，每周看 <see cref="Days" />，每天两个都不用。
///         落盘时 <see cref="Date" /> 缺省省略、<see cref="Days" /> 为 <see cref="ActionDayFlags.None" /> 时省略。
///     </para>
/// </summary>
public sealed record ActionTrigger
{
    /// <summary>触发类型。</summary>
    public ActionTriggerKind Kind { get; init; }

    /// <summary>单次触发的日期；其它类型不用。</summary>
    public DateOnly? Date { get; init; }

    /// <summary>触发时刻（三种类型都必填）。</summary>
    public TimeOnly Time { get; init; }

    /// <summary>每周触发的星期；其它类型不用。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ActionDayFlags Days { get; init; }
}

/// <summary>条件类型。v1 只有「日期范围」，枚举数值冻结、只能末尾追加。</summary>
public enum ActionConditionKind
{
    /// <summary>只在某段日期里生效（两端都含当日）。</summary>
    DateRange = 0
}

/// <summary>
///     附加条件：目前只有日期范围，<see cref="Start" /> / <see cref="End" /> 都含当日，
///     两端都可以留空表示「不限」。
/// </summary>
public sealed record ActionCondition
{
    /// <summary>条件类型。</summary>
    public ActionConditionKind Kind { get; init; }

    /// <summary>生效起始日期（含当日）；为空表示不限。</summary>
    public DateOnly? Start { get; init; }

    /// <summary>生效结束日期（含当日）；为空表示不限。</summary>
    public DateOnly? End { get; init; }
}

/// <summary>对看板执行的动作。数值冻结、只能末尾追加。</summary>
public enum BoardOperation
{
    /// <summary>显示。</summary>
    Show = 0,

    /// <summary>隐藏。</summary>
    Hide = 1
}

/// <summary>
///     步骤：对哪块看板做什么。
///     <para>
///         <see cref="TargetId" /> 用冻结的看板标识（<c>home-work</c> / <c>homework-board</c> /
///         <c>exam-board</c> / <c>display-board</c> / <c>duty-board</c>），
///         见 <see cref="IBoardTarget" />；同一个目标在一条规则里不能既 Show 又 Hide（校验器报警告）。
///     </para>
/// </summary>
public sealed record ActionStep
{
    /// <summary>目标看板标识。</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>对这块看板做什么。</summary>
    public BoardOperation Operation { get; init; }
}

/// <summary>
///     一条行动规则：什么时候（<see cref="Trigger" />）、在什么条件下（<see cref="Condition" />）、
///     对哪些看板做什么（<see cref="Steps" />）。
///     <para>
///         它是纯数据记录（<c>record</c> + <c>init</c>），没有 UI 依赖；时间戳由调用方维护
///         （编辑器新建 / 保存时填 <see cref="CreatedAt" />、<see cref="UpdatedAt" />），
///         <see cref="ActionRuleStore" /> 只负责原样落盘。
///     </para>
/// </summary>
public sealed record ActionRule
{
    /// <summary>规则标识（自定义行动的日程 Id 就是它）。</summary>
    public Guid Id { get; init; }

    /// <summary>规则名；为空时界面按目标推导一个显示名。</summary>
    public string? Name { get; init; }

    /// <summary>是否启用。⚠️ 默认 <see langword="false" />，编辑器新建规则时要显式置 <see langword="true" />。</summary>
    public bool Enabled { get; init; }

    /// <summary>触发方式；不允许为空（默认「单次」）。</summary>
    public ActionTrigger Trigger { get; init; } = new();

    /// <summary>附加条件；为空表示不限制。</summary>
    public ActionCondition? Condition { get; init; }

    /// <summary>至少一条步骤（校验器负责报「没有步骤」）。</summary>
    public IReadOnlyList<ActionStep> Steps { get; init; } = [];

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>最后修改时间。</summary>
    public DateTime UpdatedAt { get; init; }
}
