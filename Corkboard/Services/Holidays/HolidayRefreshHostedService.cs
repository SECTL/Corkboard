using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Holidays;

/// <summary>
///     节假日后台刷新：启动后延迟一次，之后每半小时看一次「该不该刷新」。
///     <para>
///         具体策略在 <see cref="HolidayService.RefreshIfDueAsync" /> 里（今年 7 天、次年 10 月起每周、
///         去年只补一次、失败退避），这里只负责按时戳它一下，**不要在托管服务里另写一套判断**。
///     </para>
///     <para>
///         离线、限流、上游抽风都只写 Warning 日志：节假日数据坏了不该打扰用户，
///         上一份缓存照旧可用（见 <c>.tmp/duty-roster-plan.md</c> §7）。
///     </para>
/// </summary>
public sealed class HolidayRefreshHostedService(
    HolidayService holidays,
    ILogger<HolidayRefreshHostedService> logger) : BackgroundService
{
    /// <summary>启动后先等一会儿：别和主窗口、云同步、更新检查抢网络。</summary>
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(20);

    /// <summary>每半小时看一眼即可；真正的「该不该拉」由服务里的策略决定。</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await holidays.RefreshIfDueAsync(stoppingToken).ConfigureAwait(false);
                if (result.UpdatedYears > 0)
                    logger.LogInformation("节假日数据已更新 {Count} 个年份。", result.UpdatedYears);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "节假日后台刷新失败，继续使用缓存。");
            }

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
}
