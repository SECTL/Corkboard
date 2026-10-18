using System.Text.Json;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Services.Stats;

namespace Corkboard.Core.Tests;

/// <summary>
///     SECTL 平台统计（在线状态 / 使用量）的纯逻辑单测：请求载荷的字段名与校验、
///     隐私三态的规则、待上报增量的取用与放回。这些是服务端契约的一部分，改名/改类型先看这里。
/// </summary>
public class OnlineStatsTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void OnlineStatusPayload_UsesApiFieldNamesAndNormalizes()
    {
        var payload = OnlineStatusPayload.Create(
            "6ac3d35400241eff9fbe",
            "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE",
            "Windows-Desktop");

        Assert.Equal("6ac3d35400241eff9fbe", payload.PlatformId);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", payload.DeviceUuid);
        Assert.Equal("windows-desktop", payload.DeviceType);

        var json = JsonSerializer.Serialize(payload, WebOptions);
        Assert.Contains("\"platform_id\":", json, StringComparison.Ordinal);
        Assert.Contains("\"device_uuid\":", json, StringComparison.Ordinal);
        Assert.Contains("\"device_type\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlineStatusPayload_DetectsDeviceTypeFromCurrentRuntime()
    {
        var deviceType = OnlineStatusPayload.DetectDeviceType();

        // 服务端按词表校验，带架构的（windows-x64）会被 400 拒掉。
        Assert.Contains(
            deviceType,
            new[] { "windows-desktop", "macos-desktop", "linux-desktop", "android", "ios", "unknown-desktop" });
    }

    [Fact]
    public void OnlineStatusPayload_RejectsMissingPlatformAndBadDeviceId()
    {
        Assert.Throws<ArgumentException>(() =>
            OnlineStatusPayload.Create(" ", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        Assert.Throws<ArgumentException>(() =>
            OnlineStatusPayload.Create("platform", "not-a-uuid"));
        Assert.Throws<ArgumentException>(() =>
            OnlineStatusPayload.Create("platform", null));
    }

    [Fact]
    public void OnlineStatsResult_ResolvesNonNegativeCount()
    {
        var parsed = JsonSerializer.Deserialize<OnlineStatsResult>(
            """{"success":true,"online_count":12,"error":null}""", WebOptions);

        Assert.NotNull(parsed);
        Assert.True(parsed.Success);
        Assert.Equal(12, parsed.ResolveOnlineCount());

        var negative = JsonSerializer.Deserialize<OnlineStatsResult>(
            """{"success":true,"online_count":-3}""", WebOptions);
        Assert.Equal(0, negative!.ResolveOnlineCount());

        var missing = JsonSerializer.Deserialize<OnlineStatsResult>("""{"success":false}""", WebOptions);
        Assert.Null(missing!.ResolveOnlineCount());

        // GET /api/stats/platform/{id} 把人数放在 online.online_count 里，也要能读出来。
        var platform = JsonSerializer.Deserialize<OnlineStatsResult>(
            """{"success":true,"platform_id":"6ac3d35400241eff9fbe","online":{"online_count":78}}""",
            WebOptions);
        Assert.Equal(78, platform!.ResolveOnlineCount());
    }

    [Theory]
    [InlineData(OnlineStatusMode.Full, true)]
    [InlineData(OnlineStatusMode.Anonymous, true)]
    [InlineData(OnlineStatusMode.Off, false)]
    public void OnlineStatusPolicy_OffStopsReporting(OnlineStatusMode mode, bool expected)
    {
        Assert.Equal(expected, OnlineStatusPolicy.ShouldReport(mode));
    }

    [Fact]
    public void OnlineStatusPolicy_NormalizesUnknownModeToFull()
    {
        Assert.Equal(OnlineStatusMode.Full, OnlineStatusPolicy.Normalize((OnlineStatusMode)42));
        Assert.Equal(OnlineStatusMode.Off, OnlineStatusPolicy.Normalize(OnlineStatusMode.Off));
    }

    [Fact]
    public void PrivacySettings_DefaultsToFullAndNormalizesUnknownValue()
    {
        var settings = new PrivacySettingsConfig();
        Assert.Equal(OnlineStatusMode.Full, settings.OnlineStatusMode);

        settings.OnlineStatusMode = (OnlineStatusMode)42;
        settings.Normalize();
        Assert.Equal(OnlineStatusMode.Full, settings.OnlineStatusMode);
    }

    [Fact]
    public void UsageIncrementPayload_UsesApiFieldNamesAndValidates()
    {
        var payload = UsageIncrementPayload.Create("platform_x", UsageMetricNames.NoteAdd, 3);

        var json = JsonSerializer.Serialize(payload, WebOptions);
        Assert.Contains("\"platform_id\":\"platform_x\"", json, StringComparison.Ordinal);
        Assert.Contains("\"field_key\":\"daily_note_add_count\"", json, StringComparison.Ordinal);
        Assert.Contains("\"delta\":3", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"field\":", json, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => UsageIncrementPayload.Create(" ", "export", 1));
        Assert.Throws<ArgumentException>(() => UsageIncrementPayload.Create("platform", " ", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => UsageIncrementPayload.Create("platform", "export", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => UsageIncrementPayload.Create("platform", "export", -1));
    }

    [Fact]
    public void UsageMetricNames_AreTheServerContract()
    {
        Assert.Equal("daily_app_launch_count", UsageMetricNames.AppLaunch);
        Assert.Equal("daily_note_add_count", UsageMetricNames.NoteAdd);
        Assert.Equal("daily_export_count", UsageMetricNames.Export);
    }

    [Fact]
    public void UsageMetricCounter_TakeClearsAndRestoreMergesWithNewIncrements()
    {
        var counter = new UsageMetricCounter();
        counter.Add(UsageMetricNames.AppLaunch);
        counter.Add(UsageMetricNames.AppLaunch);
        counter.Add(UsageMetricNames.Export, 2);

        var taken = counter.TakePending();
        Assert.Equal(2, taken[UsageMetricNames.AppLaunch]);
        Assert.Equal(2, taken[UsageMetricNames.Export]);
        Assert.False(counter.HasPending);

        // 上报期间又记了一笔，失败放回时不能覆盖它，只能相加。
        counter.Add(UsageMetricNames.AppLaunch);
        counter.Restore(taken);
        Assert.Equal(3, counter.Snapshot()[UsageMetricNames.AppLaunch]);
        Assert.Equal(2, counter.Snapshot()[UsageMetricNames.Export]);
    }

    [Fact]
    public void UsageMetricCounter_SnapshotDoesNotClearAndClearEmpties()
    {
        var counter = new UsageMetricCounter();
        counter.Add(UsageMetricNames.NoteAdd, 5);

        Assert.Equal(5, counter.Snapshot()[UsageMetricNames.NoteAdd]);
        Assert.True(counter.HasPending);

        counter.Clear();
        Assert.False(counter.HasPending);
    }
}
