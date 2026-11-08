using Avalonia.Threading;
using Corkboard.Core.Actions;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Actions;

/// <summary>
///     自定义行动的调度器：每 15 秒看一眼「上一拍到这一拍之间有哪些规则该跑」，跑完把水位线前移。
///     <para>
///         它就是 <see cref="ActionSchedule.OccurrencesBetween" /> 的唯一消费者，所以「界面上的下一次」
///         与「实际到点执行」是同一份判定。系统日程（考试放映 / 看板模式）不在这里——它们的老服务
///         自己判定、只在最终动作上走 <see cref="ActionExecutor.ExecuteSystemAsync" />。
///     </para>
///     <para>
///         三条防补发规矩（见设计文档 §5.2）：
///         <list type="bullet">
///             <item>第一次拍读 <c>state.json</c> 的水位线；它太旧（&gt; 2 分钟）就只记当前时刻，不把停机期间的发生点补跑一遍。</item>
///             <item>水位线比当前时间还晚 1 分钟以上（用户改过系统时钟）→ 记日志、水位线收到当前时刻、这一拍不执行。</item>
///             <item>前后两拍间隔超过 2 分钟（休眠 / 卡顿）→ 同样记日志、只前移水位线，不补发。</item>
///         </list>
///     </para>
///     <para>
///         总开关（<see cref="ActionSettingsConfig.Enabled" />）关掉时只更新水位线：重新打开时不该把关着的那段一股脑补执行。
///     </para>
/// </summary>
public sealed class ActionSchedulerHostedService : BackgroundService
{
    /// <summary>时刻是分钟级的，十几秒看一次足够，也不会占着 UI 线程。</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    /// <summary>两拍之间允许的最大间隔；超过说明中间漏了拍（休眠 / 卡死），不补发。</summary>
    private static readonly TimeSpan TickTolerance = TimeSpan.FromMinutes(2);

    /// <summary>水位线允许的最大「超前」；超过就是系统时钟被往回调了。</summary>
    private static readonly TimeSpan ClockBackTolerance = TimeSpan.FromMinutes(1);

    /// <summary>水位线最多每分钟落一次盘；每次真的执行了再额外落一次。</summary>
    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(1);

    private readonly MainConfigHandler _configHandler;
    private readonly ActionRuleStore _rules;
    private readonly ActionStateStore _state;
    private readonly ActionExecutor _executor;
    private readonly NtpTimeService _time;
    private readonly ILogger<ActionSchedulerHostedService> _logger;

    /// <summary>首拍是否已经处理过（水位线初始化）。</summary>
    private bool _initialized;

    /// <summary>上一拍落盘时刻（水位线的节流）。</summary>
    private DateTime _lastSavedAt;

    /// <summary>本拍是否正在跑；拍内 await 时不让下一拍重入。</summary>
    private int _ticking;

    /// <summary>构造调度器。</summary>
    public ActionSchedulerHostedService(
        MainConfigHandler configHandler,
        ActionRuleStore rules,
        ActionStateStore state,
        ActionExecutor executor,
        NtpTimeService time,
        ILogger<ActionSchedulerHostedService> logger)
    {
        _configHandler = configHandler;
        _rules = rules;
        _state = state;
        _executor = executor;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    ///     挪到线程池上跑。<c>Host.StartAsync</c> 会同步执行到第一个未完成的 await，
    ///     而这里第一件事是往 UI 线程排队——不能把启动线程卡在那儿等界面转起来。
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunLoopAsync(stoppingToken), CancellationToken.None);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("行动调度器已启动");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await CheckOnceAsync(stoppingToken).ConfigureAwait(false);

                try
                {
                    await Task.Delay(CheckInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _logger.LogDebug("行动调度器已停止");
        }
    }

    private async Task CheckOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 带上取消令牌：退出时排队中的这一笔不再等界面，直接放掉。
            await Dispatcher.UIThread.InvokeAsync(Tick, DispatcherPriority.Background, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // 后台自动化失败只记日志，绝不弹窗打断用户。
            _logger.LogError(exception, "行动调度检查失败");
        }
    }

    /// <summary>一拍：读水位线 → 防补发判定 → 取发生点 → 每条规则只跑最后一次 → 前移水位线。</summary>
    private void Tick()
    {
        if (Interlocked.CompareExchange(ref _ticking, 1, 0) != 0)
            return;

        try
        {
            var now = _time.NowLocal;
            var last = _state.LastChecked;

            if (!_initialized)
            {
                _initialized = true;

                if (last is null || now - last.Value > TickTolerance)
                {
                    _state.LastChecked = now;
                    SaveState(now, force: true);
                    return;
                }
            }

            if (last is null)
            {
                _state.LastChecked = now;
                SaveState(now, force: true);
                return;
            }

            if (now < last.Value - ClockBackTolerance)
            {
                _logger.LogWarning("系统时间比上次扫描时还早（{Last:O} → {Now:O}），跳过这一拍并前移水位线", last.Value,
                    now);
                _state.LastChecked = now;
                SaveState(now, force: true);
                return;
            }

            if (now - last.Value > TickTolerance)
            {
                _logger.LogWarning("距上次扫描已 {Gap}，超过 {Tolerance}，跳过这一拍不补发", now - last.Value,
                    TickTolerance);
                _state.LastChecked = now;
                SaveState(now, force: true);
                return;
            }

            if (!_configHandler.Data.ActionSettings.Enabled)
            {
                // 总开关关着：只把水位线推到当前时刻，重新打开时不补跑关着的那段。
                _state.LastChecked = now;
                SaveState(now, force: false);
                return;
            }

            var occurrences = ActionSchedule.OccurrencesBetween(_rules.Rules, last.Value, now);
            var executed = occurrences.Count > 0;

            // 同一条规则在一个区间里可能命中多次（例如间隔两分钟以上的连拍），只跑最后那一个发生点：
            // 用户要的是「现在该是什么状态」，不是把过去几分钟的中间态逐条重放。
            var latestPerRule = occurrences
                .GroupBy(occurrence => occurrence.Rule.Id)
                .Select(group => group.MaxBy(occurrence => occurrence.At))
                .OrderBy(occurrence => occurrence.At)
                .ToList();

            foreach (var occurrence in latestPerRule)
            {
                // 执行器在 UI 线程上同步跑完动作段，返回的 Task 只等文件落盘；
                // 这里不等它，免得一次慢磁盘把这一拍的水位线拖后。
                _ = _executor.ExecuteRuleAsync(occurrence.Rule, occurrence.At, ActionRunTrigger.Schedule)
                    .ContinueWith(
                        task => _logger.LogError(task.Exception, "执行行动规则失败：{Rule}", occurrence.Rule.Id),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
            }

            _state.LastChecked = now;
            SaveState(now, force: executed);
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    /// <summary>节流落盘：最多每分钟一次；刚执行完（<paramref name="force" />）立刻落一次。</summary>
    private void SaveState(DateTime now, bool force)
    {
        if (!force && _lastSavedAt != default && now - _lastSavedAt < SaveInterval)
            return;

        _state.Save();
        _lastSavedAt = now;
    }
}
