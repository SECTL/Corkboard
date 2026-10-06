using System.Text.Json.Serialization;

namespace Corkboard.Core.Services.Stats;

/// <summary>
///     一次版本使用人数上报，字段名就是 SECTL 统计 API（<c>POST /api/stats/version</c>）要求的形态。
/// </summary>
/// <remarks>
///     该接口按「身份」去重后统计每个版本还有多少人：身份只允许是本机设备标识，
///     **绝不**带 SECTL 账号 ID，这样版本分布无法与账号关联。
///     版本号与设备标识的格式校验放在这里（纯函数、可单测），
///     免得每次启动都被服务端以 <c>invalid_request</c> 拒绝一次。
/// </remarks>
public sealed record VersionUsageReportPayload(
    [property: JsonPropertyName("platform_id")]
    string PlatformId,
    [property: JsonPropertyName("version")]
    string Version,
    [property: JsonPropertyName("device_uuid")]
    string DeviceUuid)
{
    private const int MaxVersionLength = 64;
    private const string VersionSeparators = "._+-() ";

    /// <summary>
    ///     为本次安装构造上报内容。设备标识是必需的：没有它服务端无法把这次上报归到一台设备，
    ///     而这里不提供任何退回账号 ID 的路径。
    /// </summary>
    /// <exception cref="ArgumentException">版本值或设备标识会被 API 拒绝。</exception>
    public static VersionUsageReportPayload Create(string platformId, string version, string? deviceUuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);
        if (!IsSupportedVersion(version))
            throw new ArgumentException($@"Unsupported version value: '{version}'.", nameof(version));

        var device = Normalize(deviceUuid);
        // 设备标识必须是标准 UUID，且统一成小写，同一台设备只对应一个身份字符串。
        if (device is null || !Guid.TryParse(device, out _))
            throw new ArgumentException($@"Unsupported device UUID: '{deviceUuid}'.", nameof(deviceUuid));

        return new VersionUsageReportPayload(platformId, version, device.ToLowerInvariant());
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
