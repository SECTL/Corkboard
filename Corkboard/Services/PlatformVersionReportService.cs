using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Corkboard.Core;
using Corkboard.Core.Services.Stats;
using Corkboard.Services.Auth;

namespace Corkboard.Services;

/// <summary>
///     把本机的版本号上报给 SECTL 版本统计（<c>POST /api/stats/version</c>）。
/// </summary>
/// <remarks>
///     <para>
///         接口按身份去重统计「每个版本还有多少人」，因此载荷里只出现平台标识、版本号和伪匿名设备标识，
///         **从不**上报 SECTL 账号 ID —— 版本分布不能与账号关联。
///     </para>
///     <para>
///         API 要求每次启动上报一次，所以这个服务每个进程只跑一次：不轮询、不重试轰炸、不保留状态。
///         上报永远是尽力而为且跑在线程池上，统计失败既不阻塞启动，也不影响账号流程。
///     </para>
/// </remarks>
public sealed class PlatformVersionReportService : BackgroundService
{
    private static readonly Uri VersionReportUri = new(SectlAuthEndpoints.VersionReportUrl);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDeviceIdProvider _deviceIdProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlatformVersionReportService> _logger;

    public PlatformVersionReportService(
        IDeviceIdProvider deviceIdProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<PlatformVersionReportService> logger)
    {
        _deviceIdProvider = deviceIdProvider;
        _httpClientFactory = httpClientFactory;
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
        VersionUsageReportPayload payload;
        try
        {
            payload = VersionUsageReportPayload.Create(
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

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var response = await _httpClientFactory.CreateClient()
                .PostAsync(VersionReportUri, JsonContent.Create(payload, options: JsonOptions), timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("版本使用人数上报被拒绝：HTTP {StatusCode}", (int)response.StatusCode);
                return;
            }

            _logger.LogDebug("已上报版本使用人数：{Version}", payload.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "版本使用人数上报失败");
        }
    }
}
