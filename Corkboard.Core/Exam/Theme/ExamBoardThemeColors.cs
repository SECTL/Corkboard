using Avalonia.Media;

namespace Corkboard.Core.Exam.Theme;

/// <summary>
///     一套主题解析后的 13 个语义色。只读、可跨线程传递。
/// </summary>
public sealed class ExamBoardThemeColors
{
    private readonly IReadOnlyDictionary<string, Color> _values;

    private ExamBoardThemeColors(IReadOnlyDictionary<string, Color> values) => _values = values;

    public Color Surface => Get("surface");
    public Color TextPrimary => Get("text_primary");
    public Color TextSecondary => Get("text_secondary");
    public Color TextTertiary => Get("text_tertiary");
    public Color Subtitle => Get("subtitle");
    public Color Divider => Get("divider");
    public Color Upcoming => Get("upcoming");
    public Color InProgress => Get("in_progress");
    public Color Urgent => Get("urgent");
    public Color Highlight => Get("highlight");
    public Color ExitTrack => Get("exit_track");
    public Color ExitRingStart => Get("exit_ring_start");
    public Color ExitRingEnd => Get("exit_ring_end");

    public Color Get(string tokenId) => _values[tokenId];

    public Color Get(ExamBoardThemeToken token) => _values[token.Id];

    /// <summary>按 token 表逐个解析；<paramref name="resolve" /> 返回 null 时用内置兜底。</summary>
    public static ExamBoardThemeColors Create(Func<ExamBoardThemeToken, Color?> resolve)
    {
        var values = new Dictionary<string, Color>(ExamBoardThemeTokens.All.Count, StringComparer.Ordinal);
        foreach (var token in ExamBoardThemeTokens.All)
            values[token.Id] = resolve(token) ?? FollowAppDefaults.Get(token);
        return new ExamBoardThemeColors(values);
    }

    /// <summary>
    ///     「跟随应用外观」查不到资源时的兜底（设计期、启动早期、Fluent 笔刷不是纯色）。
    ///     取偏深的 Fluent 观感，保证任何情况下都不会出现透明/黑屏。
    /// </summary>
    public static ExamBoardThemeColors FollowAppDefaults { get; } = new(new Dictionary<string, Color>
    {
        ["surface"] = Color.Parse("#FF101418"),
        ["text_primary"] = Color.Parse("#FFF2F5FA"),
        ["text_secondary"] = Color.Parse("#FFB7C3D6"),
        ["text_tertiary"] = Color.Parse("#FF7E8CA5"),
        ["subtitle"] = Color.Parse("#FFA8B6CC"),
        ["divider"] = Color.Parse("#FF2B3B54"),
        ["upcoming"] = Color.Parse("#FFE9B45E"),
        ["in_progress"] = Color.Parse("#FF6FD39A"),
        ["urgent"] = Color.Parse("#FFFF6B75"),
        ["highlight"] = Color.Parse("#33243A55"),
        ["exit_track"] = Color.Parse("#FF3C4D66"),
        ["exit_ring_start"] = Color.Parse("#FF0078D4"),
        ["exit_ring_end"] = Color.Parse("#FFE81123")
    });
}