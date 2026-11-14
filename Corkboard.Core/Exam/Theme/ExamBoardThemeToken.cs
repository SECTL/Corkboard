using Avalonia.Media;
using Corkboard.Core.Models.SubConfigs.Exam;

namespace Corkboard.Core.Exam.Theme;

/// <summary>
///     一个主题 token 的描述：id、资源键后缀、取值/读写覆盖委托。
///     UI、配置与资源字典三处共用这一份定义，避免各写一张表。
/// </summary>
/// <param name="Id">落盘键，冻结（如 <c>text_primary</c>）。</param>
/// <param name="ResourceKeySuffix">Pascal 后缀（如 <c>TextPrimary</c>），资源键 = ExamBoard + 后缀 + Brush。</param>
public sealed record ExamBoardThemeToken(
    string Id,
    string ResourceKeySuffix,
    Func<ExamBoardThemeColors, Color> Get,
    Func<ExamBoardThemeOverrides, Color?> GetOverride,
    Action<ExamBoardThemeOverrides, Color?> SetOverride)
{
    public string NameKey => "Settings_Exam_Theme_Token_" + ResourceKeySuffix;

    public string BrushKey => "ExamBoard" + ResourceKeySuffix + "Brush";
}

/// <summary>13 个 token 的唯一清单，顺序即设置页颜色列表顺序。</summary>
public static class ExamBoardThemeTokens
{
    private static readonly IReadOnlyList<ExamBoardThemeToken> _all =
    [
        new("surface", "Surface", c => c.Surface, o => o.Surface, (o, v) => o.Surface = v),
        new("text_primary", "TextPrimary", c => c.TextPrimary, o => o.TextPrimary, (o, v) => o.TextPrimary = v),
        new("text_secondary", "TextSecondary", c => c.TextSecondary, o => o.TextSecondary, (o, v) => o.TextSecondary = v),
        new("text_tertiary", "TextTertiary", c => c.TextTertiary, o => o.TextTertiary, (o, v) => o.TextTertiary = v),
        new("subtitle", "Subtitle", c => c.Subtitle, o => o.Subtitle, (o, v) => o.Subtitle = v),
        new("divider", "Divider", c => c.Divider, o => o.Divider, (o, v) => o.Divider = v),
        new("upcoming", "Upcoming", c => c.Upcoming, o => o.Upcoming, (o, v) => o.Upcoming = v),
        new("in_progress", "InProgress", c => c.InProgress, o => o.InProgress, (o, v) => o.InProgress = v),
        new("urgent", "Urgent", c => c.Urgent, o => o.Urgent, (o, v) => o.Urgent = v),
        new("highlight", "Highlight", c => c.Highlight, o => o.Highlight, (o, v) => o.Highlight = v),
        new("exit_track", "ExitTrack", c => c.ExitTrack, o => o.ExitTrack, (o, v) => o.ExitTrack = v),
        new("exit_ring_start", "ExitRingStart", c => c.ExitRingStart, o => o.ExitRingStart, (o, v) => o.ExitRingStart = v),
        new("exit_ring_end", "ExitRingEnd", c => c.ExitRingEnd, o => o.ExitRingEnd, (o, v) => o.ExitRingEnd = v)
    ];

    public static IReadOnlyList<ExamBoardThemeToken> All => _all;

    public static ExamBoardThemeToken Get(string id) =>
        _all.FirstOrDefault(t => t.Id == id) ?? throw new ArgumentOutOfRangeException(nameof(id), id, "未知主题 token");
}