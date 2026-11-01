using System.Text.Json;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Services.Stats;

namespace Corkboard.Core.Tests;

/// <summary>
///     SECTL 平台统计的纯逻辑单测：在线人数查询响应、隐私三态、待上报增量的取用与放回。
///     事件形状（聚合上报的请求体）与批量响应解析见 <see cref="StatsReportEventTests" />。
/// </summary>
public class OnlineStatsTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void StatsReportEvent_DetectsDeviceTypeFromCurrentRuntime()
    {
        var deviceType = StatsReportEvent.DetectDeviceType();

        // 服务端按词表校验，带架构的（windows-x64）会被 400 拒掉。
        Assert.Contains(
            deviceType,
            new[] { "windows-desktop", "macos-desktop", "linux-desktop", "android", "ios", "unknown-desktop" });
    }

    [Fact]
    public void OnlineStatsResult_ResolvesNonNegativeCount()
    {
        // GET /api/stats/platform/{id} 把人数放在 online.online_count 里。
        var platform = JsonSerializer.Deserialize<OnlineStatsResult>(
            """{"success":true,"platform_id":"6ac3d35400241eff9fbe","online":{"online_count":78}}""",
            WebOptions);
        Assert.Equal(78, platform!.ResolveOnlineCount());

        // 顶层形态也认（同一份响应在别处的写法）。
        var flat = JsonSerializer.Deserialize<OnlineStatsResult>(
            """{"success":true,"online_count":12,"error":null}""", WebOptions);
        Assert.NotNull(flat);
        Assert.True(flat.Success);
        Assert.Equal(12, flat.ResolveOnlineCount());

        var negative = JsonSerializer.Deserialize<OnlineStatsResult>(
            """{"success":true,"online_count":-3}""", WebOptions);
        Assert.Equal(0, negative!.ResolveOnlineCount());

        var missing = JsonSerializer.Deserialize<OnlineStatsResult>("""{"success":false}""", WebOptions);
        Assert.Null(missing!.ResolveOnlineCount());
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
