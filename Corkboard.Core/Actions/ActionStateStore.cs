using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Corkboard.Core.Actions;

/// <summary>
///     行动状态文件 <c>action/state.json</c>：调度水位线 + 「今天不执行」抑制表。
///     <para>
///         它只是<b>缓存性质</b>的状态：损坏、缺失、版本比当前新都不该让功能起不来，
///         所以加载失败一律退回安全默认（水位线为空、抑制表为空）且不抛异常。
///         写入同样是原子写；写失败只记日志 —— 记一条执行记录都要紧，不能被状态文件拖住。
///     </para>
///     <para>
///         抑制表按「源 Id → 日期」存（每个源只记最近一次），加载 / 保存时顺手清掉
///         <see cref="SuppressedRetentionDays" /> 天前的条目，文件不会越滚越大。
///     </para>
/// </summary>
public sealed class ActionStateStore
{
    /// <summary>抑制条目保留天数（过期自动清掉；窗口只需要「今天不执行」这一档）。</summary>
    public const int SuppressedRetentionDays = 7;

    /// <summary>当前能读写的 state.json 版本。</summary>
    public const int CurrentVersion = 1;

    private readonly ActionStorePaths _paths;
    private readonly ILogger<ActionStateStore>? _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, DateOnly> _suppressed = new(StringComparer.Ordinal);

    private DateTime? _lastChecked;
    private bool _readOnly;
    private string? _loadError;

    /// <summary>按路径表建存储并立即读一次盘（读坏不抛）。</summary>
    public ActionStateStore(ActionStorePaths paths, ILogger<ActionStateStore>? logger = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger;
        Load();
    }

    /// <summary>上一次扫描到的时刻（水位线，补发窗口按它算）。改完记得 <see cref="Save" />。</summary>
    public DateTime? LastChecked
    {
        get
        {
            lock (_sync)
            {
                return _lastChecked;
            }
        }
        set
        {
            lock (_sync)
            {
                _lastChecked = value;
            }
        }
    }

    /// <summary>state.json 版本比当前新时为真；此时 <see cref="Save" /> 不覆盖文件。</summary>
    public bool IsReadOnly
    {
        get
        {
            lock (_sync)
            {
                return _readOnly;
            }
        }
    }

    /// <summary>加载失败 / 只读的原因（正常时为空），只用于日志与诊断。</summary>
    public string? LoadError
    {
        get
        {
            lock (_sync)
            {
                return _loadError;
            }
        }
    }

    /// <summary><paramref name="sourceId" /> 这个源在 <paramref name="date" /> 这一天是不是被「今天不执行」了。</summary>
    public bool IsSuppressed(string sourceId, DateOnly date)
    {
        if (string.IsNullOrEmpty(sourceId))
            return false;

        lock (_sync)
        {
            return _suppressed.TryGetValue(sourceId, out var value) && value == date;
        }
    }

    /// <summary>记下「这个源今天不执行」。只改内存，<see cref="Save" /> 才落盘。</summary>
    public void Suppress(string sourceId, DateOnly date)
    {
        if (string.IsNullOrEmpty(sourceId))
            return;

        lock (_sync)
        {
            _suppressed[sourceId] = date;
            PruneSuppressed(_suppressed, DateOnly.FromDateTime(DateTime.Now));
        }
    }

    /// <summary>撤销「今天不执行」（窗口里「恢复今天执行」用）；日期对不上就什么也不做。</summary>
    public void Unsuppress(string sourceId, DateOnly date)
    {
        if (string.IsNullOrEmpty(sourceId))
            return;

        lock (_sync)
        {
            if (_suppressed.TryGetValue(sourceId, out var value) && value == date)
                _suppressed.Remove(sourceId);
        }
    }

    /// <summary>原子写回 state.json。失败只记日志，不抛（状态丢一拍不影响下一拍重新算）。</summary>
    public void Save()
    {
        lock (_sync)
        {
            if (_readOnly)
            {
                _logger?.LogWarning("state.json 版本高于当前支持的版本，跳过写入");
                return;
            }

            PruneSuppressed(_suppressed, DateOnly.FromDateTime(DateTime.Now));

            var temp = _paths.StateFile + ".tmp";
            try
            {
                Directory.CreateDirectory(_paths.Root);

                var file = new ActionStateFile
                {
                    Version = CurrentVersion,
                    LastChecked = _lastChecked,
                    Suppressed = _suppressed.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        StringComparer.Ordinal)
                };

                var json = JsonSerializer.Serialize(file, ActionJson.Options);
                File.WriteAllText(temp, json);
                File.Move(temp, _paths.StateFile, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
                {
                    _logger?.LogWarning(cleanupError, "清理 state.json 的临时文件失败：{Temp}", temp);
                }

                _logger?.LogWarning(ex, "写入行动状态失败：{File}", _paths.StateFile);
            }
        }
    }

    private void Load()
    {
        var suppressed = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        DateTime? lastChecked = null;
        var readOnly = false;
        string? loadError = null;

        try
        {
            if (File.Exists(_paths.StateFile))
            {
                var json = File.ReadAllText(_paths.StateFile);
                var file = JsonSerializer.Deserialize<ActionStateFile>(json, ActionJson.Options);
                var version = file?.Version ?? CurrentVersion;

                if (version > CurrentVersion)
                {
                    readOnly = true;
                    loadError = $"state.json 的版本是 {version}，当前版本只支持到 {CurrentVersion}";
                    _logger?.LogWarning("行动状态文件版本 {Version} 高于当前支持的 {Current}，跳过写入", version,
                        CurrentVersion);
                }

                lastChecked = file?.LastChecked;
                suppressed = ParseSuppressed(file?.Suppressed);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException or InvalidOperationException)
        {
            // 状态文件是缓存，读坏就当没写过：水位线清空（下一拍最多倒退到 now-2 分钟）、抑制表清空。
            suppressed.Clear();
            lastChecked = null;
            loadError = "state.json 读取失败，按安全默认（无水位线、无抑制）继续";
            _logger?.LogWarning(ex, "读取行动状态失败，按安全默认继续：{File}", _paths.StateFile);
        }

        PruneSuppressed(suppressed, DateOnly.FromDateTime(DateTime.Now));

        lock (_sync)
        {
            _suppressed.Clear();
            foreach (var pair in suppressed)
                _suppressed[pair.Key] = pair.Value;

            _lastChecked = lastChecked;
            _readOnly = readOnly;
            _loadError = loadError;
        }
    }

    private static Dictionary<string, DateOnly> ParseSuppressed(IDictionary<string, string>? raw)
    {
        var result = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        if (raw is null)
            return result;

        foreach (var pair in raw)
        {
            if (string.IsNullOrEmpty(pair.Key))
                continue;

            if (DateOnly.TryParseExact(pair.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                result[pair.Key] = date;
        }

        return result;
    }

    private static void PruneSuppressed(Dictionary<string, DateOnly> suppressed, DateOnly today)
    {
        var cutoff = today.AddDays(-SuppressedRetentionDays);
        var stale = suppressed.Where(pair => pair.Value < cutoff).Select(pair => pair.Key).ToList();
        foreach (var key in stale)
            suppressed.Remove(key);
    }
}

/// <summary>
///     state.json 的外壳（<c>{ "version": 1, "last_checked": ..., "suppressed": { ... } }</c>）。
/// </summary>
internal sealed class ActionStateFile
{
    /// <summary>格式版本。</summary>
    public int Version { get; set; } = ActionStateStore.CurrentVersion;

    /// <summary>上一次扫描到的时刻。</summary>
    public DateTime? LastChecked { get; set; }

    /// <summary>源 Id → <c>yyyy-MM-dd</c>。</summary>
    public Dictionary<string, string>? Suppressed { get; set; }
}
