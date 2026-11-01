using System.Text.Json.Serialization;

namespace Corkboard.Core.Services.Stats;

/// <summary>
///     聚合上报入口（<c>POST /api/stats/report</c>）里一条事件被服务端判定成的上报类型。
/// </summary>
/// <remarks>
///     服务端**只看键是否存在**、按「版本 → 用法容器 → 在线」的顺序命中即止，
///     所以「这条到底会被当成什么」必须由客户端自己也算一遍（<see cref="StatsReportEvent.ResolveKind" />），
///     写错字段不会报错，只会让数据静默落进错误的统计口径。
/// </remarks>
public enum StatsReportKind
{
    /// <summary>判不出来（服务端会回 <c>400 invalid_request</c>）。</summary>
    Unknown = 0,

    /// <summary>在线状态心跳：只剩 <c>platform_id</c> / <c>device_uuid</c> / <c>device_type</c>。</summary>
    Online = 1,

    /// <summary>版本使用人数：带 <c>version</c>。</summary>
    Version = 2,

    /// <summary>累计用量增量：带 <c>metrics</c> 用法容器。</summary>
    Usage = 3
}

/// <summary>
///     聚合上报（<c>POST /api/stats/report</c>）的**一条**事件。
/// </summary>
/// <remarks>
///     <para>
///         三类上报共用这一个形状：字段全为空的不写进 JSON（<see cref="JsonIgnoreCondition.WhenWritingNull" />），
///         于是「哪些键在」既决定了这条事件的类型，也和旧端点的请求体逐字段一致——
///         换端点不影响服务端解析，也不会让老统计口径断档。
///     </para>
///     <para>
///         位置由服务端按访问地址统计，客户端**不发** IP / 城市；也**绝不**带 SECTL 账号 ID，
///         在线人数与版本分布都无法与账号关联。
///     </para>
///     <para>
///         校验与设备类型推断放这里（纯函数、可单测），免得每次上报都被服务端以
///         <c>invalid_request</c> 拒绝一次。
///     </para>
/// </remarks>
public sealed record StatsReportEvent(
    [property: JsonPropertyName("platform_id")]
    string PlatformId,
    [property: JsonPropertyName("device_uuid")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DeviceUuid = null,
    [property: JsonPropertyName("device_type")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DeviceType = null,
    [property: JsonPropertyName("version")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Version = null,
    [property: JsonPropertyName("metrics")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, long>? Metrics = null)
{
    /// <summary>一次最多上报的增量（服务端对 <c>delta</c> 的上限），超了整条会被拒。</summary>
    public const int MaxDelta = 10_000;

    private const int MaxVersionLength = 64;
    private const string VersionSeparators = "._+-() ";

    /// <summary>
    ///     服务端会把这几个键判定成什么类型（复刻文档里「按顺序命中即止」那张表）。
    ///     <c>field_key</c> + <c>value</c> 那一档本应用不写，所以不参与判定。
    /// </summary>
    public StatsReportKind ResolveKind()
    {
        if (Version is not null)
            return StatsReportKind.Version;

        if (Metrics is not null)
            return StatsReportKind.Usage;

        // 服务端的兜底档：只剩 platform_id / device_uuid（/ device_type）时就是在线心跳。
        return PlatformId.Length > 0 ? StatsReportKind.Online : StatsReportKind.Unknown;
    }

    /// <summary>
    ///     构造一条在线状态心跳。设备标识是必需的：没有它服务端无法归并同一台设备，
    ///     这里也不提供退回账号 ID 的路径。
    /// </summary>
    /// <exception cref="ArgumentException">平台标识为空，或设备标识不是标准 UUID。</exception>
    public static StatsReportEvent CreateOnline(string platformId, string? deviceUuid, string? deviceType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);

        var device = Normalize(deviceUuid);
        if (device is null || !Guid.TryParse(device, out _))
            throw new ArgumentException($@"Unsupported device UUID: '{deviceUuid}'.", nameof(deviceUuid));

        var type = Normalize(deviceType) ?? DetectDeviceType();
        return new StatsReportEvent(
            platformId.Trim(),
            device.ToLowerInvariant(),
            type.ToLowerInvariant());
    }

    /// <summary>
    ///     构造一条版本使用人数事件。接口按软件安装去重统计「每个版本还有多少人在用」，
    ///     身份只允许是本机设备标识。
    /// </summary>
    /// <exception cref="ArgumentException">版本值或设备标识会被 API 拒绝。</exception>
    public static StatsReportEvent CreateVersion(string platformId, string version, string? deviceUuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);
        if (!IsSupportedVersion(version))
            throw new ArgumentException($@"Unsupported version value: '{version}'.", nameof(version));

        var device = Normalize(deviceUuid);
        // 设备标识必须是标准 UUID，且统一成小写，同一台设备只对应一个身份字符串。
        if (device is null || !Guid.TryParse(device, out _))
            throw new ArgumentException($@"Unsupported device UUID: '{deviceUuid}'.", nameof(deviceUuid));

        return new StatsReportEvent(platformId.Trim(), device.ToLowerInvariant(), Version: version);
    }

    /// <summary>
    ///     构造一条累计用量增量事件：<c>metrics</c> 是用法容器，一次可以带多项，
    ///     服务端按 <c>platform_id + field_key + 统计时间</c> 累加。
    /// </summary>
    /// <remarks>
    ///     ⚠️ 只发本次增量，**不要**发本地累计值；<c>period</c> 一律不传——档位由字段名前缀
    ///     （<c>daily_*</c>）兜底到服务端当天，传了反而多一处可能写错的地方。
    /// </remarks>
    /// <exception cref="ArgumentException">平台标识或指标名为空。</exception>
    /// <exception cref="ArgumentOutOfRangeException">增量不在 <c>1..<see cref="MaxDelta" /></c> 之间。</exception>
    public static StatsReportEvent CreateUsage(string platformId, string metric, long delta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metric);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(delta);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delta, MaxDelta);

        return new StatsReportEvent(
            platformId.Trim(),
            Metrics: new Dictionary<string, long>(StringComparer.Ordinal) { [metric.Trim()] = delta });
    }

    /// <summary>
    ///     推断设备类型（<c>windows-desktop</c>、<c>macos-desktop</c>、<c>linux-desktop</c>、
    ///     <c>android</c>、<c>ios</c> 这种）。
    /// </summary>
    /// <remarks>
    ///     ⚠️ 服务端按**词表**校验 <c>device_type</c>，写别的值（例如带架构的 <c>windows-x64</c>）
    ///     会直接回 <c>400</c>，在线状态整条上报不生效——这里的取值必须与上游 SecRandom 的
    ///     <c>OnlineStatusDeviceType</c> 一致。只作为统计维度，不是安全边界。
    /// </remarks>
    public static string DetectDeviceType()
    {
        if (OperatingSystem.IsAndroid())
            return "android";

        if (OperatingSystem.IsIOS())
            return "ios";

        if (OperatingSystem.IsWindows())
            return "windows-desktop";

        if (OperatingSystem.IsMacOS())
            return "macos-desktop";

        if (OperatingSystem.IsLinux())
            return "linux-desktop";

        return "unknown-desktop";
    }

    /// <summary>
    ///     复刻 API 的版本号规则：1–64 字符，首字符为字母或数字，其余允许字母、数字与
    ///     <c>. _ + - ( )</c> 和空格。
    /// </summary>
    public static bool IsSupportedVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > MaxVersionLength)
            return false;

        if (!char.IsAsciiLetterOrDigit(version[0]))
            return false;

        return version.All(character =>
            char.IsAsciiLetterOrDigit(character) || VersionSeparators.Contains(character, StringComparison.Ordinal));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
