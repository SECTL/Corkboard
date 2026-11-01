using Corkboard.Core.Widgets.Weather;

namespace Corkboard.Core.Tests;

/// <summary>
///     「天气现象」到图标文件名的映射，以及「这些图标真的在仓库里」。
///     <para>
///         映射错了不会崩，只会画错图标（下雪画成下雨）；图标漏打包更隐蔽——运行时
///         <c>AssetLoader</c> 取不到就静默不画，屏幕上只剩温度数字。所以这里除了映射表，
///         还按映射结果去磁盘上核对一遍文件名。
///     </para>
/// </summary>
public class WeatherIconCatalogTests
{
    [Fact]
    public void Clear_DiffersBetweenDayAndNight()
    {
        Assert.Equal("clear-day", WeatherIconCatalog.FileName(WeatherCondition.Clear, isDaytime: true));
        Assert.Equal("clear-night", WeatherIconCatalog.FileName(WeatherCondition.Clear, isDaytime: false));
    }

    [Fact]
    public void ConditionsWithoutASunOrMoon_IgnoreDaylight()
    {
        foreach (var condition in new[]
                 {
                     WeatherCondition.Cloudy, WeatherCondition.Overcast, WeatherCondition.ModerateRain,
                     WeatherCondition.HeavyRain, WeatherCondition.Rainstorm, WeatherCondition.ThunderHail,
                     WeatherCondition.Sleet, WeatherCondition.FreezingRain, WeatherCondition.Blizzard
                 })
        {
            Assert.Equal(
                WeatherIconCatalog.FileName(condition, isDaytime: true),
                WeatherIconCatalog.FileName(condition, isDaytime: false));
        }
    }

    [Fact]
    public void Unknown_FallsBackToACloud()
    {
        Assert.Equal(WeatherIconCatalog.Fallback, WeatherIconCatalog.FileName(WeatherCondition.Unknown, true));
        Assert.Equal(WeatherIconCatalog.Fallback, WeatherIconCatalog.FileName((WeatherCondition)999, false));
    }

    [Fact]
    public void EveryCondition_MapsToAnIconThatShipsWithTheApp()
    {
        var folder = ResolveWeatherAssetFolder();
        if (folder is null)
            return; // 打包 / 单工程构建时找不到仓库根，跳过（不误报失败）。

        foreach (var condition in Enum.GetValues<WeatherCondition>())
        {
            foreach (var daytime in new[] { true, false })
            {
                var name = WeatherIconCatalog.FileName(condition, daytime);
                var path = Path.Combine(folder, name + ".png");

                Assert.True(File.Exists(path), $"缺少图标：Assets/Weather/{name}.png（{condition} / 白天={daytime}）");
            }
        }
    }

    /// <summary>从测试程序集往上找仓库根，再进 <c>Corkboard/Assets/Weather</c>。</summary>
    private static string? ResolveWeatherAssetFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Corkboard.sln")))
                return Path.Combine(directory.FullName, "Corkboard", "Assets", "Weather");

            directory = directory.Parent;
        }

        return null;
    }
}
