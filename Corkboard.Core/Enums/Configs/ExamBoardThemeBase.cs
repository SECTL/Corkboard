namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     自定义主题的「底」：用户只改了个别颜色时，其余 token 从哪来。枚举按数字落盘，只能在末尾追加成员。
/// </summary>
public enum ExamBoardThemeBase
{
    /// <summary>跟随应用外观（默认）：其余 token 继续跟着 Fluent 亮/暗走。</summary>
    FollowApp = 0,

    /// <summary>某套内置预设：其余 token 沿用该预设，不随应用亮/暗变化。</summary>
    Preset = 1
}