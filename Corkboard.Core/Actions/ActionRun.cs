namespace Corkboard.Core.Actions;

/// <summary>一次执行的总体状态。数值冻结，只能末尾追加。</summary>
public enum ActionRunStatus
{
    /// <summary>全部成功。</summary>
    Success = 0,

    /// <summary>部分成功（有步骤被跳过、其余成功）。</summary>
    Partial = 1,

    /// <summary>失败（任一步失败即整体失败）。</summary>
    Failed = 2,

    /// <summary>整条规则被跳过（目标都不可用 / 今天已抑制等）。</summary>
    Skipped = 3
}

/// <summary>这次执行是谁发起的。数值冻结，只能末尾追加。</summary>
public enum ActionRunTrigger
{
    /// <summary>到点自动执行。</summary>
    Schedule = 0,

    /// <summary>用户在窗口里手动「立即执行」。</summary>
    Manual = 1
}

/// <summary>执行来源的种类，决定 <see cref="ActionRun.SourceId" /> 怎么解读。数值冻结，只能末尾追加。</summary>
public enum ActionRunSourceKind
{
    /// <summary>自定义行动规则，<see cref="ActionRun.SourceId" /> 是规则 Guid 字符串。</summary>
    CustomRule = 0,

    /// <summary>考试自动放映（系统日程），<see cref="ActionRun.SourceId" /> 是 <c>exam-auto-show</c>。</summary>
    ExamAutoShow = 1,

    /// <summary>看板模式自动放映（系统日程），<see cref="ActionRun.SourceId" /> 是 <c>display-auto-show</c>。</summary>
    DisplayAutoShow = 2
}

/// <summary>一条步骤的执行结果。</summary>
public sealed record ActionStepRun
{
    /// <summary>目标看板标识。</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>对这块看板做了什么。</summary>
    public BoardOperation Operation { get; init; }

    /// <summary>这一步的结果。</summary>
    public ActionRunStatus Status { get; init; }

    /// <summary>失败 / 跳过原因（成功时为空）。</summary>
    public string? Message { get; init; }
}

/// <summary>
///     一次执行记录，落盘到 <c>action/runs/yyyy-MM-dd.jsonl</c>（一行一条，按 <see cref="StartedAt" /> 归日）。
///     <para>
///         历史只追加不改写；保留 90 天由 <see cref="ActionRunStore.Prune" /> 清理。
///     </para>
/// </summary>
public sealed record ActionRun
{
    /// <summary>执行记录标识。</summary>
    public Guid Id { get; init; }

    /// <summary>来源种类。</summary>
    public ActionRunSourceKind SourceKind { get; init; }

    /// <summary>来源标识：自定义规则是规则 Guid，系统日程是固定的源 Id。</summary>
    public string SourceId { get; init; } = string.Empty;

    /// <summary>界面上显示的名字（规则名 / 考试看板 / 看板模式）。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>自动还是手动。</summary>
    public ActionRunTrigger TriggeredBy { get; init; }

    /// <summary>被触发的是哪一个发生点。</summary>
    public DateTime OccurrenceAt { get; init; }

    /// <summary>开始执行时刻。</summary>
    public DateTime StartedAt { get; init; }

    /// <summary>执行结束时刻。</summary>
    public DateTime FinishedAt { get; init; }

    /// <summary>总体状态（<see cref="Aggregate" /> 的结果）。</summary>
    public ActionRunStatus Status { get; init; }

    /// <summary>每一步的结果（按规则里的步骤顺序）。</summary>
    public IReadOnlyList<ActionStepRun> Steps { get; init; } = [];

    /// <summary>
    ///     由每步结果归并出总体状态：任一步失败 → <see cref="ActionRunStatus.Failed" />；
    ///     全跳过 → <see cref="ActionRunStatus.Skipped" />；有跳过也有成功 → <see cref="ActionRunStatus.Partial" />；
    ///     其余 → <see cref="ActionRunStatus.Success" />。执行器与历史页共用这一份口径。
    /// </summary>
    public static ActionRunStatus Aggregate(IReadOnlyList<ActionStepRun>? steps)
    {
        if (steps is null || steps.Count == 0)
            return ActionRunStatus.Success;

        if (steps.Any(step => step.Status == ActionRunStatus.Failed))
            return ActionRunStatus.Failed;

        var skipped = steps.Count(step => step.Status == ActionRunStatus.Skipped);
        if (skipped == steps.Count)
            return ActionRunStatus.Skipped;

        if (skipped > 0 && steps.Any(step => step.Status == ActionRunStatus.Success))
            return ActionRunStatus.Partial;

        return ActionRunStatus.Success;
    }
}
