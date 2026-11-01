using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Corkboard.Core.Attributes;
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
          "window_fixed": false
        }
      },
      "appearance": { "theme": 2 },
      "board_settings": { "sort_mode": 1 },
      "cloud": { "enabled": false, "last_synced_at": "2026-01-01T00:00:00+08:00" }
    }
    """;

    /// <summary>
    ///     排除清单是<b>算出来的</b>（<see cref="LocalOnlyPaths" /> 反射收 <c>[LocalOnly]</c>），不是手写的。
    ///     这份断言把当前结果钉住：少贴一个特性 = 那项会被传到云端、恢复时还会覆盖本机；
    ///     多贴一个 = 用户以为会同步的设置其实没同步。真要对不上，先看配置类上的特性，
    ///     确认是有意改了再动这份清单。
    /// </summary>
    [Fact]
    public void Prefixes_MatchConfigurationAttributes()
    {
        string[] expected =
        [
            "board_auto_save.folder",
            "cloud",
            "general.basic.autostart",
            "general.basic.main_window_height",
            "general.basic.main_window_maximized",
            "general.basic.main_window_maximized_height",
            "general.basic.main_window_maximized_width",
            "general.basic.main_window_opacity",
            "general.basic.main_window_position_x",
            "general.basic.main_window_position_y",
            "general.basic.main_window_rounded_corners",
            "general.basic.main_window_screen_height",
            "general.basic.main_window_screen_width",
            "general.basic.main_window_width",
            "general.basic.settings_window_height",
            "general.basic.settings_window_maximized",
            "general.basic.settings_window_maximized_height",
            "general.basic.settings_window_maximized_width",
            "general.basic.settings_window_width",
            "general.basic.window_fixed"
        ];

        Assert.Equal(expected, CloudSyncExclusions.Prefixes);
    }

    /// <summary>走反射的那几个边角：类级标记、<c>[JsonIgnore]</c> 转发属性、<c>[JsonPropertyName]</c> 改名。</summary>
    [Fact]
    public void Build_HandlesClassLevelTargetsAndKeyOverrides()
    {
        string[] expected = ["autostart", "nested.path", "raw_name.path", "whole_segment"];

        Assert.Equal(expected, LocalOnlyPaths.Build(typeof(SampleRoot)));
    }

    private sealed class SampleRoot
    {
        [LocalOnly] public bool Autostart { get; set; }

        public string Language { get; set; } = string.Empty;

        /// <summary>转发属性不落盘，不该收出 <c>forward.path</c>。</summary>
        [JsonIgnore]
        public SampleLeaf Forward { get; set; } = new();

        /// <summary>显式键名要盖过 snake_case。</summary>
        [JsonPropertyName("raw_name")]
        public SampleLeaf Renamed { get; set; } = new();

        public SampleLeaf Nested { get; set; } = new();

        [LocalOnly] public SampleLeaf WholeSegment { get; set; } = new();
    }

    private sealed class SampleLeaf
    {
        [LocalOnly] public string Path { get; set; } = string.Empty;
    }

    /// <summary>开机自启、窗口尺寸位置、窗口固定状态、自动保存目录、云同步自己的开关都不参与同步。</summary>
    [Theory]
    [InlineData("general.basic.autostart")]
    [InlineData("general.basic.main_window_width")]
    [InlineData("general.basic.main_window_position_x")]
    [InlineData("general.basic.main_window_opacity")]
    [InlineData("general.basic.window_fixed")]
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
    [InlineData("general.basic.show_corner_assign_button")]
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
        Assert.False(basic.ContainsKey("window_fixed"));
        Assert.Equal(1, basic["language"]!.GetValue<int>());
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
        Assert.False(basic["window_fixed"]!.GetValue<bool>());
        Assert.Equal(7, merged["future_section"]!["value"]!.GetValue<int>());
    }
}
