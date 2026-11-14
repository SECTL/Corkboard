using Avalonia.Media;

namespace Corkboard.Core.Exam.Theme;

/// <summary>
///     一套内置主题。id 是数据契约（冻结且只能追加），显示名走本地化资源键。
///     配色只是设计基线；预设是绝对观感，不随应用亮/暗变化。
/// </summary>
public sealed class ExamBoardThemePreset
{
    private readonly IReadOnlyDictionary<string, Color> _colors;

    private ExamBoardThemePreset(string id, IReadOnlyDictionary<string, string> hexColors)
    {
        Id = id;
        NameKey = "Settings_Exam_Theme_Preset_" + ToPascal(id);
        DescriptionKey = NameKey + "Description";

        var parsed = new Dictionary<string, Color>(hexColors.Count, StringComparer.Ordinal);
        foreach (var (tokenId, hex) in hexColors)
            parsed[tokenId] = Color.Parse(hex);
        _colors = parsed;
    }

    public string Id { get; }

    public string NameKey { get; }

    public string DescriptionKey { get; }

    public Color Get(string tokenId) => _colors[tokenId];

    public Color Get(ExamBoardThemeToken token) => _colors[token.Id];

    private static string ToPascal(string id) =>
        string.Concat(id.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    internal static ExamBoardThemePreset Create(string id, IReadOnlyDictionary<string, string> hexColors) =>
        new(id, hexColors);
}

/// <summary>五套内置预设与 id 归一。</summary>
public static class ExamBoardThemePresets
{
    public const string DefaultId = "midnight";

    public static IReadOnlyList<ExamBoardThemePreset> All { get; } =
    [
        ExamBoardThemePreset.Create("midnight", new Dictionary<string, string>
        {
            ["surface"] = "#0B1220",
            ["text_primary"] = "#F2F5FA",
            ["text_secondary"] = "#B7C3D6",
            ["text_tertiary"] = "#7E8CA5",
            ["subtitle"] = "#A8B6CC",
            ["divider"] = "#2B3B54",
            ["upcoming"] = "#E9B45E",
            ["in_progress"] = "#6FD39A",
            ["urgent"] = "#FF6B75",
            ["highlight"] = "#33243A55",
            ["exit_track"] = "#3C4D66",
            ["exit_ring_start"] = "#4BA3F2",
            ["exit_ring_end"] = "#FF5A5A"
        }),
        ExamBoardThemePreset.Create("paper", new Dictionary<string, string>
        {
            ["surface"] = "#F6F3EA",
            ["text_primary"] = "#1F2933",
            ["text_secondary"] = "#55606E",
            ["text_tertiary"] = "#8A94A3",
            ["subtitle"] = "#6B7684",
            ["divider"] = "#D8D2C4",
            ["upcoming"] = "#A96500",
            ["in_progress"] = "#237A4B",
            ["urgent"] = "#B3261E",
            ["highlight"] = "#1A1F2933",
            ["exit_track"] = "#C3BDAD",
            ["exit_ring_start"] = "#2563EB",
            ["exit_ring_end"] = "#DC2626"
        }),
        ExamBoardThemePreset.Create("chalkboard", new Dictionary<string, string>
        {
            ["surface"] = "#14301F",
            ["text_primary"] = "#F1EDDC",
            ["text_secondary"] = "#C7CBB0",
            ["text_tertiary"] = "#91A087",
            ["subtitle"] = "#B7BFA4",
            ["divider"] = "#2F4A39",
            ["upcoming"] = "#E3C471",
            ["in_progress"] = "#92D89A",
            ["urgent"] = "#FF7A66",
            ["highlight"] = "#332F4A39",
            ["exit_track"] = "#3E5A47",
            ["exit_ring_start"] = "#8FD3FF",
            ["exit_ring_end"] = "#FF8A7A"
        }),
        ExamBoardThemePreset.Create("focus", new Dictionary<string, string>
        {
            ["surface"] = "#201C19",
            ["text_primary"] = "#E6E0D5",
            ["text_secondary"] = "#BCB4A6",
            ["text_tertiary"] = "#8C8579",
            ["subtitle"] = "#A9A192",
            ["divider"] = "#3B352E",
            ["upcoming"] = "#D2A45C",
            ["in_progress"] = "#9BC98C",
            ["urgent"] = "#DE8B7B",
            ["highlight"] = "#332B251F",
            ["exit_track"] = "#4A443B",
            ["exit_ring_start"] = "#93B8E3",
            ["exit_ring_end"] = "#DF8275"
        }),
        ExamBoardThemePreset.Create("contrast", new Dictionary<string, string>
        {
            ["surface"] = "#000000",
            ["text_primary"] = "#FFFFFF",
            ["text_secondary"] = "#E2E2E2",
            ["text_tertiary"] = "#CACACA",
            ["subtitle"] = "#D6D6D6",
            ["divider"] = "#7A7A7A",
            ["upcoming"] = "#FFD24A",
            ["in_progress"] = "#6BE38A",
            ["urgent"] = "#FF6B6B",
            ["highlight"] = "#33FFFFFF",
            ["exit_track"] = "#9A9A9A",
            ["exit_ring_start"] = "#7CC4FF",
            ["exit_ring_end"] = "#FF5252"
        })
    ];

    public static ExamBoardThemePreset Get(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal)) ?? GetDefault();

    public static ExamBoardThemePreset GetDefault() => All[0];

    /// <summary>未知 / 空 id 回默认预设；自定义主题的底也靠它兜底。</summary>
    public static string NormalizeId(string? id) => Get(id).Id;
}