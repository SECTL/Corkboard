using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Platform;
using Avalonia.Threading;
using Corkboard.Core.DutyRoster;
using Corkboard.Core.Holidays;
using Corkboard.Core.Time;
using Corkboard.Services.Updates;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Holidays;

/// <summary>一次刷新的结果（给节假日窗口的状态行用）。</summary>
public sealed record HolidayRefreshResult(bool Success, int UpdatedYears, int FailedYears, string? Message)
{
    public bool Busy { get; init; }
}

/// <summary>节假日服务的当前状态：上次成功 / 检查时间、最近一次错误、是否正在刷新。</summary>
public sealed class HolidayServiceStatus
{
    public DateTimeOffset? LastSuccessUtc { get; init; }

    public DateTimeOffset? LastCheckUtc { get; init; }

    public string? LastError { get; init; }

    public bool IsRefreshing { get; init; }

    /// <summary>有在线缓存的年份数。</summary>
    public int CachedYearCount { get; init; }

    /// <summary>随包内置数据的年份数。</summary>
    public int BuiltinYearCount { get; init; }
}

/// <summary>
///     节假日服务（单例）：拉取 → 校验 → 缓存 → 合并，对界面暴露一个 <see cref="IDutyHolidayCalendar" />。
///     <para>
///         数据链路：在线缓存（<c>data/holidays/online/{year}.json</c>）→ 随包内置 → 用户覆盖；
///         读盘与合并全在 <see cref="HolidayCatalog" /> / <see cref="HolidayCacheStore" /> 里，这里只管 HTTP、
///         刷新策略与状态。**不要**在别处另写一套合并逻辑。
///     </para>
///     <para>
///         ⚠️ 失败只记 Warning：请求失败、解析失败、校验失败都**不删缓存**，界面上照旧能用上一份数据；
///         任何情况下都不让「今天没网」变成「值日表停摆」。
///     </para>
///     <para>
///         时间一律走 <see cref="NtpTimeService" />（<c>NowLocal</c> 判今天、<c>UtcNow</c> 记抓取时间），
///         不用 <see cref="DateTime.Now" />。
///     </para>
/// </summary>
public sealed class HolidayService : IDutyHolidayCalendar, IDisposable
{
    /// <summary>App.axaml.cs 里注册的命名 HttpClient（超时 15 秒；请求另有逐次 CTS）。</summary>
    public const string HttpClientName = "holidays";

    /// <summary>单份响应上限：实测 4–5 KB，给 256 KB 足够且能挡住错误页面。</summary>
    public const int MaximumResponseBytes = 256 * 1024;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private const string BundledAssetRoot = "avares://Corkboard/Assets/Holidays/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HolidayCacheStore _store;
    private readonly NtpTimeService _time;
    private readonly ILogger<HolidayService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly IReadOnlyDictionary<int, HolidayYearData> _builtin;

    private volatile HolidayCatalog _catalog;
    private IReadOnlyDictionary<int, HolidayYearData> _online;
    private HolidayOverrides _overrides;
    private string? _lastError;
    private volatile bool _isRefreshing;

    public HolidayService(
        IHttpClientFactory httpClientFactory,
        HolidayCacheStore store,
        NtpTimeService time,
        ILogger<HolidayService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _store = store;
        _time = time;
        _logger = logger;

        _overrides = _store.LoadOverrides();
        _online = _store.LoadAllYears();
        _builtin = LoadBundledYears();
        _catalog = new HolidayCatalog(_overrides, _online, _builtin);
        Status = BuildStatus();
    }

    /// <summary>缓存 / 覆盖 / 数据源发生变化（刷新完成、用户改覆盖）。界面收到后要重建表格。</summary>
    public event EventHandler? Changed;

    /// <summary>当前合并后的查询对象。</summary>
    public HolidayCatalog Catalog => _catalog;

    /// <summary>在线缓存（年份 → 数据）。</summary>
    public IReadOnlyDictionary<int, HolidayYearData> OnlineYears => _online;

    /// <summary>随包内置（年份 → 数据）。</summary>
    public IReadOnlyDictionary<int, HolidayYearData> BuiltinYears => _builtin;

    public HolidayServiceStatus Status { get; private set; }

    /// <summary>今天（校时后的本地日期）。</summary>
    public DateOnly Today => DateOnly.FromDateTime(_time.NowLocal);

    // ── IDutyHolidayCalendar：全部委托给当前 catalog ────────────────────────

    public bool IsLegalHoliday(DateOnly date) => _catalog.IsLegalHoliday(date);

    public bool IsMakeupWorkday(DateOnly date) => _catalog.IsMakeupWorkday(date);

    public bool IsVacation(DateOnly date) => _catalog.IsVacation(date);

    // ── 刷新 ────────────────────────────────────────────────────────────────

    /// <summary>
    ///     按年拉取节假日数据。
    ///     <para>
    ///         <paramref name="year" /> 为 null 时：强制刷新拉 [今年-1, 今年, 今年+1]；非强制按
    ///         「今年 7 天、次年 10 月起 7 天 / 其余 30 天、去年只补一次」的策略挑年份。
    ///     </para>
    ///     <para>同一时刻只允许一次刷新在跑（手动点按钮撞上后台任务时直接返回 Busy）。</para>
    /// </summary>
    public async Task<HolidayRefreshResult> RefreshAsync(
        int? year = null,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return new HolidayRefreshResult(false, 0, 0, "正在刷新") { Busy = true };

        try
        {
            _isRefreshing = true;
            RaiseChanged();

            var years = year is { } single
                ? [single]
                : force ? ProbeYears() : GetDueYears();

            var state = _store.LoadState();
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var updated = 0;
            var failed = 0;
            var firstError = _lastError;

            foreach (var target in years.Distinct().Order())
            {
                var yearState = state.Get(target);
                if (!force && yearState.NextRetryUtc is { } nextRetry && nextRetry > _time.UtcNow)
                    continue;

                var (ok, error) = await FetchYearAsync(client, target, yearState, cancellationToken)
                    .ConfigureAwait(false);
                _store.SaveState(state);

                if (ok)
                {
                    updated++;
                }
                else
                {
                    failed++;
                    firstError ??= error;
                }
            }

            if (updated > 0)
                ReloadFromDisk(clearError: true);
            else
                ReloadFromDisk(clearError: false);

            if (failed > 0)
                _lastError = firstError ?? "拉取失败";

            Status = BuildStatus();
            RaiseChanged();

            var message = failed == 0
                ? null
                : firstError ?? $"有 {failed} 个年份没有拉到数据，继续使用缓存";
            return new HolidayRefreshResult(failed == 0, updated, failed, message);
        }
        finally
        {
            _isRefreshing = false;
            _refreshGate.Release();
            Status = BuildStatus();
            RaiseChanged();
        }
    }

    /// <summary>后台托管的「该刷新才刷新」：不强制、按退避跳过刚失败的年份。</summary>
    public Task<HolidayRefreshResult> RefreshIfDueAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(year: null, force: false, cancellationToken);

    private IReadOnlyList<int> ProbeYears()
    {
        var current = Today.Year;
        return [current - 1, current, current + 1];
    }

    /// <summary>
    ///     刷新策略（<c>.tmp/duty-roster-plan.md</c> §7）：今年 7 天一次；次年 10 月起每周试、
    ///     其余月份 30 天试一次、没成功过就不试；去年只在从没成功过时补一次。
    /// </summary>
    private IReadOnlyList<int> GetDueYears()
    {
        var today = Today;
        var utcNow = _time.UtcNow;
        var state = _store.LoadState();

        // 第一次运行（还没有任何状态）：三年都试一遍。
        if (state.Years.Count == 0)
            return ProbeYears();

        var years = new List<int>(3);
        var current = today.Year;

        var currentState = state.Years.GetValueOrDefault(current);
        if (currentState?.LastSuccessUtc is null
            || utcNow - currentState.LastSuccessUtc.Value >= TimeSpan.FromDays(7))
        {
            years.Add(current);
        }

        var next = current + 1;
        var nextState = state.Years.GetValueOrDefault(next);
        if (nextState?.LastSuccessUtc is null)
        {
            if (today.Month >= 10)
                years.Add(next);
        }
        else
        {
            var period = today.Month >= 10 ? TimeSpan.FromDays(7) : TimeSpan.FromDays(30);
            if (utcNow - nextState.LastSuccessUtc.Value >= period)
                years.Add(next);
        }

        if (state.Years.GetValueOrDefault(current - 1)?.LastSuccessUtc is null)
            years.Add(current - 1);

        return years;
    }

    private async Task<(bool Success, string? Error)> FetchYearAsync(
        HttpClient client,
        int year,
        HolidayYearState state,
        CancellationToken cancellationToken)
    {
        var lastError = "没有可用的数据源";

        foreach (var endpoint in HolidaySourceCatalog.Candidates(year))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url);
                request.Headers.Accept.ParseAdd("application/json");

                // ETag 只对同源地址有意义：换了源（主源 ↔ 备源）就别带，免得对不上。
                if (!string.IsNullOrWhiteSpace(state.ETag)
                    && string.Equals(state.SourceName, endpoint.SourceName, StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", state.ETag);
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(RequestTimeout);

                using var response = await client.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                    .ConfigureAwait(false);

                state.LastCheckUtc = _time.UtcNow;

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    state.LastSuccessUtc = _time.UtcNow;
                    state.ConsecutiveFailures = 0;
                    state.NextRetryUtc = null;
                    return (true, null);
                }

                if (!response.IsSuccessStatusCode)
                {
                    lastError = $"HTTP {(int)response.StatusCode}";
                    _logger.LogWarning(
                        "拉取 {Year} 年节假日返回 HTTP {Status}：{Url}", year, (int)response.StatusCode, endpoint.Url);
                    continue;
                }

                var payload = await BoundedContentReader
                    .ReadAsync(response.Content, MaximumResponseBytes, timeoutCts.Token)
                    .ConfigureAwait(false);

                var json = Encoding.UTF8.GetString(payload);
                if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '{')
                {
                    lastError = "返回内容不是 JSON";
                    continue;
                }

                if (!HolidayJsonParser.TryParse(json, year, out var data) || data is null)
                {
                    lastError = "解析失败";
                    continue;
                }

                if (!HolidayDataRules.IsPlausible(data, out var reason))
                {
                    lastError = reason;
                    _logger.LogWarning(
                        "拉取 {Year} 年节假日数据不可信（{Reason}）：{Url}", year, reason, endpoint.Url);
                    continue;
                }

                if (!HolidayDataRules.IsYearConsistent(data, year))
                {
                    lastError = "数据年份不对";
                    continue;
                }

                var fetchedAt = _time.UtcNow;
                data.FetchedAt = fetchedAt;
                data.SourceName = endpoint.SourceName;
                data.SourceUrl = endpoint.SourceUrl;
                data.License = endpoint.License;

                state.ETag = response.Headers.ETag?.Tag;
                state.LastModified = response.Content.Headers.LastModified?.ToString("R");
                state.FetchedAtUtc = fetchedAt;
                state.LastSuccessUtc = fetchedAt;
                state.ConsecutiveFailures = 0;
                state.NextRetryUtc = null;
                state.SourceName = endpoint.SourceName;

                if (!_store.SaveYear(year, json, state))
                {
                    lastError = "写入缓存失败";
                    continue;
                }

                return (true, null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 单次请求超时：算这个源失败，继续下一档。
                lastError = "请求超时";
                _logger.LogWarning("拉取 {Year} 年节假日超时：{Url}", year, endpoint.Url);
            }
            catch (OperationCanceledException)
            {
                // 调用方取消：必须原样抛出去，不能当成数据源失败。
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException
                                                  or InvalidDataException or JsonException or NotSupportedException)
            {
                lastError = exception.Message;
                _logger.LogWarning(exception, "拉取 {Year} 年节假日失败：{Url}", year, endpoint.Url);
            }
        }

        state.LastCheckUtc = _time.UtcNow;
        state.ConsecutiveFailures++;
        state.NextRetryUtc = _time.UtcNow + Backoff(state.ConsecutiveFailures);
        _logger.LogWarning(
            "拉取 {Year} 年节假日全部数据源失败（{Error}），继续使用缓存", year, lastError);
        return (false, lastError);
    }

    /// <summary>失败退避：30 分钟起翻倍，封顶 24 小时。</summary>
    public static TimeSpan Backoff(int consecutiveFailures)
    {
        var exponent = Math.Clamp(consecutiveFailures - 1, 0, 6);
        var minutes = Math.Min(30 * Math.Pow(2, exponent), 24 * 60);
        return TimeSpan.FromMinutes(minutes);
    }

    // ── 用户覆盖 / 假期区间 ─────────────────────────────────────────────────

    /// <summary>某天的用户覆盖状态。</summary>
    public HolidayOverrideState OverrideStateOf(DateOnly date) => _overrides.StateOf(date);

    /// <summary>读某天的覆盖备注；没有覆盖返回空串。</summary>
    public string OverrideNoteOf(DateOnly date) =>
        _overrides.Days.Find(item => item.Date == date)?.Note ?? string.Empty;

    /// <summary>用户自建假期区间（只读快照）。</summary>
    public IReadOnlyList<HolidayVacationRange> Vacations => _overrides.Vacations;

    /// <summary>先按 <see cref="HolidayOverrideState.Auto" /> 移除；error/写入失败返回 false。</summary>
    public bool SetOverride(DateOnly date, HolidayOverrideState state, string? note = null)
    {
        _overrides.Days.RemoveAll(item => item.Date == date);
        if (state != HolidayOverrideState.Auto)
        {
            _overrides.Days.Add(new HolidayOverride
            {
                Date = date,
                State = state,
                Note = note?.Trim() ?? string.Empty
            });
        }

        return PersistOverrides();
    }

    /// <summary>删除某天的覆盖 = 恢复按数据源判定。</summary>
    public bool ClearOverride(DateOnly date) => SetOverride(date, HolidayOverrideState.Auto);

    /// <summary>加一段「不排班」的假期区间（寒暑假 / 校历调整）。</summary>
    public bool AddVacation(DateOnly start, DateOnly end, string? note = null)
    {
        if (end < start)
            end = start;

        _overrides.Vacations.Add(new HolidayVacationRange
        {
            Start = start,
            End = end,
            Note = note?.Trim() ?? string.Empty
        });

        return PersistOverrides();
    }

    /// <summary>删除一段假期区间（按起止日期定位，同起止的只删第一条）。</summary>
    public bool RemoveVacation(DateOnly start, DateOnly end)
    {
        var index = _overrides.Vacations.FindIndex(range => range.Start == start && range.End == end);
        if (index < 0)
            return false;

        _overrides.Vacations.RemoveAt(index);
        return PersistOverrides();
    }

    private bool PersistOverrides()
    {
        if (!_store.SaveOverrides(_overrides))
            return false;

        _catalog = new HolidayCatalog(_overrides, _online, _builtin);
        Status = BuildStatus();
        RaiseChanged();
        return true;
    }

    private void ReloadFromDisk(bool clearError)
    {
        _overrides = _store.LoadOverrides();
        _online = _store.LoadAllYears();
        _catalog = new HolidayCatalog(_overrides, _online, _builtin);
        if (clearError)
            _lastError = null;
    }

    // ── 状态 / 内置数据 ─────────────────────────────────────────────────────

    private HolidayServiceStatus BuildStatus()
    {
        var state = _store.LoadState();
        var successes = state.Years.Values
            .Select(item => item.LastSuccessUtc)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToList();
        var checks = state.Years.Values
            .Select(item => item.LastCheckUtc)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToList();

        return new HolidayServiceStatus
        {
            LastSuccessUtc = successes.Count > 0 ? successes.Max() : null,
            LastCheckUtc = checks.Count > 0 ? checks.Max() : null,
            LastError = _lastError,
            IsRefreshing = _isRefreshing,
            CachedYearCount = _online.Count,
            BuiltinYearCount = _builtin.Count
        };
    }

    /// <summary>
    ///     读随包内置数据（<c>Corkboard/Assets/Holidays/{year}.json</c>，与在线源同格式）。
    ///     文件不存在就跳过——内置只是兜底，缺了不影响在线 / 缓存链路。
    /// </summary>
    private IReadOnlyDictionary<int, HolidayYearData> LoadBundledYears()
    {
        var result = new Dictionary<int, HolidayYearData>();
        var currentYear = Today.Year;

        foreach (var year in Enumerable.Range(currentYear - 2, 5))
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri(BundledAssetRoot + year + ".json"));
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var json = reader.ReadToEnd();
                if (!HolidayJsonParser.TryParse(json, year, out var data)
                    || data is null
                    || !HolidayDataRules.IsPlausible(data, out _)
                    || !HolidayDataRules.IsYearConsistent(data, year))
                {
                    continue;
                }

                data.SourceName = HolidaySourceCatalog.BuiltinName;
                data.SourceUrl = HolidaySourceCatalog.HolidayCnRepoUrl;
                data.License = HolidaySourceCatalog.HolidayCnLicense;
                data.FetchedAt = default;
                result[year] = data;
            }
            catch (Exception exception) when (exception is FileNotFoundException or IOException
                                                  or NotSupportedException or UriFormatException)
            {
                // 该年份没有随包数据：正常情况，不用记日志。
            }
        }

        return result;
    }

    private void RaiseChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            Changed?.Invoke(this, EventArgs.Empty);
        else
            Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    public void Dispose() => _refreshGate.Dispose();
}
