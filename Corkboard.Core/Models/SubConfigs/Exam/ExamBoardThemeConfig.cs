using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam.Theme;

namespace Corkboard.Core.Models.SubConfigs.Exam;

/// <summary>
///     考试看板的主题配置（<c>exam_board_theme</c>）。默认全部跟随应用外观，
///     所以「什么都没配」的用户观感与升级前完全一致。
/// </summary>
public partial class ExamBoardThemeConfig : ObservableObject
{
    /// <summary>新增预设时只往后面加；这个 id 会写进 JSON，发布后不能改名。</summary>
    public const string DefaultPresetId = ExamBoardThemePresets.DefaultId;

    /// <summary>主题来源：跟随应用外观 / 预设 / 自定义。</summary>
    [ObservableProperty] private ExamBoardThemeSource _source = ExamBoardThemeSource.FollowApp;

    /// <summary>自定义的「底」：跟随应用外观或某套预设（只在 Source=Custom 时用）。</summary>
    [ObservableProperty] private ExamBoardThemeBase _customBase = ExamBoardThemeBase.FollowApp;

    /// <summary>当前预设：Source=Preset 时是选中的预设；CustomBase=Preset 时是自定义的底。</summary>
    [ObservableProperty] private string _presetId = DefaultPresetId;

    /// <summary>自定义覆盖：null = 沿用底，只有用户改过的项才有值。</summary>
    [ObservableProperty] private ExamBoardThemeOverrides _palette = new();

    /// <summary>
    ///     读盘后归一：非法枚举回「跟随应用外观」、未知预设回默认预设；
    ///     除 <c>highlight</c> 外的全透明色当作坏数据清掉（手改坏串会被读成 default 全透明）。
    /// </summary>
    public void Normalize()
    {
        Source = Enum.IsDefined(Source) ? Source : ExamBoardThemeSource.FollowApp;
        CustomBase = Enum.IsDefined(CustomBase) ? CustomBase : ExamBoardThemeBase.FollowApp;
        PresetId = ExamBoardThemePresets.NormalizeId(PresetId);

        Palette ??= new ExamBoardThemeOverrides();
        foreach (var token in ExamBoardThemeTokens.All)
        {
            if (token.Id == "highlight")
                continue;

            var color = token.GetOverride(Palette);
            if (color is { A: 0 })
                token.SetOverride(Palette, null);
        }
    }
}