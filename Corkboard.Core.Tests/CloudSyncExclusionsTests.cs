using System.Text.Json.Nodes;
using Corkboard.Core.Services.Cloud;

namespace Corkboard.Core.Tests;

/// <summary>
///     <see cref="CloudSyncExclusions" /> 的单测：本机专属项不上传、恢复时也不覆盖本机。
/// </summary>
public class CloudSyncExclusionsTests
{
    private const string LocalSettings = """
    {
      "general": {
        "basic": {
          "autostart": true,
          "language": 1,
          "main_window_width": 1200,
          "main_window_position_x": -1,
          "main_window_opacity": 0.8,
          "hold_to_drag_window": true
        }
      },
      "appearance": { "theme": 2 },
      "board_settings": { "sort_mode": 1 },
      "cloud": { "enabled": false, "last_synced_at": "2026-01-01T00:00:00+08:00" }
    }
    """;

    /// <summary>开机自启、窗口尺寸位置、自动保存目录、云同步自己的开关都不参与同步。</summary>
    [Theory]
    [InlineData("general.basic.autostart")]
    [InlineData("general.basic.main_window_width")]
    [InlineData("general.basic.main_window_position_x")]
    [InlineData("general.basic.main_window_opacity")]
    [InlineData("general.basic.settings_window_height")]
    [InlineData("board_auto_save.folder")]
    [InlineData("cloud")]
    [InlineData("cloud.enabled")]
    public void IsExcluded_RejectsMachineSpecificPaths(string path)
    {
        Assert.True(CloudSyncExclusions.IsExcluded(path));
    }

    /// <summary>别的设置照旧同步。</summary>
    [Theory]
    [InlineData("general.basic.language")]
    [InlineData("general.basic.hold_to_drag_window")]
    [InlineData("appearance.theme")]
    [InlineData("board_settings.sort_mode")]
    [InlineData("general.basic.show_startup_window")]
    // 自动保存的开关与时机跟着账号走（只有目录是本机专属的）。
    [InlineData("board_auto_save.enabled")]
    [InlineData("board_auto_save.mode")]
    public void IsExcluded_KeepsNormalPaths(string path)
    {
        Assert.False(CloudSyncExclusions.IsExcluded(path));
    }

    /// <summary>上传前的裁剪：本机专属项消失，其余原样保留。</summary>
    [Fact]
    public void StripForUpload_RemovesOnlyMachineSpecificItems()
    {
        var upload = JsonNode.Parse(CloudSyncExclusions.StripForUpload(LocalSettings))!.AsObject();

        var basic = upload["general"]!["basic"]!.AsObject();
        Assert.False(basic.ContainsKey("autostart"));
        Assert.False(basic.ContainsKey("main_window_width"));
        Assert.False(basic.ContainsKey("main_window_position_x"));
        Assert.Equal(1, basic["language"]!.GetValue<int>());
        Assert.True(basic["hold_to_drag_window"]!.GetValue<bool>());
        Assert.Equal(2, upload["appearance"]!["theme"]!.GetValue<int>());
        Assert.False(upload.ContainsKey("cloud"));
    }

    /// <summary>空对象与坏 JSON 不能让上传流程崩掉。</summary>
    [Fact]
    public void StripForUpload_ToleratesBrokenInput()
    {
        Assert.Equal("{}", CloudSyncExclusions.StripForUpload("{}"));
        Assert.NotNull(CloudSyncExclusions.StripForUpload("not json"));
    }

    /// <summary>从云端恢复：远端的设置覆盖本机，但本机专属项与未知项保持原样。</summary>
    [Fact]
    public void MergeRemoteOntoLocal_KeepsLocalMachineSpecificValues()
    {
        const string remote = """
        {
          "general": { "basic": { "language": 2, "autostart": false, "main_window_width": 800 } },
          "appearance": { "theme": 0 },
          "cloud": { "enabled": true },
          "future_section": { "value": 7 }
        }
        """;

        var merged = JsonNode.Parse(CloudSyncExclusions.MergeRemoteOntoLocal(LocalSettings, remote))!.AsObject();
        var basic = merged["general"]!["basic"]!.AsObject();

        Assert.Equal(2, basic["language"]!.GetValue<int>());
        Assert.Equal(0, merged["appearance"]!["theme"]!.GetValue<int>());
        // 本机专属项与云端的同名字段都保持本机值
        Assert.True(basic["autostart"]!.GetValue<bool>());
        Assert.Equal(1200, basic["main_window_width"]!.GetValue<int>());
        Assert.False(merged["cloud"]!["enabled"]!.GetValue<bool>());
        // 本机独有的项留着，远端多出来的项也带进来（新版本写的键不该丢）
        Assert.True(basic["hold_to_drag_window"]!.GetValue<bool>());
        Assert.Equal(7, merged["future_section"]!["value"]!.GetValue<int>());
    }
}
