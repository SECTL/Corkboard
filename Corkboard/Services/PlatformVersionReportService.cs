using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Corkboard.Core;
using Corkboard.Core.Services.Stats;
using Corkboard.Services.Auth;
using Corkboard.Services.Stats;

namespace Corkboard.Services;

/// <summary>
///     把本机的版本号上报给 SECTL 版本统计（走聚合入口 <c>POST /api/stats/report</c>，
///     一条带 <c>version</c> 的事件就是版本上报）。
/// </summary>
/// <remarks>
///     <para>
///         接口按软件安装去重统计「每个版本还有多少人」，因此载荷里只出现平台标识、版本号和伪匿名设备标识，
///         **从不**上报 SECTL 账号 ID —— 版本分布不能与账号关联。
///     </para>
///     <para>
///         API 要求每次启动上报一次，所以这个服务每个进程只跑一次：不轮询、不重试轰炸、不保留状态。
///         上报永远是尽力而为且跑在线程池上，统计失败既不阻塞启动，也不影响账号流程。
///     </para>
/// </remarks>
public sealed class PlatformVersionReportService : BackgroundService
{
    private readonly IDeviceIdProvider _deviceIdProvider;
    private readonly ILogger<PlatformVersionReportService> _logger;
    private readonly ISectlStatsReporter _reporter;

    public PlatformVersionReportService(
        IDeviceIdProvider deviceIdProvider,
        ISectlStatsReporter reporter,
        ILogger<PlatformVersionReportService> logger)
    {
        _deviceIdProvider = deviceIdProvider;
        _reporter = reporter;
        _logger = logger;
    }

    /// <summary>
    ///     把上报交给线程池。<c>Host.StartAsync</c> 会同步执行托管服务 <c>ExecuteAsync</c> 到第一个未完成的 await，
    ///     而这段前置代码要读（首次还写）设备标识文件、构造 HTTP 请求——这些都不该跑在启动线程上，
    ///     请求本身也不能拖慢启动。
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ReportOnceAsync(stoppingToken), CancellationToken.None);

    private async Task ReportOnceAsync(CancellationToken cancellationToken)
    {
        StatsReportEvent report;
        try
        {
            report = StatsReportEvent.CreateVersion(
                GlobalConstants.PlatformId,
                // 版本号保留 v 前缀：服务端按这个字符串归并人数，不要换成 Tag 或去掉前缀。
                GlobalConstants.Version,
                _deviceIdProvider.GetOrCreate().ToString("D"));
        }
        catch (ArgumentException exception)
        {
            // 版本号格式或设备标识不合规只影响统计，绝不能影响启动。
            _logger.LogDebug(exception, "版本使用人数上报内容无效，已跳过");
            return;
        }

        var results = await _reporter.ReportAsync([report], cancellationToken).ConfigureAwait(false);
        if (results.Count == 0 || !results[0].IsAccepted)
        {
            _logger.LogDebug("版本使用人数上报被拒绝");
            return;
        }

        _logger.LogDebug("已上报版本使用人数：{Version}", report.Version);
    }
}
