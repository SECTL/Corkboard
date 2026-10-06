using System.Text.Json;
using Corkboard.Core;
using Corkboard.Core.Services.Stats;

namespace Corkboard.Core.Tests;

/// <summary>
///     版本上报载荷是纯函数，规则直接对齐 SECTL API：本地先挡掉会被服务端拒绝的版本号与设备标识，
///     否则每次启动都会白白换来一次 <c>invalid_request</c>。
/// </summary>
public class VersionUsageReportPayloadTests
{
    [Fact]
    public void Create_UsesApiFieldNames()
    {
        var device = Guid.NewGuid();
        var payload = VersionUsageReportPayload.Create(GlobalConstants.PlatformId, "v0.1.0", device.ToString("D"));

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"platform_id\":", json);
        Assert.Contains("\"version\":\"v0.1.0\"", json);
        Assert.Contains($"\"device_uuid\":\"{device:D}\"", json);
    }

    [Fact]
    public void Create_NormalizesDeviceIdToLowerCase()
    {
        var device = Guid.NewGuid();

        var payload = VersionUsageReportPayload.Create(GlobalConstants.PlatformId, "v0.1.0", device.ToString("D").ToUpperInvariant());

        Assert.Equal(device.ToString("D").ToLowerInvariant(), payload.DeviceUuid);
    }

    [Fact]
    public void Create_KeepsVersionPrefixAndSeparators()
    {
        var payload = VersionUsageReportPayload.Create(
            GlobalConstants.PlatformId, "v1.2.3-beta+build(7)", Guid.NewGuid().ToString("D"));

        Assert.Equal("v1.2.3-beta+build(7)", payload.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0.1.0;drop")]
    [InlineData("v 1/2")]
    public void Create_WithUnsupportedVersion_Throws(string? version)
    {
        Assert.Throws<ArgumentException>(() =>
            VersionUsageReportPayload.Create(GlobalConstants.PlatformId, version!, Guid.NewGuid().ToString("D")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    public void Create_WithUnsupportedDeviceId_Throws(string? deviceUuid)
    {
        Assert.Throws<ArgumentException>(() =>
            VersionUsageReportPayload.Create(GlobalConstants.PlatformId, "v0.1.0", deviceUuid));
    }

    [Fact]
    public void Create_WithBlankPlatformId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            VersionUsageReportPayload.Create("  ", "v0.1.0", Guid.NewGuid().ToString("D")));
    }

    [Fact]
    public void PlatformIdAndOAuthClientId_AreDistinct()
    {
        // 上游两者同值，本项目是两个不同的值：混用会让统计维度与 OAuth 客户端互相污染。
        Assert.StartsWith("platform_", GlobalConstants.PlatformId);
        Assert.NotEqual(GlobalConstants.PlatformId, "6ac3d35400241eff9fbe");
    }
}
