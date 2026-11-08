using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Corkboard.Shared;
using Microsoft.Extensions.Logging;

namespace Corkboard.Core.Holidays;

/// <summary>某一个年份的缓存状态：ETag、上次检查 / 成功时间与退避。</summary>
public sealed class HolidayYearState
{
    public string? ETag { get; set; }

    public string? LastModified { get; set; }

    /// <summary>上一次真正发出请求的时间（含 304 与失败）。</summary>
    public DateTimeOffset? LastCheckUtc { get; set; }

    /// <summary>上一次拿到有效数据的时间（304 也算成功）。</summary>
    public DateTimeOffset? LastSuccessUtc { get; set; }

    /// <summary>数据本身的拉取时间（与 <see cref="LastSuccessUtc" /> 不同：304 时不更新它）。</summary>
    public DateTimeOffset? FetchedAtUtc { get; set; }

    public string? SourceName { get; set; }

    public int ConsecutiveFailures { get; set; }

    /// <summary>退避到什么时候；为空表示可以立刻重试。</summary>
    public DateTimeOffset? NextRetryUtc { get; set; }
}

/// <summary><c>data/holidays/online/_state.json</c> 的内容。</summary>
public sealed class HolidayCacheState
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>年份 → 状态。JSON 里是字符串键，读回来仍是整数。</summary>
    public Dictionary<int, HolidayYearState> Years { get; set; } = [];

    public HolidayYearState Get(int year)
    {
        if (!Years.TryGetValue(year, out var state))
        {
            state = new HolidayYearState();
            Years[year] = state;
        }

        return state;
    }
}

/// <summary>
///     节假日缓存与用户覆盖的落盘层：<c>data/holidays/</c>。
///     <para>
///         布局：<c>online/{year}.json</c>（上游原文）、<c>online/_state.json</c>（ETag / 时间 / 退避）、
///         <c>overrides.json</c>（逐日覆盖 + 假期区间）。
///     </para>
///     <para>
///         ⚠️ 读盘一律容错：文件不存在、半截 JSON、日期坏了都只返回 <c>false</c> / 空对象，**绝不抛异常**；
///         写盘走「临时文件 + 覆盖 Move」的原子替换。失败只记 Warning，不清缓存——
///         离线时必须还能用上一份数据（见 <c>.tmp/duty-roster-plan.md</c> §7）。
///     </para>
///     <para>
///         根目录可注入（单测用临时目录），默认 <c>Utils.GetDirectoryPath("holidays")</c>。
///     </para>
/// </summary>
public sealed class HolidayCacheStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly ILogger<HolidayCacheStore>? _logger;

    public HolidayCacheStore(string? root = null, ILogger<HolidayCacheStore>? logger = null)
    {
        Root = string.IsNullOrWhiteSpace(root) ? Utils.GetDirectoryPath("holidays") : root;
        _logger = logger;
        OnlineDirectory = Path.Combine(Root, "online");
        StateFilePath = Path.Combine(OnlineDirectory, "_state.json");
        OverridesFilePath = Path.Combine(Root, "overrides.json");
    }

    /// <summary><c>data/holidays</c> 的绝对路径。</summary>
    public string Root { get; }

    public string OnlineDirectory { get; }

    public string StateFilePath { get; }

    public string OverridesFilePath { get; }

    public string YearFilePath(int year) => Path.Combine(OnlineDirectory, year.ToString() + ".json");

    // ── 在线数据 ────────────────────────────────────────────────────────────

    /// <summary>读某一年的缓存；不存在 / 坏了 / 校验不过都返回 false。</summary>
    public bool TryLoadYear(int year, out HolidayYearData? data)
    {
        data = null;
        try
        {
            var path = YearFilePath(year);
            if (!File.Exists(path))
                return false;

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (!HolidayJsonParser.TryParse(json, year, out var parsed)
                || !HolidayDataRules.IsPlausible(parsed, out _))
            {
                return false;
            }

            var state = LoadState().Years.GetValueOrDefault(year);
            parsed!.FetchedAt = state?.FetchedAtUtc
                                ?? state?.LastSuccessUtc
                                ?? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            data = parsed;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or JsonException or NotSupportedException)
        {
            _logger?.LogWarning(exception, "读取 {Year} 年节假日缓存失败，按没有缓存处理", year);
            return false;
        }
    }

    /// <summary>读全部有缓存的年份（按年份升序）；坏文件跳过。</summary>
    public IReadOnlyDictionary<int, HolidayYearData> LoadAllYears()
    {
        var result = new Dictionary<int, HolidayYearData>();
        try
        {
            if (!Directory.Exists(OnlineDirectory))
                return result;

            foreach (var path in Directory.EnumerateFiles(OnlineDirectory, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!int.TryParse(name, out var year) || year is < 1900 or > 2200)
                    continue;

                if (TryLoadYear(year, out var data) && data is not null)
                    result[year] = data;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(exception, "枚举节假日缓存目录失败");
        }

        return result;
    }

    /// <summary>
    ///     写一年的缓存：原文按上游格式存，状态一起更新。
    ///     <para>调用方负责先校验数据；这里只保证「写出去了就是完整的」。</para>
    /// </summary>
    public bool SaveYear(int year, string rawJson, HolidayYearState state)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return false;

        try
        {
            Directory.CreateDirectory(OnlineDirectory);
            WriteAllTextAtomic(YearFilePath(year), rawJson);

            var cacheState = LoadState();
            state.LastSuccessUtc ??= state.LastCheckUtc;
            state.ConsecutiveFailures = 0;
            state.NextRetryUtc = null;
            cacheState.Years[year] = state;
            SaveState(cacheState);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            _logger?.LogWarning(exception, "写入 {Year} 年节假日缓存失败", year);
            return false;
        }
    }

    // ── 状态 ────────────────────────────────────────────────────────────────

    /// <summary>读状态文件；不存在或坏了返回空状态（不抛）。</summary>
    public HolidayCacheState LoadState()
    {
        try
        {
            if (!File.Exists(StateFilePath))
                return new HolidayCacheState();

            var json = File.ReadAllText(StateFilePath, Encoding.UTF8);
            var state = JsonSerializer.Deserialize<HolidayCacheState>(json, ReadOptions);
            if (state is null)
                return new HolidayCacheState();

            state.Years ??= [];
            return state;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or JsonException or NotSupportedException)
        {
            _logger?.LogWarning(exception, "读取节假日缓存状态失败，按空状态处理");
            return new HolidayCacheState();
        }
    }

    /// <summary>写状态文件（原子替换）。</summary>
    public bool SaveState(HolidayCacheState state)
    {
        try
        {
            Directory.CreateDirectory(OnlineDirectory);
            var json = JsonSerializer.Serialize(state, WriteOptions);
            WriteAllTextAtomic(StateFilePath, json);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            _logger?.LogWarning(exception, "写入节假日缓存状态失败");
            return false;
        }
    }

    // ── 用户覆盖 ────────────────────────────────────────────────────────────

    /// <summary>读用户覆盖；不存在或坏了返回空覆盖（不抛）。</summary>
    public HolidayOverrides LoadOverrides()
    {
        try
        {
            if (!File.Exists(OverridesFilePath))
                return new HolidayOverrides();

            var json = File.ReadAllText(OverridesFilePath, Encoding.UTF8);
            var overrides = JsonSerializer.Deserialize<HolidayOverrides>(json, ReadOptions);
            if (overrides is null)
                return new HolidayOverrides();

            overrides.Days ??= [];
            overrides.Vacations ??= [];
            overrides.Normalize();
            return overrides;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or JsonException or NotSupportedException)
        {
            _logger?.LogWarning(exception, "读取节假日用户覆盖失败，按空覆盖处理");
            return new HolidayOverrides();
        }
    }

    /// <summary>写用户覆盖（原子替换）；写入前先 <see cref="HolidayOverrides.Normalize" />。</summary>
    public bool SaveOverrides(HolidayOverrides overrides)
    {
        try
        {
            overrides.Normalize();
            Directory.CreateDirectory(Root);
            var json = JsonSerializer.Serialize(overrides, WriteOptions);
            WriteAllTextAtomic(OverridesFilePath, json);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            _logger?.LogWarning(exception, "写入节假日用户覆盖失败");
            return false;
        }
    }

    /// <summary>临时文件 + <see cref="File.Move(string, string, bool)" /> 的原子替换。</summary>
    private static void WriteAllTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (IOException)
            {
                // 清理失败无关紧要，下一次写入会覆盖它。
            }

            throw;
        }
    }
}
