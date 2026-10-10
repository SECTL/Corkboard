using Avalonia.Threading;
using Corkboard.Core.Services.Board;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services;

/// <summary>
///     到点自动清理过期作业：每分钟看一眼设置里的清理时刻到没到，到了就把过了截止日期的作业
///     从板子上清掉（只打「已清理」标记，数据原地不动）。
/// </summary>
/// <remarks>
///     <para>
///         为什么是「每分钟看一次」而不是「睡到下一个时刻」：用户随时可能把清理时刻从 4 点改到 5 点、
///         或者干脆关掉开关，定时器一旦睡死就得等它醒。每分钟一次的比较是纯内存判断，代价可以忽略。
///     </para>
///     <para>
///         第一眼在启动后立刻看，不到一分钟就检查一次：应用没开着的时候错过的那个时刻，
///         会在下次启动时补清（见 <see cref="BoardCleanupService.IsDue" />）。
///     </para>
///     <para>
///         <b>清理必须回到 UI 线程上跑</b>：清完会落盘并通知作业集合变更，主页面随即重建绑定到界面上
///         的那一份列表；在线程池线程上走这一趟会直接跟界面的渲染打架。
///         判断「到没到点」也一起放在 UI 线程，省得和设置页写配置互相插队。
///     </para>
///     <para>
///         失败只记日志：清理是后台的顺手活，绝不能把应用拖垮，也不该弹窗打断用户。
///     </para>
/// </remarks>
public sealed class BoardCleanupHostedService(
    BoardCleanupService cleanupService,
    ILogger<BoardCleanupHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     挪到线程池上跑。<c>Host.StartAsync</c> 会同步执行 <c>ExecuteAsync</c> 到第一个未完成的 await，
    ///     而这里第一件事就是往 UI 线程排队——不能把启动线程卡在那儿等界面转起来。
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunLoopAsync(stoppingToken), CancellationToken.None);

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("作业自动清理服务已启动");

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
            logger.LogDebug("作业自动清理服务已停止");
        }
    }

    private async Task CheckOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 带上取消令牌：退出时排队中的这一笔不再等界面，直接放掉。
            await Dispatcher.UIThread.InvokeAsync(
                () => RunIfDue(DateTimeOffset.Now), DispatcherPriority.Background, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "作业自动清理检查失败");
        }
    }

    private void RunIfDue(DateTimeOffset now)
    {
        // 清理时刻到了就动手。判定只看「最近一个该动手的时刻过去了没有」，
        // 所以应用当时没开着也能在下次运行时补上。
        if (!cleanupService.IsDue(now))
            return;

        var result = cleanupService.Run(now);

        if (result.Cleaned > 0)
            logger.LogInformation("自动清理清掉 {Count} 条过期作业", result.Cleaned);
    }
}
