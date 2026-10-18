using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Corkboard.Core;
using Corkboard.Core.Services.Stats;
using Corkboard.Services.Auth;
using Corkboard.Shared;

namespace Corkboard.Services.Stats;

/// <summary>
///     把本机的使用量增量上报给 SECTL 平台统计（<c>POST /api/stats/usage/increment</c>）。
/// </summary>
/// <remarks>
///     <para>
///         只上报增量（应用启动 / 新增作业 / 成功导出），服务端累加；客户端不保存统计结果。
///         待上报的增量落在 <c>data/config/usage-stats.json</c>：断网或退出时不会丢，
///         下次启动接着传。它**不是**设置项，不能并进 <c>settings.json</c>（会被云同步互相同步掉）。
///     </para>
///     <para>
///         记录后 2 秒防抖再传（连续操作合成一次）；失败把增量放回队列，30 秒后重试。
///         所有失败都只记 debug/warning，绝不影响用户操作。
///     </para>
/// </remarks>
public sealed class PlatformUsageReportService : BackgroundService
{
    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri IncrementUri = new(SectlAuthEndpoints.UsageIncrementUrl);

    private readonly UsageMetricCounter _counter = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlatformUsageReportService> _logger;

    public PlatformUsageReportService(IHttpClientFactory httpClientFactory, ILogger<PlatformUsageReportService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        PendingFilePath = Utils.GetFilePath("config", "usage-stats.json");
        LoadPending();
    }

    /// <summary>待上报增量的落盘位置（<c>data/config/usage-stats.json</c>）。</summary>
    public string PendingFilePath { get; }

    /// <summary>记一次应用启动（服务启动时自己调，页面不用管）。</summary>
    public void RecordAppLaunch() => Record(UsageMetricNames.AppLaunch);

    /// <summary>累加一项使用量并排一次上报。</summary>
    public void Record(string metric)
    {
        try
        {
            _counter.Add(metric);
        }
        catch (ArgumentException exception)
        {
            _logger.LogDebug(exception, "使用量指标名为空，已忽略");
            return;
        }

        PersistPending();
        Signal();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunAsync(stoppingToken), CancellationToken.None);

    public override void Dispose()
    {
        _signal.Dispose();
        base.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // 每次进程启动都算一次使用：这是版本统计之外最基础的「有多少人在用」。
        RecordAppLaunch();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(FlushDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var success = await FlushAsync(cancellationToken).ConfigureAwait(false);
            if (success || cancellationToken.IsCancellationRequested)
                continue;

            try
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>把当前待上报的增量整批发出去；失败时放回队列并返回 <see langword="false" />。</summary>
    private async Task<bool> FlushAsync(CancellationToken cancellationToken)
    {
        var pending = _counter.TakePending();
        if (pending.Count == 0)
            return true;

        var failed = new Dictionary<string, long>(StringComparer.Ordinal);
        var client = _httpClientFactory.CreateClient();

        foreach (var (field, delta) in pending)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                failed[field] = delta;
                continue;
            }

            try
            {
                var payload = UsageIncrementPayload.Create(GlobalConstants.PlatformId, field, delta);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(RequestTimeout);
                using var response = await client
                    .PostAsync(IncrementUri, JsonContent.Create(payload, options: JsonOptions), timeout.Token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("使用量上报被拒绝：{Field} HTTP {StatusCode}", field, (int)response.StatusCode);
                    failed[field] = delta;
                }
                else
                {
                    _logger.LogDebug("已上报使用量：{Field} +{Delta}", field, delta);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                failed[field] = delta;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "使用量上报失败：{Field}", field);
                failed[field] = delta;
            }
        }

        if (failed.Count > 0)
            _counter.Restore(failed);

        PersistPending();
        return failed.Count == 0;
    }

    private void Signal()
    {
        try
        {
            if (_signal.CurrentCount == 0)
                _signal.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    #region 待上报增量落盘

    private void LoadPending()
    {
        try
        {
            if (!File.Exists(PendingFilePath))
                return;

            var json = File.ReadAllText(PendingFilePath);
            var record = JsonSerializer.Deserialize<UsageMetricSnapshot>(json, JsonOptions);
            _counter.Restore(record?.Pending);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(exception, "读取待上报使用量失败，本次从零开始。");
        }
    }

    private void PersistPending()
    {
        try
        {
            var snapshot = _counter.Snapshot();
            var directory = Path.GetDirectoryName(PendingFilePath)!;
            Directory.CreateDirectory(directory);

            var content = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new UsageMetricSnapshot(snapshot), JsonOptions));
            var temporary = $"{PendingFilePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllBytes(temporary, content);
                File.Move(temporary, PendingFilePath, overwrite: true);
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "保存待上报使用量失败，增量只留在本次运行内。");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record UsageMetricSnapshot(
        [property: JsonPropertyName("pending")] IReadOnlyDictionary<string, long>? Pending);

    #endregion
}
