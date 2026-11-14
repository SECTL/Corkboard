namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     考试看板主题的颜色来源。枚举按数字落盘，只能在末尾追加成员。
/// </summary>
public enum ExamBoardThemeSource
{
    /// <summary>跟随应用外观：13 个 token 全部查 Fluent 的亮/暗资源，与今天的观感一致。</summary>
    FollowApp = 0,

    /// <summary>使用一套内置预设：绝对观感，不随应用亮/暗变化。</summary>
    Preset = 1,

    /// <summary>自定义：在「跟随应用外观」或某套预设的底上，只覆盖用户改过的 token。</summary>
    Custom = 2
}