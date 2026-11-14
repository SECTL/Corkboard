using Avalonia.Media;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam.Theme;
using Corkboard.Core.Models.SubConfigs.Exam;

namespace Corkboard.Core.Tests;

/// <summary>
///     主题解析这条线：预设必须覆盖 13 个 token、自定义只覆盖改过的项、坏配置归一后回默认，
///     以及「跟随应用外观」交给调用方的查色函数。
/// </summary>
public class ExamBoardThemeTests
{
    private static Color? FixedFollowApp(ExamBoardThemeToken token) => token.Id switch
    {
        "surface" => Color.Parse("#FF202020"),
        "text_primary" => Color.Parse("#FFFFFFFF"),
        _ => null
    };

    [Fact]
    public void EveryPresetCoversEveryToken()
    {
        foreach (var preset in ExamBoardThemePresets.All)
        foreach (var token in ExamBoardThemeTokens.All)
        {
            var color = preset.Get(token);
            Assert.True(color.A > 0 || token.Id == "highlight");
        }
    }

    [Fact]
    public void FollowApp_UsesCallbackAndFallsBackToDefaults()
    {
        var config = new ExamBoardThemeConfig { Source = ExamBoardThemeSource.FollowApp };
        var colors = ExamBoardThemeResolver.Resolve(config, FixedFollowApp);

        Assert.Equal(Color.Parse("#FF202020"), colors.Surface);
        Assert.Equal(Color.Parse("#FFFFFFFF"), colors.TextPrimary);
        Assert.Equal(ExamBoardThemeColors.FollowAppDefaults.Urgent, colors.Urgent);
    }

    [Fact]
    public void Preset_IgnoresPaletteAndFollowApp()
    {
        var config = new ExamBoardThemeConfig
        {
            Source = ExamBoardThemeSource.Preset,
            PresetId = "paper",
            Palette = new ExamBoardThemeOverrides { Surface = Colors.Red }
        };

        var colors = ExamBoardThemeResolver.Resolve(config, _ => Colors.Lime);
        var paper = ExamBoardThemePresets.Get("paper");

        Assert.Equal(paper.Get("surface"), colors.Surface);
        Assert.Equal(paper.Get("text_primary"), colors.TextPrimary);
    }

    [Fact]
    public void CustomOnFollowApp_OverridesOnlyGivenTokens()
    {
        var config = new ExamBoardThemeConfig
        {
            Source = ExamBoardThemeSource.Custom,
            CustomBase = ExamBoardThemeBase.FollowApp,
            Palette = new ExamBoardThemeOverrides { Surface = Colors.Red }
        };

        var colors = ExamBoardThemeResolver.Resolve(config, FixedFollowApp);

        Assert.Equal(Colors.Red, colors.Surface);
        Assert.Equal(Color.Parse("#FFFFFFFF"), colors.TextPrimary);
    }

    [Fact]
    public void CustomOnPreset_UsesPresetAsBase()
    {
        var config = new ExamBoardThemeConfig
        {
            Source = ExamBoardThemeSource.Custom,
            CustomBase = ExamBoardThemeBase.Preset,
            PresetId = "focus",
            Palette = new ExamBoardThemeOverrides { Urgent = Colors.Red }
        };

        var colors = ExamBoardThemeResolver.Resolve(config, _ => Colors.Lime);
        var focus = ExamBoardThemePresets.Get("focus");

        Assert.Equal(Colors.Red, colors.Urgent);
        Assert.Equal(focus.Get("surface"), colors.Surface);
    }

    [Fact]
    public void UnknownPreset_FallsBackToDefault()
    {
        var config = new ExamBoardThemeConfig
        {
            Source = ExamBoardThemeSource.Preset,
            PresetId = "does-not-exist"
        };

        var colors = ExamBoardThemeResolver.Resolve(config, _ => null);
        Assert.Equal(ExamBoardThemePresets.GetDefault().Get("surface"), colors.Surface);
    }

    [Fact]
    public void Normalize_FixesBadEnumsPresetAndTransparentOverrides()
    {
        var config = new ExamBoardThemeConfig
        {
            Source = (ExamBoardThemeSource)99,
            CustomBase = (ExamBoardThemeBase)99,
            PresetId = "nope",
            Palette = new ExamBoardThemeOverrides
            {
                Surface = Color.Parse("#00000000"),
                Highlight = Color.Parse("#00000000")
            }
        };

        config.Normalize();

        Assert.Equal(ExamBoardThemeSource.FollowApp, config.Source);
        Assert.Equal(ExamBoardThemeBase.FollowApp, config.CustomBase);
        Assert.Equal(ExamBoardThemePresets.DefaultId, config.PresetId);
        Assert.Null(config.Palette.Surface);
        // highlight 允许半透明/透明，不能当坏数据清掉。
        Assert.Equal(Color.Parse("#00000000"), config.Palette.Highlight);
    }
}