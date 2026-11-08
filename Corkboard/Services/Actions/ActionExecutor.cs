using Avalonia.Threading;
using Corkboard.Core.Actions;
using Corkboard.Core.Time;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Actions;

/// <summary>
///     行动的唯一执行出口：把一串 <see cref="ActionStep" /> 落到板子上，并把结果写成一条
///     <see cref="ActionRun" /> 追加到当天的 jsonl。
///     <para>
///         谁触发的不重要（调度器到点、用户点「立即执行」、老服务考试放映 / 看板模式进出），
///         全部走这里，保证「记录里有的」与「实际发生的」是同一件事。
///     </para>
///     <para>
///         板子操作在 UI 线程上做（开窗 / 关窗本来只能在 UI 线程）；文件写在操作之后做，
///         不阻塞这一拍。步骤之间没有延迟——同一条规则的步骤是「一起发生」的语义。
///     </para>
///     <para>
///         ⚠️ 同一时刻只允许一个执行流。所有调用方都在 UI 线程上调用，而动作段是同步跑完的，
///         所以真正并发只会出现在「后台文件还没写完、用户又点了立即执行」这种极短窗口里；
///         撞上时不排队、直接返回一条 <see cref="ActionRunStatus.Failed" /> 且没有步骤的 run，
///         界面据此提示「正在执行，请稍后」。
///     </para>
/// </summary>
public sealed class ActionExecutor
{
    /// <summary>失败消息截断长度：异常消息可能很长，运行记录只留可读的一行。</summary>
    private const int MaxMessageLength = 200;

    private readonly IBoardTargetRegistry _targets;
    private readonly ActionRunStore _runs;
    private readonly NtpTimeService _time;
    private readonly ILogger<ActionExecutor>? _logger;

    /// <summary>执行中标志（0 = 空闲，1 = 有执行流在动作段里）。</summary>
    private int _busy;

    /// <summary>构造执行器。<paramref name="logger" /> 可空，方便纯逻辑场景直接 new。</summary>
    public ActionExecutor(
        IBoardTargetRegistry targets,
        ActionRunStore runs,
        NtpTimeService time,
        ILogger<ActionExecutor>? logger = null)
    {
        _targets = targets;
        _runs = runs;
        _time = time;
        _logger = logger;
    }

    /// <summary>一条运行记录已经落库（列表「最近结果」、周月视图订阅它刷新）。在 UI 线程上发。</summary>
    public event EventHandler<ActionRun>? ActionRunRecorded;

    /// <summary>当前是否有执行流在动作段里。界面用它禁用「立即执行」按钮。</summary>
    public bool IsExecuting => Volatile.Read(ref _busy) != 0;

    /// <summary>
    ///     执行一条自定义行动。<paramref name="occurrenceAt" /> 是计划发生时刻（手动执行传当前时刻），
    ///     会原样记进记录的 <c>OccurrenceAt</c>，方便月视图与历史对账。
    /// </summary>
    public Task<ActionRun> ExecuteRuleAsync(ActionRule rule, DateTime occurrenceAt, ActionRunTrigger triggeredBy)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return RunOnUiThreadAsync(() =>
        {
            var draft = new ActionRun
            {
                Id = Guid.NewGuid(),
                SourceKind = ActionRunSourceKind.CustomRule,
                SourceId = rule.Id.ToString("D"),
                Title = ActionTextFormatter.Summarize(rule),
                TriggeredBy = triggeredBy,
                OccurrenceAt = occurrenceAt
            };

            return ExecuteCoreAsync(draft, rule.Steps ?? []);
        });
    }

    /// <summary>
    ///     执行一路系统日程（考试自动放映 / 看板模式自动进出）。判定与抑制仍由各自的老服务负责，
    ///     这里只执行动作 + 落记录。
    /// </summary>
    /// <param name="triggeredBy">
    ///     系统日程到点执行是 <see cref="ActionRunTrigger.Schedule" />；
    ///     窗口里点「立即执行」传 <see cref="ActionRunTrigger.Manual" />（记一条手动记录）。
    /// </param>
    public Task<ActionRun> ExecuteSystemAsync(
        ActionRunSourceKind sourceKind,
        string sourceId,
        string title,
        IReadOnlyList<ActionStep> steps,
        DateTime occurrenceAt,
        ActionRunTrigger triggeredBy = ActionRunTrigger.Schedule)
    {
        return RunOnUiThreadAsync(() =>
        {
            var draft = new ActionRun
            {
                Id = Guid.NewGuid(),
                SourceKind = sourceKind,
                SourceId = sourceId ?? string.Empty,
                Title = title ?? string.Empty,
                TriggeredBy = triggeredBy,
                OccurrenceAt = occurrenceAt
            };

            return ExecuteCoreAsync(draft, steps ?? []);
        });
    }

    /// <summary>把动作段切到 UI 线程；已经在 UI 线程时同步进入（调用方返回时动作已经做完）。</summary>
    private Task<ActionRun> RunOnUiThreadAsync(Func<Task<ActionRun>> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return action();

        return DispatchAsync(action);
    }

    /// <summary>
    ///     不在 UI 线程时：把动作数列进 UI 队列并等它执行完。
    ///     Avalonia 对 <c>Func&lt;Task&lt;T&gt;&gt;</c> 有专门的重载，直接给出解包后的结果。
    /// </summary>
    private static async Task<ActionRun> DispatchAsync(Func<Task<ActionRun>> action) =>
        await Dispatcher.UIThread.InvokeAsync(action);

    /// <summary>执行动作段并落记录。第一个 await 之前完成全部板子操作，调用方拿到 Task 时窗口已经动过。</summary>
    private async Task<ActionRun> ExecuteCoreAsync(ActionRun draft, IReadOnlyList<ActionStep> steps)
    {
        var startedAt = _time.NowLocal;

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            _logger?.LogDebug("已有执行流在跑，跳过这一次：{Title}", draft.Title);
            return draft with
            {
                StartedAt = startedAt,
                FinishedAt = startedAt,
                Status = ActionRunStatus.Failed,
                Steps = []
            };
        }

        ActionRun run;
        try
        {
            var stepRuns = new List<ActionStepRun>(steps.Count);
            foreach (var step in steps)
                stepRuns.Add(ExecuteStep(step));

            run = draft with
            {
                StartedAt = startedAt,
                FinishedAt = _time.NowLocal,
                Status = ActionRun.Aggregate(stepRuns),
                Steps = stepRuns
            };
        }
        catch (Exception exception)
        {
            // 理论上每个步骤自己已经兜住了异常；这里只防注册表 / 记录构造这类意外。
            _logger?.LogError(exception, "执行行动失败：{Title}", draft.Title);
            run = draft with
            {
                StartedAt = startedAt,
                FinishedAt = _time.NowLocal,
                Status = ActionRunStatus.Failed,
                Steps = []
            };
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }

        await PersistAsync(run).ConfigureAwait(true);
        RaiseRecorded(run);
        return run;
    }

    /// <summary>一个步骤：找目标 → 判可用 → 判已是目标状态 → 全屏让位 → 调 Show/Hide。</summary>
    private ActionStepRun ExecuteStep(ActionStep step)
    {
        if (step is null)
            return FailedStep(string.Empty, BoardOperation.Show,
                ActionResources.Get("Action_Message_TargetMissing", "目标板子不存在"));

        var target = _targets.Find(step.TargetId);
        if (target is null)
            return FailedStep(step.TargetId, step.Operation,
                ActionResources.Get("Action_Message_TargetMissing", "目标板子不存在"));

        if (!target.IsAvailable)
        {
            return new ActionStepRun
            {
                TargetId = step.TargetId,
                Operation = step.Operation,
                Status = ActionRunStatus.Skipped,
                Message = target.UnavailableReason
                          ?? ActionResources.Get("Action_Message_Unavailable", "目标当前不可用")
            };
        }

        var wantVisible = step.Operation == BoardOperation.Show;

        // 已经是要的状态：记成功，但不再调一次 Show/Hide——那会把「收纳展开」「最小化还原」
        // 这类副作用重复触发一遍。
        if (target.IsVisible == wantVisible)
        {
            return new ActionStepRun
            {
                TargetId = step.TargetId,
                Operation = step.Operation,
                Status = ActionRunStatus.Success,
                Message = ActionResources.Get("Action_Message_AlreadyInState", "已经是这个状态")
            };
        }

        string? yieldMessage = null;
        if (wantVisible && target.IsFullScreen)
            yieldMessage = HideOtherFullScreenTargets(target);

        try
        {
            if (wantVisible)
                target.Show();
            else
                target.Hide();

            return new ActionStepRun
            {
                TargetId = step.TargetId,
                Operation = step.Operation,
                Status = ActionRunStatus.Success,
                Message = yieldMessage
            };
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "行动步骤失败：{Operation} {Target}", step.Operation, step.TargetId);
            return FailedStep(step.TargetId, step.Operation, BuildExceptionMessage(exception));
        }
    }

    /// <summary>全屏互斥：显示一块全屏目标前，把同组其它可见的全屏目标先藏起来。</summary>
    private string? HideOtherFullScreenTargets(IBoardTarget showing)
    {
        List<string>? hidden = null;

        foreach (var other in _targets.Targets)
        {
            if (ReferenceEquals(other, showing) || !other.IsFullScreen || !other.IsVisible)
                continue;

            try
            {
                other.Hide();
                (hidden ??= []).Add(other.DisplayName);
            }
            catch (Exception exception)
            {
                // 让位失败不拦住本次显示：最坏是两块全屏叠在一起，用户还能手动关。
                _logger?.LogError(exception, "全屏让位时关闭看板失败：{Target}", other.Id);
            }
        }

        if (hidden is not { Count: > 0 })
            return null;

        var label = ActionResources.Get("Action_Message_FullScreenYield", "全屏看板正在使用，已让位");
        return $"{label}{(ActionResources.IsChinese ? "：" : ": ")}{string.Join(ActionResources.IsChinese ? "、" : ", ", hidden)}";
    }

    private ActionStepRun FailedStep(string targetId, BoardOperation operation, string message) => new()
    {
        TargetId = targetId,
        Operation = operation,
        Status = ActionRunStatus.Failed,
        Message = message
    };

    private static string BuildExceptionMessage(Exception exception)
    {
        var text = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;

        var prefix = ActionResources.Get("Action_Message_Exception", "执行出错");
        var message = $"{prefix}{(ActionResources.IsChinese ? "：" : ": ")}{text}";
        return message.Length <= MaxMessageLength ? message : message[..MaxMessageLength];
    }

    /// <summary>追加当天 jsonl。写在动作之后、线程池上做；失败只记日志，不影响这次执行的结果。</summary>
    private async Task PersistAsync(ActionRun run)
    {
        try
        {
            await Task.Run(() => _runs.Append(run)).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "写入行动运行记录失败：{Title}", run.Title);
        }
    }

    private void RaiseRecorded(ActionRun run)
    {
        try
        {
            ActionRunRecorded?.Invoke(this, run);
        }
        catch (Exception exception)
        {
            // 订阅方（界面）的异常不能反过来影响执行结果。
            _logger?.LogError(exception, "通知行动运行记录失败：{Title}", run.Title);
        }
    }
}
