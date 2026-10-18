using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Corkboard.Core;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Services.Stats;
using Corkboard.Services.Auth;

namespace Corkboard.Services.Stats;

/// <summary>
///     把本机的在线状态上报给 SECTL 平台统计（<c>POST /api/stats/online</c>），
///     并缓存服务端返回的在线人数供「关于」页显示。
/// </summary>
/// <remarks>
///     <para>
///         载荷只有平台标识、伪匿名设备标识与设备类型；位置由服务端按访问地址统计，
///         **从不**上报 IP、城市或 SECTL 账号 ID —— 在线人数不能与账号关联。
///     </para>
///     <para>
///         每 2 分钟上报一次，失败只记 debug，不重试轰炸、不影响任何业务。
///         隐私设置选「停止上报」时完全不动网络（<see cref="OnlineStatusPolicy" />）。
///         宿主里注册成单例再挂成托管服务，页面读到的缓存与后台写的是同一个实例。
///     </para>
/// </remarks>
public sealed class OnlineStatusService : BackgroundService
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri ReportUri = new(SectlAuthEndpoints.OnlineReportUrl);

    private readonly MainConfigHandler _configHandler;
    private readonly IDeviceIdProvider _deviceIdProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OnlineStatusService> _logger;

    private int _onlineCount;
    private int _hasOnlineCount;

    public OnlineStatusService(
        MainConfigHandler configHandler,
        IDeviceIdProvider deviceIdProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<OnlineStatusService> logger)
    {
        _configHandler = configHandler;
        _deviceIdProvider = deviceIdProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // 从云端恢复设置会整个换掉配置对象，隐私开关可能因此变成「停止上报」。
        _configHandler.Reloaded += OnConfigReloaded;
    }

    /// <summary>服务端最近一次返回的在线人数；<see cref="HasOnlineCount" /> 为假时这个值没有意义。</summary>
    public int OnlineCount => Volatile.Read(ref _onlineCount);

    /// <summary>有没有拿到过在线人数（没上报过或服务端没给数字时为假，界面显示「—」）。</summary>
    public bool HasOnlineCount => Volatile.Read(ref _hasOnlineCount) != 0;

    /// <summary>在线人数变化时触发（后台线程），页面订阅后要在离开可视树时解开。</summary>
    public event EventHandler? OnlineCountChanged;

    /// <summary>在线状态上报是否开着（隐私设置不是「停止上报」）。</summary>
    public bool IsEnabled =>
        OnlineStatusPolicy.ShouldReport(_configHandler.Data.General.PrivacySettings.OnlineStatusMode);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ExecuteAsync 的前置代码跑在启动线程上：先让出，避免构造请求拖慢启动。
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReportOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "在线状态上报失败");
            }

            try
            {
                await Task.Delay(ReportInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     查一次在线人数（「关于」页打开时用）。隐私设置是「停止上报」时直接返回空，不发请求。
    /// </summary>
    public async Task<int?> GetOnlineStatsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var response = await _httpClientFactory.CreateClient()
                .GetAsync(new Uri(SectlAuthEndpoints.OnlineStatsUrl(GlobalConstants.PlatformId)), timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("在线人数查询被拒绝：HTTP {StatusCode}", (int)response.StatusCode);
                return null;
            }

            var result = await response.Content
                .ReadFromJsonAsync<OnlineStatsResult>(JsonOptions, timeout.Token)
                .ConfigureAwait(false);
            if (result?.ResolveOnlineCount() is not { } count)
            {
                _logger.LogDebug("在线人数查询响应里没有 online_count");
                return null;
            }

            SetOnlineCount(count);
            return count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "在线人数查询失败");
            return null;
        }
    }

    public override void Dispose()
    {
        _configHandler.Reloaded -= OnConfigReloaded;
        base.Dispose();
    }

    private async Task ReportOnceAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            _logger.LogDebug("在线状态上报已关闭，跳过");
            return;
        }

        OnlineStatusPayload payload;
        try
        {
            payload = OnlineStatusPayload.Create(
                GlobalConstants.PlatformId,
                _deviceIdProvider.GetOrCreate().ToString("D"));
        }
        catch (ArgumentException exception)
        {
            // 设备标识不合规只影响统计，绝不能影响运行。
            _logger.LogDebug(exception, "在线状态上报内容无效，已跳过");
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var response = await _httpClientFactory.CreateClient()
            .PostAsync(ReportUri, JsonContent.Create(payload, options: JsonOptions), timeout.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("在线状态上报被拒绝：HTTP {StatusCode}", (int)response.StatusCode);
            return;
        }

        var result = await response.Content
            .ReadFromJsonAsync<OnlineStatsResult>(JsonOptions, timeout.Token)
            .ConfigureAwait(false);
        if (result?.ResolveOnlineCount() is { } count)
            SetOnlineCount(count);

        _logger.LogDebug("已上报在线状态：{DeviceType}", payload.DeviceType);
    }

    private void OnConfigReloaded(object? sender, EventArgs e)
    {
        if (IsEnabled)
            return;

        // 关掉上报后不保留旧数字，避免「已经停止上报」还显示一个过期的在线人数。
        Interlocked.Exchange(ref _hasOnlineCount, 0);
        Interlocked.Exchange(ref _onlineCount, 0);
        OnlineCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetOnlineCount(int count)
    {
        Interlocked.Exchange(ref _onlineCount, count);
        Interlocked.Exchange(ref _hasOnlineCount, 1);
        OnlineCountChanged?.Invoke(this, EventArgs.Empty);
    }
}
