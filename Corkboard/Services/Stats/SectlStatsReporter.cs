using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Corkboard.Core.Services.Stats;
using Corkboard.Services.Auth;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Stats;

/// <summary>
///     <see cref="ISectlStatsReporter" /> 的实现：一律按**批量**形态把事件塞进
///     <c>{"events":[…]}</c> 发给聚合入口 <c>POST /api/stats/report</c>。
/// </summary>
/// <remarks>
///     <para>
///         即使只有一条事件也走批量形态：这样「第 i 条结果对应第 i 条事件」这条映射永远成立，
///         调用方不需要区分「单条响应摊在顶层」与「批量响应在 results[] 里」两套解析。
///         批量最外层固定回 <c>207</c>，所以成败只由 <c>results[i].status</c> 决定。
///     </para>
///     <para>
///         统计是尽力而为的旁路：除了调用方自己取消（这时按「没成功」返回，让增量留在队列里），
///         这里**不抛异常**——上报失败绝不能影响业务与启动。
///     </para>
/// </remarks>
public sealed class SectlStatsReporter : ISectlStatsReporter
{
    /// <summary>单次上报的超时（与旧端点一致）。</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>批量上报最外层固定回的 HTTP 状态码（里面全部成功也是它）。</summary>
    private const int MultiStatus = 207;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Uri ReportUri = new(SectlAuthEndpoints.StatsReportUrl);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SectlStatsReporter> _logger;

    public SectlStatsReporter(IHttpClientFactory httpClientFactory, ILogger<SectlStatsReporter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StatsReportResult>> ReportAsync(
        IReadOnlyList<StatsReportEvent> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            return [];

        StatsReportBatch batch;
        try
        {
            batch = StatsReportBatch.Create(events);
        }
        catch (ArgumentException exception)
        {
            // 构造期就有问题（空数组 / null 元素），发出去也只会换一个 400。
            _logger.LogDebug(exception, "统计上报内容无效，已跳过");
            return Repeat(eventCount: events.Count, status: null, body: null);
        }

        int httpStatus;
        string raw;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var response = await _httpClientFactory.CreateClient()
                .PostAsync(ReportUri, JsonContent.Create(batch, options: JsonOptions), timeout.Token)
                .ConfigureAwait(false);

            httpStatus = (int)response.StatusCode;
            raw = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 应用正在退出：当作没成功，增量留在本地队列里，下次启动接着传。
            return Repeat(eventCount: events.Count, status: null, body: null);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "统计上报失败");
            return Repeat(eventCount: events.Count, status: null, body: null);
        }

        JsonElement? root = null;
        StatsReportResponse? parsed = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                root = document.RootElement.Clone();
                parsed = root.Value.Deserialize<StatsReportResponse>(JsonOptions);
            }
            catch (JsonException)
            {
                // 响应不是 JSON（代理层错误页之类）：下面按 HTTP 状态码兜底。
            }
        }

        IReadOnlyList<StatsReportResult> results;
        if (parsed?.Results is { Count: > 0 } batchResults && batchResults.Count == events.Count)
        {
            results = batchResults;
        }
        else
        {
            // 单条形态（200 / 201）或形状不认识：把整份响应体当成每一条的结果。
            // ⚠️ 207 没有可用的 results[] 时**不能**当成功——那样会把没落库的事件当成已上报。
            var fallbackStatus = httpStatus == MultiStatus ? (int?)null : httpStatus;
            results = Repeat(events.Count, fallbackStatus, root);
        }

        LogOutcome(results, parsed, events.Count);
        return results;
    }

    private void LogOutcome(IReadOnlyList<StatsReportResult> results, StatsReportResponse? parsed, int eventCount)
    {
        var accepted = results.Count(item => item.IsAccepted);
        if (accepted == eventCount)
        {
            _logger.LogDebug("已上报 {Count} 条统计事件", eventCount);
            return;
        }

        if (parsed?.IsRateLimited == true)
        {
            _logger.LogDebug(
                "统计上报被限流，{RetryAfter} ms 后可重试（{Accepted}/{Count} 条已落库）",
                parsed.RetryAfterMs,
                accepted,
                eventCount);
            return;
        }

        _logger.LogDebug(
            "统计上报只落库 {Accepted}/{Count} 条：{Error} {Description}",
            accepted,
            eventCount,
            parsed?.Error ?? "-",
            parsed?.ErrorDescription ?? "-");
    }

    private static IReadOnlyList<StatsReportResult> Repeat(int eventCount, int? status, JsonElement? body)
    {
        if (eventCount <= 0)
            return [];

        var results = new StatsReportResult[eventCount];
        for (var index = 0; index < eventCount; index++)
            results[index] = new StatsReportResult(null, status, body);

        return results;
    }
}
