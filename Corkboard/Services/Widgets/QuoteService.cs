using System.Globalization;
using System.Net.Http;
using Corkboard.Core.Models.SubConfigs.Widgets;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using Corkboard.Core.Widgets.Quote;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Widgets;

/// <summary>
///     一言的取用与缓存（看板模式底部那一行）。
///     <para>
///         三件事在这里定死，别处不要再写一遍：
///         1) <b>节流</b>——国内节点 QPS 2，所以同一进程里两次请求至少隔 30 秒；
///         2) <b>缓存</b>——「每 N 分钟换一条」由缓存的新鲜度判定，窗口每秒刷新时钟时不会每帧去请求；
///            配置里 <c>quote_refresh_minutes = 0</c> 表示「每次打开看板都换一条」，那是调用方<b>强制</b>刷新；
///         3) <b>失败保留上一条</b>——取不到就继续显示上一句，从来没有成功过则整行不画。
///     </para>
///     <para>
///         <b>屏蔽词</b>在拿到句子之后自己判（官方 v1 没有排除关键词的参数，见
///         <see cref="QuoteBlocklist" />）：命中就换一条再试，最多 <see cref="MaxAttempts" /> 次；
///         全被挡住时保留上一条——宁可屏幕上还是旧句子，也不把用户明确不想看的内容画上去。
///     </para>
///     <para>
///         它是单例（窗口开开关关共用一份缓存），订阅与计时器一概没有：拉取时机由窗口决定，
///         别在这里再挂一个后台轮询，那样会和「打开时刷新」叠成双倍请求。
///     </para>
/// </summary>
public sealed class QuoteService
{
    /// <summary>单次请求的超时：一言偶尔慢，超过这个时长就当没取到。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>两次请求之间的最小间隔（QPS 2 的保守值）。</summary>
    private static readonly TimeSpan MinRequestGap = TimeSpan.FromSeconds(30);

    /// <summary>被屏蔽词挡掉时最多要几次（含第一次）：再多就是在接口上刷请求了。</summary>
    private const int MaxAttempts = 3;

    /// <summary>重取之间的间隔：官方国内节点 QPS 2，隔开半秒就落在 2 次/秒以内。</summary>
    private static readonly TimeSpan BlockedRetryGap = TimeSpan.FromMilliseconds(500);

    private readonly MainConfigHandler _configHandler;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<QuoteService> _logger;
    private readonly NtpTimeService _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HitokotoQuote _current = HitokotoQuote.Empty;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSuccess = DateTimeOffset.MinValue;

    /// <summary>上一次取用时的选项指纹：选项一改，缓存立刻作废（不然改了类别还得等半小时）。</summary>
    private string _lastSignature = string.Empty;

    public QuoteService(
        MainConfigHandler configHandler,
        IHttpClientFactory httpClientFactory,
        NtpTimeService time,
        ILogger<QuoteService> logger)
    {
        _configHandler = configHandler;
        _httpClientFactory = httpClientFactory;
        _time = time;
        _logger = logger;
    }

    /// <summary>当前缓存着的那一句（界面绑定它）。</summary>
    public HitokotoQuote Current => _current;

    /// <summary>最近一次成功取到的时刻；一次都没成功过时为 <see langword="null" />。</summary>
    public DateTimeOffset? LastSuccessAt => _lastSuccess == DateTimeOffset.MinValue ? null : _lastSuccess;

    /// <summary>
    ///     拿一句一言：缓存还新就直接给，过期了才去请求。
    /// </summary>
    /// <param name="forceRefresh">用户刚打开看板，或者点了「换一条」——不看缓存新鲜度，能换就换。</param>
    /// <param name="cancellationToken">取消令牌（窗口关闭时取消）。</param>
    public async Task<HitokotoQuote> GetAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var settings = _configHandler.Data.Widgets;
        if (!settings.QuoteEnabled)
            return HitokotoQuote.Empty;

        var options = settings.BuildQuoteOptions();
        var blockedWords = QuoteBlocklist.Parse(settings.QuoteBlockedWords);
        var signature = BuildSignature(options, settings.QuoteRefreshMinutes, blockedWords);

        lock (_gate)
        {
            if (signature != _lastSignature)
            {
                // 选项变了：以前那条不算数，但也不立刻清屏——下面会去取新的。
                _lastSignature = signature;
                forceRefresh = true;
            }
        }

        if (!forceRefresh && !IsStale(settings.QuoteRefreshMinutes))
            return _current;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 排队期间可能已经有人取过一条了（两个窗口同时开的情况），别重复请求。
            if (!forceRefresh && !IsStale(settings.QuoteRefreshMinutes))
                return _current;

            var now = _time.NowLocal;
            if (!forceRefresh && _lastAttempt != DateTimeOffset.MinValue && now - _lastAttempt < MinRequestGap)
                return _current;

            // 强制刷新也留一个下限：连点两下「换一条」不该把 QPS 打穿。
            if (forceRefresh && _lastAttempt != DateTimeOffset.MinValue && now - _lastAttempt < TimeSpan.FromSeconds(5))
                return _current;

            _lastAttempt = now;

            try
            {
                var uri = HitokotoQuery.BuildUri(options);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(RequestTimeout);

                var client = _httpClientFactory.CreateClient();

                // 命中屏蔽词就换一条再试（有限次，间隔半秒）；全被挡住时保留上一条。
                for (var attempt = 0; attempt < MaxAttempts; attempt++)
                {
                    if (attempt > 0)
                        await Task.Delay(BlockedRetryGap, timeout.Token).ConfigureAwait(false);

                    var json = await client.GetStringAsync(uri, timeout.Token).ConfigureAwait(false);

                    if (!HitokotoQuoteParser.TryParse(json, out var quote) || quote is null)
                    {
                        _logger.LogDebug("一言返回了无法解析的内容，保留上一条");
                        break;
                    }

                    if (QuoteBlocklist.IsBlocked(quote, blockedWords))
                    {
                        _logger.LogDebug("一言命中屏蔽词，换一条再试（第 {Attempt} 次）", attempt + 1);
                        continue;
                    }

                    _current = quote;
                    _lastSuccess = _time.NowLocal;
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("一言请求超时或被取消，保留上一条");
            }
            catch (HttpRequestException exception)
            {
                _logger.LogDebug(exception, "一言请求失败，保留上一条");
            }
            catch (Exception exception)
            {
                // 网络层的意外（DNS、代理、证书）不该冒到 UI 线程上：这里兜住，屏幕上继续显示旧句子。
                _logger.LogDebug(exception, "一言请求出现意外错误，保留上一条");
            }

            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>缓存是不是已经过期。<c>0</c> = 每次打开都换，所以永远算过期（由调用方强制刷新）。</summary>
    private bool IsStale(int refreshMinutes)
    {
        if (_lastSuccess == DateTimeOffset.MinValue)
            return true;

        if (refreshMinutes <= 0)
            return true;

        return _time.NowLocal - _lastSuccess >= TimeSpan.FromMinutes(refreshMinutes);
    }

    /// <summary>选项指纹：用来判断「用户改了设置，缓存该作废」。</summary>
    private static string BuildSignature(HitokotoQueryOptions options, int refreshMinutes, IReadOnlyList<string> blockedWords) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{options.Categories}|{options.MinLength}|{options.MaxLength}|{options.ExtraQuery}|{options.UseInternationalEndpoint}|{refreshMinutes}|{string.Join('\n', blockedWords)}");
}
