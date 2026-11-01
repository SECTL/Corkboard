using System.Net.Http;
using Corkboard.Core.Models.SubConfigs.Widgets;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using Corkboard.Core.Widgets.Weather;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Widgets;

/// <summary>
///     天气的取用与缓存（看板模式右上角那一行），数据源是小米天气。
///     <para>
///         规则与 <see cref="QuoteService" /> 对齐：缓存按「多少分钟刷一次」判新鲜度、
///         失败保留上一次成功的数据、从来不显示错误文字（一块看板不该被一次网络抖动变成红色告警）。
///         区别只有一个：天气变化慢，所以打开看板时<b>不</b>强制刷新，只在数据过期时去取。
///     </para>
///     <para>
///         数据源是小米那个非公开接口（<see cref="XiaomiWeatherRequest" />）。哪天它变了，
///         换掉这里的两个 HTTP 调用与解析即可，界面绑定的是 <see cref="WeatherSnapshot" />。
///     </para>
/// </summary>
public sealed class WeatherService
{
    /// <summary>单次请求的超时。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>两次请求之间的最小间隔：设置里最短也是 5 分钟，这里再兜一层。</summary>
    private static readonly TimeSpan MinRequestGap = TimeSpan.FromSeconds(60);

    private readonly MainConfigHandler _configHandler;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WeatherService> _logger;
    private readonly NtpTimeService _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WeatherSnapshot _current = WeatherSnapshot.Empty;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSuccess = DateTimeOffset.MinValue;
    private string _lastSignature = string.Empty;

    public WeatherService(
        MainConfigHandler configHandler,
        IHttpClientFactory httpClientFactory,
        NtpTimeService time,
        ILogger<WeatherService> logger)
    {
        _configHandler = configHandler;
        _httpClientFactory = httpClientFactory;
        _time = time;
        _logger = logger;
    }

    /// <summary>当前缓存着的天气（界面绑定它）。</summary>
    public WeatherSnapshot Current => _current;

    /// <summary>最近一次成功取到的时刻；一次都没成功过时为 <see langword="null" />。</summary>
    public DateTimeOffset? LastSuccessAt => _lastSuccess == DateTimeOffset.MinValue ? null : _lastSuccess;

    /// <summary>
    ///     把缓存作废：设置页换了城市时叫一声，下次取一定重新请求。
    ///     <para>不动 <see cref="LastSuccessAt" />——「上次成功」是历史事实，换了城市也不该忘掉。</para>
    /// </summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _lastSignature = string.Empty;
            _current = WeatherSnapshot.Empty;
        }
    }

    /// <summary>
    ///     取天气：缓存还新就直接给，过期了才请求。
    /// </summary>
    /// <param name="forceRefresh">用户改了城市、或者手动点了刷新。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<WeatherSnapshot> GetAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var settings = _configHandler.Data.Widgets;
        if (!settings.WeatherEnabled)
            return WeatherSnapshot.Empty;

        var signature = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{settings.WeatherLatitude}|{settings.WeatherLongitude}|{settings.WeatherCityName}|{settings.WeatherRefreshMinutes}");

        lock (_gate)
        {
            if (signature != _lastSignature)
            {
                // 换了城市：旧数据立刻作废，否则屏幕上会挂着「北京」的天气配新城市名。
                _lastSignature = signature;
                _current = WeatherSnapshot.Empty;
                forceRefresh = true;
            }
        }

        if (!forceRefresh && !IsStale(settings.WeatherRefreshMinutes))
            return _current;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && !IsStale(settings.WeatherRefreshMinutes))
                return _current;

            var now = _time.NowLocal;
            if (_lastAttempt != DateTimeOffset.MinValue && now - _lastAttempt < MinRequestGap)
                return _current;

            _lastAttempt = now;

            var snapshot = await FetchAsync(settings, cancellationToken).ConfigureAwait(false);
            if (snapshot is { HasContent: true })
            {
                _current = snapshot with { CityName = settings.WeatherCityName };
                _lastSuccess = now;
            }

            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     按城市名搜一串候选城市（设置页那个「搜一下再挑」）。搜不到或者接口出问题都返回空表，
    ///     <b>不抛异常</b>——设置页据此显示一句「没搜到这个城市」。
    /// </summary>
    public async Task<IReadOnlyList<XiaomiCity>> SearchCitiesAsync(string cityName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cityName))
            return [];

        try
        {
            var uri = XiaomiWeatherRequest.BuildCitySearchUri(cityName);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            var client = _httpClientFactory.CreateClient();
            var json = await client.GetStringAsync(uri, timeout.Token).ConfigureAwait(false);

            return XiaomiCitySearchParser.TryParse(json, out var cities) ? cities : [];
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("城市搜索超时或被取消");
            return [];
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "城市搜索失败");
            return [];
        }
    }

    /// <summary>真正去请求并解析一次天气；失败返回 <see langword="null" />。</summary>
    private async Task<WeatherSnapshot?> FetchAsync(WidgetsSettingsConfig settings, CancellationToken cancellationToken)
    {
        try
        {
            var uri = XiaomiWeatherRequest.BuildWeatherUri(settings.WeatherLatitude, settings.WeatherLongitude,
                IsDaylightNow());
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            var client = _httpClientFactory.CreateClient();
            var json = await client.GetStringAsync(uri, timeout.Token).ConfigureAwait(false);

            if (XiaomiWeatherParser.TryParse(json, out var snapshot) && snapshot is not null)
                return snapshot;

            _logger.LogDebug("天气返回了无法解析的内容");
            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("天气请求超时或被取消");
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "天气请求失败");
            return null;
        }
    }

    /// <summary>现在是不是白天（判据在 Core 的 <see cref="WeatherDaylight" />，界面选图标用的也是它）。</summary>
    private bool IsDaylightNow() => WeatherDaylight.IsDay(_time.NowLocal);

    private bool IsStale(int refreshMinutes)
    {
        if (_lastSuccess == DateTimeOffset.MinValue)
            return true;

        return _time.NowLocal - _lastSuccess >= TimeSpan.FromMinutes(Math.Max(1, refreshMinutes));
    }
}
