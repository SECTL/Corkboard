using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Time;

namespace Corkboard.Services.Time;

/// <summary>
///     后台校时：启动时同步一次，之后按「设置 → 考试看板 → 时间同步」里的间隔再来一次。
///     <para>
///         不睡到下一个整点、而是每 30 秒醒一次看一眼「到点没有 / 开关还是不是开着」——
///         用户随时会改间隔、改源、关开关，睡死一条 <c>Task.Delay(间隔)</c> 会让改动迟迟不生效。
///         真正的 NTP 请求频率仍由配置决定，醒着不花什么代价。
///     </para>
///     <para>
///         校时失败只写日志：网络不好时看板照样能用（退回本机时间），不能因为一次同步失败就报错。
///     </para>
/// </summary>
public sealed class NtpSyncHostedService : BackgroundService
{
    /// <summary>轮询粒度：既让「改完设置马上生效」不至于等太久，又不至于频繁打扰。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly NtpTimeService _time;
    private readonly ILogger<NtpSyncHostedService> _logger;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public NtpSyncHostedService(NtpTimeService time, ILogger<NtpSyncHostedService> logger)
    {
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "NTP 校时循环出错，稍后重试。");
            }
        }
    }

    /// <summary>一轮判定：关着就只把偏差归零，开着且到点就同步一次。</summary>
    private async Task TickAsync(CancellationToken cancellationToken)
    {
        _time.RefreshEnabledState();

        if (!_time.IsEnabled)
            return;

        var now = DateTimeOffset.UtcNow;
        if (_lastAttempt != DateTimeOffset.MinValue && now - _lastAttempt < _time.Settings.ResolveInterval())
            return;

        _lastAttempt = now;
        await _time.SyncNowAsync(cancellationToken).ConfigureAwait(false);
    }
}
