namespace Corkboard.Core.Actions.Schedule;

/// <summary>日程条目的种类。数值冻结，只能末尾追加。</summary>
public enum ScheduleItemKind
{
    /// <summary>用户自定义的行动规则。</summary>
    CustomAction = 0,

    /// <summary>考试自动放映（系统日程）。</summary>
    ExamAutoShow = 1,

    /// <summary>看板模式自动放映（系统日程）。</summary>
    DisplayAutoShow = 2
}

/// <summary>
///     日程中心 / 行动窗口里的一条日程。
///     <para>
///         自定义行动与系统日程（考试自动放映、看板模式时段）用同一个形状，界面才能合并成一条时间线；
///         <see cref="Kind" /> 决定能不能编辑、能不能暂停、去哪个设置页。
///     </para>
///     <para>
///         <see cref="End" /> 为空表示点事件（到点触发一次，没有持续区间）；有值的是区间，口径<b>左闭右开</b>
///         <c>[Start, End)</c>。所有时刻都是本地时间。
///     </para>
/// </summary>
public sealed record ScheduleItem
{
    /// <summary>条目标识：自定义行动是规则 Guid 字符串，系统日程是 <c>exam-auto-show</c> / <c>display-&lt;时段&gt;-&lt;日期&gt;</c>。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>种类。</summary>
    public ScheduleItemKind Kind { get; init; }

    /// <summary>显示名。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>开始时刻（本地时间）。</summary>
    public DateTime Start { get; init; }

    /// <summary>结束时刻；为空表示点事件。</summary>
    public DateTime? End { get; init; }

    /// <summary>这条日程此刻会不会真的执行（系统日程在设置里关掉时为假，界面显示「未启用」）。</summary>
    public bool IsEnabled { get; init; }

    /// <summary>能不能在窗口里直接编辑（自定义行动为真，系统日程为假 —— 只能跳设置页）。</summary>
    public bool IsEditable { get; init; }

    /// <summary>能不能「今天不执行」。</summary>
    public bool CanPause { get; init; }

    /// <summary>今天是不是已经被「今天不执行」暂停（由状态存储叠加到投影结果上）。</summary>
    public bool IsSuppressed { get; init; }

    /// <summary>系统日程对应的设置页 Id（自定义行动为空）。</summary>
    public string? SettingsPageId { get; init; }

    /// <summary>这条日程会操作哪些目标看板（冻结的 <see cref="BoardTargetIds" />）。</summary>
    public IReadOnlyList<string> TargetIds { get; init; } = [];

    /// <summary>自定义行动对应的规则 Id（系统日程为空）。</summary>
    public Guid? RuleId { get; init; }

    /// <summary>是不是点事件（没有结束时刻）。</summary>
    public bool IsPointEvent => End is null;
}

/// <summary>
///     日程数据源：自定义行动、考试自动放映、看板模式时段各实现一个，
///     <see cref="IScheduleSourceRegistry" /> 负责把它们合并成一条时间线。
///     <para>
///         实现方（App 层）在查询区间上做投影；<see cref="CanPause" /> 表示这类条目能否「今天不执行」，
///         自定义行动与两个系统源都支持，以后接入的只读源（例如值日表）可以返回假。
///     </para>
/// </summary>
public interface IScheduleSource
{
    /// <summary>源标识（也是系统日程条目 Id 的前缀，如 <c>exam-auto-show</c>）。</summary>
    string Id { get; }

    /// <summary>显示名（窗口里分组标题用）。</summary>
    string DisplayName { get; }

    /// <summary>这个源的条目能不能「今天不执行」。</summary>
    bool CanPause { get; }

    /// <summary>取 <c>[fromInclusive, toExclusive)</c> 里与这个源相关的日程条目。</summary>
    IReadOnlyList<ScheduleItem> GetItems(DateTime fromInclusive, DateTime toExclusive);
}
