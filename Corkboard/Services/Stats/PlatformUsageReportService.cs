using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Corkboard.Core;
using Corkboard.Core.Services.Stats;
using Corkboard.Shared;

namespace Corkboard.Services.Stats;

/// <summary>
///     把本机的使用量增量上报给 SECTL 平台统计（走聚合入口 <c>POST /api/stats/report</c>，
///     一条带 <c>metrics</c> 用法容器的事件就是用量上报）。
/// </summary>
/// <remarks>
///     <para>
///         只上报增量（应用启动 / 新增作业 / 成功导出），服务端累加；客户端不保存统计结果。
///         待上报的增量落在 <c>data/config/usage-stats.json</c>：断网或退出时不会丢，
///         下次启动接着传。它**不是**设置项，不能并进 <c>settings.json</c>（会被云同步互相同步掉）。
///     </para>
///     <para>
///         记录后 2 秒防抖再传（连续操作合成一次）：**一次刷写只发一个请求**，把当前所有指标
///         各装成一条事件塞进同一个 <c>events[]</c>——旧端点得一项指标一个往返。
///         失败的那几条放回队列，30 秒后重试。所有失败都只记 debug/warning，绝不影响用户操作。
///     </para>
/// </remarks>
public sealed class PlatformUsageReportService : BackgroundService
{
    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly UsageMetricCounter _counter = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly ILogger<PlatformUsageReportService> _logger;
    private readonly ISectlStatsReporter _reporter;

    public PlatformUsageReportService(ISectlStatsReporter reporter, ILogger<PlatformUsageReportService> logger)
    {
        _reporter = reporter;
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

        // 按 nameof 之外的顺序稳定性无所谓：事件顺序与结果顺序一一对应就够了。
        var metrics = pending.ToArray();

        // 服务端对单条增量的上限是 1..MaxDelta，超了整条会被拒。超出的部分拆到下一批发，
        // **不要丢掉**——离线很久再上线时攒超上限的增量照样要算进总数（`carried` 在下面放回队列，
        // 下一拍刷写接着发）。
        var sent = new List<(string Field, long Delta)>(metrics.Length);
        var carried = new Dictionary<string, long>(StringComparer.Ordinal);
        var events = new List<StatsReportEvent>(metrics.Length);
        foreach (var (field, total) in metrics)
        {
            var chunk = Math.Min(total, StatsReportEvent.MaxDelta);
            if (total > chunk)
                carried[field] = total - chunk;

            sent.Add((field, chunk));
            events.Add(StatsReportEvent.CreateUsage(GlobalConstants.PlatformId, field, chunk));
        }

        var results = await _reporter.ReportAsync(events, cancellationToken).ConfigureAwait(false);

        // results[i] 对应 events[i]：只把没落库的那几条放回队列，成功的别再重发（会被重复计数）。
        var failed = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var index = 0; index < sent.Count; index++)
        {
            var (field, delta) = sent[index];
            if (index < results.Count && results[index].IsAccepted)
            {
                _logger.LogDebug("已上报使用量：{Field} +{Delta}", field, delta);
                continue;
            }

            _logger.LogDebug("使用量上报未落库：{Field} +{Delta}", field, delta);
            failed[field] = delta;
        }

        if (carried.Count > 0)
            _counter.Restore(carried);

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
