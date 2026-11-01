using System.Text.Json;
using Corkboard.Core;
using Corkboard.Core.Services.Stats;

namespace Corkboard.Core.Tests;

/// <summary>
///     聚合上报（<c>POST /api/stats/report</c>）的契约单测：一条事件写哪些键、服务端会把它判成哪一类、
///     批量请求体的形状、以及 <c>207</c> 批量响应怎么逐条读成败。
///     这些是服务端契约的一部分，改字段名/改类型先看这里。
/// </summary>
public class StatsReportEventTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CreateOnline_UsesApiFieldNamesAndNormalizes()
    {
        var report = StatsReportEvent.CreateOnline(
            "6ac3d35400241eff9fbe",
            "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE",
            "Windows-Desktop");

        Assert.Equal("6ac3d35400241eff9fbe", report.PlatformId);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", report.DeviceUuid);
        Assert.Equal("windows-desktop", report.DeviceType);

        var json = JsonSerializer.Serialize(report, WebOptions);
        Assert.Contains("\"platform_id\":", json, StringComparison.Ordinal);
        Assert.Contains("\"device_uuid\":", json, StringComparison.Ordinal);
        Assert.Contains("\"device_type\":", json, StringComparison.Ordinal);
        // 在线心跳不能带版本 / 用法容器，否则会被判成别的类型。
        Assert.DoesNotContain("\"version\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"metrics\"", json, StringComparison.Ordinal);
        Assert.Equal(StatsReportKind.Online, report.ResolveKind());
    }

    [Fact]
    public void CreateOnline_RejectsMissingPlatformAndBadDeviceId()
    {
        Assert.Throws<ArgumentException>(() =>
            StatsReportEvent.CreateOnline(" ", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        Assert.Throws<ArgumentException>(() =>
            StatsReportEvent.CreateOnline("platform", "not-a-uuid"));
        Assert.Throws<ArgumentException>(() =>
            StatsReportEvent.CreateOnline("platform", null));
    }

    [Fact]
    public void CreateVersion_UsesApiFieldNames()
    {
        var device = Guid.NewGuid();
        var report = StatsReportEvent.CreateVersion(GlobalConstants.PlatformId, "v0.1.0", device.ToString("D"));

        var json = JsonSerializer.Serialize(report, WebOptions);

        Assert.Contains("\"platform_id\":", json);
        Assert.Contains("\"version\":\"v0.1.0\"", json);
        Assert.Contains($"\"device_uuid\":\"{device:D}\"", json);
        // 版本上报只认 version，不能再带 metrics（会先命中用法那一档）。
        Assert.DoesNotContain("\"metrics\"", json, StringComparison.Ordinal);
        Assert.Equal(StatsReportKind.Version, report.ResolveKind());
    }

    [Fact]
    public void CreateVersion_NormalizesDeviceIdToLowerCase()
    {
        var device = Guid.NewGuid();

        var report = StatsReportEvent.CreateVersion(
            GlobalConstants.PlatformId, "v0.1.0", device.ToString("D").ToUpperInvariant());

        Assert.Equal(device.ToString("D").ToLowerInvariant(), report.DeviceUuid);
    }

    [Fact]
    public void CreateVersion_KeepsVersionPrefixAndSeparators()
    {
        var report = StatsReportEvent.CreateVersion(
            GlobalConstants.PlatformId, "v1.2.3-beta+build(7)", Guid.NewGuid().ToString("D"));

        Assert.Equal("v1.2.3-beta+build(7)", report.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0.1.0;drop")]
    [InlineData("v 1/2")]
    public void CreateVersion_WithUnsupportedVersion_Throws(string? version)
    {
        Assert.Throws<ArgumentException>(() =>
            StatsReportEvent.CreateVersion(GlobalConstants.PlatformId, version!, Guid.NewGuid().ToString("D")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    public void CreateVersion_WithUnsupportedDeviceId_Throws(string? deviceUuid)
    {
        Assert.Throws<ArgumentException>(() =>
            StatsReportEvent.CreateVersion(GlobalConstants.PlatformId, "v0.1.0", deviceUuid));
    }

    [Fact]
    public void CreateVersion_WithBlankPlatformId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            StatsReportEvent.CreateVersion("  ", "v0.1.0", Guid.NewGuid().ToString("D")));
    }

    [Fact]
    public void CreateUsage_UsesMetricsContainerAndValidates()
    {
        var report = StatsReportEvent.CreateUsage("platform_x", UsageMetricNames.NoteAdd, 3);

        var json = JsonSerializer.Serialize(report, WebOptions);
        Assert.Contains("\"platform_id\":\"platform_x\"", json, StringComparison.Ordinal);
        Assert.Contains("\"metrics\":{\"daily_note_add_count\":3}", json, StringComparison.Ordinal);
        // 旧端点的单条形态（field_key / delta）不能再出现。
        Assert.DoesNotContain("\"field_key\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"delta\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"version\"", json, StringComparison.Ordinal);
        Assert.Equal(StatsReportKind.Usage, report.ResolveKind());

        Assert.Throws<ArgumentException>(() => StatsReportEvent.CreateUsage(" ", UsageMetricNames.Export, 1));
        Assert.Throws<ArgumentException>(() => StatsReportEvent.CreateUsage("platform", " ", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatsReportEvent.CreateUsage("platform", UsageMetricNames.Export, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatsReportEvent.CreateUsage("platform", UsageMetricNames.Export, -1));
        // 服务端收 1..10000，超了整条被拒。
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StatsReportEvent.CreateUsage("platform", UsageMetricNames.Export, StatsReportEvent.MaxDelta + 1));
    }

    [Fact]
    public void ResolveKind_MirrorsServerFieldPriority()
    {
        // 一条事件只能被判成一种类型，判定只看键在不在（服务端也是这么做的）。
        Assert.Equal(
            StatsReportKind.Version,
            StatsReportEvent.CreateVersion("p", "v1.0.0", Guid.NewGuid().ToString("D")).ResolveKind());
        Assert.Equal(
            StatsReportKind.Usage,
            StatsReportEvent.CreateUsage("p", UsageMetricNames.AppLaunch, 1).ResolveKind());
        Assert.Equal(
            StatsReportKind.Online,
            StatsReportEvent.CreateOnline("p", Guid.NewGuid().ToString("D")).ResolveKind());

        // 只有 platform_id 时服务端按兜底算在线 —— 但设备标识是必填，这种事件会被 400 拒掉。
        Assert.Equal(StatsReportKind.Online, new StatsReportEvent("p").ResolveKind());

        // 连平台标识都没有：判不出来，服务端回 400 invalid_request。
        Assert.Equal(StatsReportKind.Unknown, new StatsReportEvent(string.Empty).ResolveKind());
    }

    [Fact]
    public void Batch_WrapsEventsInEventsArray()
    {
        var batch = StatsReportBatch.Create([
            StatsReportEvent.CreateOnline("platform_x", Guid.NewGuid().ToString("D")),
            StatsReportEvent.CreateVersion("platform_x", "v1.0.0", Guid.NewGuid().ToString("D")),
            StatsReportEvent.CreateUsage("platform_x", UsageMetricNames.AppLaunch, 2)
        ]);

        var json = JsonSerializer.Serialize(batch, WebOptions);

        Assert.StartsWith("{\"events\":[", json, StringComparison.Ordinal);
        Assert.Equal(3, batch.Events.Count);
    }

    [Fact]
    public void Batch_RejectsEmptyEvents()
    {
        // 服务端对空数组回 400 events must be a non-empty array，本地先挡住。
        Assert.Throws<ArgumentException>(() => StatsReportBatch.Create([]));
    }

    [Fact]
    public void BatchResponse_ReadsPerEventOutcomesFrom207()
    {
        const string json = """
                            {
                              "success": false,
                              "events": 3,
                              "matched": ["online", "version", "usage"],
                              "results": [
                                { "matched": "online", "status": 200, "body": { "success": true, "online_count": 42 } },
                                { "matched": "version", "status": 200, "body": { "success": true } },
                                { "matched": "usage", "status": 404, "body": { "error": "not_found" } }
                              ]
                            }
                            """;

        var parsed = JsonSerializer.Deserialize<StatsReportResponse>(json, WebOptions);

        Assert.NotNull(parsed);
        Assert.Equal(3, parsed.EventCount);
        Assert.NotNull(parsed.Results);
        var results = parsed.Results!;
        Assert.Equal(3, results.Count);

        // 批量最外层是 207 / success:false，但前两条其实成功了 —— 只能逐条看 status。
        Assert.True(results[0].IsAccepted);
        Assert.Equal(StatsReportKind.Online, results[0].MatchedKind);
        Assert.Equal(42, results[0].ResolveOnlineCount());

        Assert.True(results[1].IsAccepted);
        Assert.Equal(StatsReportKind.Version, results[1].MatchedKind);
        Assert.Null(results[1].ResolveOnlineCount());

        Assert.False(results[2].IsAccepted);
        Assert.Equal(StatsReportKind.Usage, results[2].MatchedKind);
    }

    [Fact]
    public void SingleResponse_ExposesTopLevelOnlineCount()
    {
        var parsed = JsonSerializer.Deserialize<StatsReportResponse>(
            """{"success":true,"matched":"online","online_count":7}""", WebOptions);

        Assert.NotNull(parsed);
        Assert.True(parsed.Success);
        Assert.Null(parsed.Results);
        Assert.Equal(7, parsed.OnlineCount);
        Assert.False(parsed.IsRateLimited);
    }

    [Fact]
    public void Response_RecognizesRateLimit()
    {
        var parsed = JsonSerializer.Deserialize<StatsReportResponse>(
            """{"error":"rate_limited","error_description":"Too many requests. Please try again later.","retry_after_ms":60000}""",
            WebOptions);

        Assert.NotNull(parsed);
        Assert.True(parsed.IsRateLimited);
        Assert.Equal(60000, parsed.RetryAfterMs);
        Assert.Null(parsed.Results);
    }

    [Fact]
    public void PlatformIdIsTheSectPlatformLoginId()
    {
        // SECTL 平台记录的主键同时用于 OAuth 与统计（在线状态、使用量、版本上报）。
        // 改这个值要同步改服务端；App 层的 SectlAuthEndpoints.ClientId 直接取这里，别写第二份。
        Assert.Equal("6ac3d35400241eff9fbe", GlobalConstants.PlatformId);
    }
}
