using Avalonia.Media;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.Exam;

namespace Corkboard.Core.Exam.Theme;

/// <summary>
///     把主题配置解析成 13 个实际颜色。纯逻辑，不碰 UI，便于单测。
/// </summary>
public static class ExamBoardThemeResolver
{
    /// <param name="config">主题配置（来源 / 自定义底 / 预设 id / 覆盖）。</param>
    /// <param name="followAppColor">
    ///     「跟随应用外观」时按 token 查 Fluent 色；App 层用 Application.TryGetResource 传进来，
    ///     测试里传固定表。返回 null 表示查不到，用内置兜底。
    /// </param>
    public static ExamBoardThemeColors Resolve(
        ExamBoardThemeConfig? config,
        Func<ExamBoardThemeToken, Color?> followAppColor)
    {
        ArgumentNullException.ThrowIfNull(followAppColor);

        config ??= new ExamBoardThemeConfig();
        var source = Enum.IsDefined(config.Source) ? config.Source : ExamBoardThemeSource.FollowApp;
        var customBase = Enum.IsDefined(config.CustomBase) ? config.CustomBase : ExamBoardThemeBase.FollowApp;
        var preset = ExamBoardThemePresets.Get(config.PresetId);

        return source switch
        {
            ExamBoardThemeSource.FollowApp => ResolveFollowApp(followAppColor),
            ExamBoardThemeSource.Preset => FromPreset(preset),
            ExamBoardThemeSource.Custom when customBase == ExamBoardThemeBase.Preset =>
                FromPreset(preset, config.Palette),
            _ => FromFollowApp(followAppColor, config.Palette)
        };
    }

    private static ExamBoardThemeColors ResolveFollowApp(Func<ExamBoardThemeToken, Color?> followAppColor) =>
        ExamBoardThemeColors.Create(followAppColor);

    private static ExamBoardThemeColors FromFollowApp(
        Func<ExamBoardThemeToken, Color?> followAppColor,
        ExamBoardThemeOverrides? overrides)
    {
        var palette = overrides ?? new ExamBoardThemeOverrides();
        return ExamBoardThemeColors.Create(token => token.GetOverride(palette) ?? followAppColor(token));
    }

    private static ExamBoardThemeColors FromPreset(
        ExamBoardThemePreset preset,
        ExamBoardThemeOverrides? overrides = null)
    {
        var palette = overrides ?? new ExamBoardThemeOverrides();
        return ExamBoardThemeColors.Create(token => token.GetOverride(palette) ?? preset.Get(token));
    }
}