using System.Text.Json.Serialization;

namespace Corkboard.Core.Services.Stats;

/// <summary>
///     一次在线状态上报，字段名就是 SECTL 统计 API（<c>POST /api/stats/online</c>）要求的形态。
/// </summary>
/// <remarks>
///     位置由服务端按访问地址统计，客户端**不发** IP 或城市；也**绝不**带 SECTL 账号 ID，
///     在线人数无法与账号关联。校验与设备类型推断放这里（纯函数、可单测），
///     免得每次上报都被服务端以 <c>invalid_request</c> 拒绝一次。
/// </remarks>
public sealed record OnlineStatusPayload(
    [property: JsonPropertyName("platform_id")]
    string PlatformId,
    [property: JsonPropertyName("device_uuid")]
    string DeviceUuid,
    [property: JsonPropertyName("device_type")]
    string DeviceType)
{
    /// <summary>
    ///     为本次上报构造内容。设备标识是必需的：没有它服务端无法归并同一台设备，
    ///     这里也不提供退回账号 ID 的路径。
    /// </summary>
    /// <exception cref="ArgumentException">平台标识为空，或设备标识不是标准 UUID。</exception>
    public static OnlineStatusPayload Create(string platformId, string? deviceUuid, string? deviceType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);

        var device = Normalize(deviceUuid);
        if (device is null || !Guid.TryParse(device, out _))
            throw new ArgumentException($@"Unsupported device UUID: '{deviceUuid}'.", nameof(deviceUuid));

        var type = Normalize(deviceType) ?? DetectDeviceType();
        return new OnlineStatusPayload(platformId.Trim(), device.ToLowerInvariant(), type.ToLowerInvariant());
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

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
///     在线状态接口的响应：上报（<c>POST /api/stats/online</c>）是顶层
///     <c>success</c> / <c>online_count</c> / <c>error</c>；
///     在线人数查询（<c>GET /api/stats/platform/{id}</c>）把人数放在 <c>online.online_count</c> 里，
///     所以两种形态都要认。
/// </summary>
public sealed record OnlineStatsResult(
    [property: JsonPropertyName("success")]
    bool? Success,
    [property: JsonPropertyName("online_count")]
    int? OnlineCount,
    [property: JsonPropertyName("error")]
    string? Error,
    [property: JsonPropertyName("online")]
    OnlineStatsSnapshot? Online)
{
    /// <summary>把在线人数抬成非负值；没有可用数字时返回 <see langword="null" />。</summary>
    public int? ResolveOnlineCount()
    {
        var count = OnlineCount ?? Online?.OnlineCount;
        return count is { } value ? Math.Max(0, value) : null;
    }
}

/// <summary>平台统计响应里 <c>online</c> 那一段（<c>online_count</c> 等）。</summary>
public sealed record OnlineStatsSnapshot(
    [property: JsonPropertyName("online_count")]
    int? OnlineCount);
