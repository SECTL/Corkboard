using System.Text.Json;
using Avalonia.Media;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models;

namespace Corkboard.Core.Tests;

/// <summary>
///     配置序列化契约：磁盘上是 snake_case、颜色是十六进制字符串、旧版平铺的 basic 段要能被迁移。
///     这几条一旦破了，老用户的配置文件就会静默回退成默认值。
/// </summary>
public class ConfigSerializationTests
{
    [Fact]
    public void MainConfig_RoundTripsWithSnakeCasePropertyNames()
    {
        var config = new MainConfigModel();
        config.Basic.Language = LanguageMode.English;
        config.Basic.MainWindowWidth = 1440;
        config.Appearance.Theme = ThemeMode.Dark;
        config.Appearance.ThemeColor = Color.Parse("#FF112233");

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);

        Assert.Contains("\"main_window_width\": 1440", json);
        // 枚举按上游约定写成数字（JsonOptions 里没有字符串枚举转换器），改枚举顺序等价于改配置格式。
        Assert.Contains("\"language\": 1", json);

        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)!;
        Assert.Equal(LanguageMode.English, restored.Basic.Language);
        Assert.Equal(1440, restored.Basic.MainWindowWidth);
        Assert.Equal(ThemeMode.Dark, restored.Appearance.Theme);
        // 颜色的序列化形态是 #RRGGBBAA（alpha 在尾部），只要求往返一致。
        Assert.Equal(config.Appearance.ThemeColor, restored.Appearance.ThemeColor);
    }

    [Fact]
    public void MainConfig_DeserializesLegacyBasicSectionIntoGeneral()
    {
        const string legacy = """
        {
          "basic": { "language": 2, "show_startup_window": false },
          "appearance": { "theme": 2 }
        }
        """;

        var restored = JsonSerializer.Deserialize<MainConfigModel>(legacy, ConfigServiceBase.JsonOptions)!;

        Assert.Equal(LanguageMode.Japanese, restored.Basic.Language);
        Assert.False(restored.Basic.ShowStartupWindow);
        Assert.Equal(ThemeMode.Dark, restored.Appearance.Theme);
    }

    [Fact]
    public void ColorJsonConverter_AcceptsLegacyObjectForm()
    {
        const string legacy = """{ "appearance": { "theme_color": { "A": 255, "R": 1, "G": 2, "B": 3 } } }""";

        var restored = JsonSerializer.Deserialize<MainConfigModel>(legacy, ConfigServiceBase.JsonOptions)!;

        Assert.Equal(new Color(255, 1, 2, 3), restored.Appearance.ThemeColor);
    }

    [Fact]
    public void GlobalConstants_VersionIsVPrefixed()
    {
        Assert.StartsWith("v", GlobalConstants.Version);
        Assert.False(string.IsNullOrWhiteSpace(GlobalConstants.DisplayVersion));
    }

    [Fact]
    public void ConfigFilePath_PointsIntoConfigDirectory()
    {
        var path = new MainConfigModel().ConfigFilePath;

        Assert.EndsWith(Path.Combine("config", "settings.json"), path);
    }
}
