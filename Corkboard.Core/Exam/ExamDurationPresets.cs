namespace Corkboard.Core.Exam;

/// <summary>
///     科目 → 常见考试时长（分钟）。
///     <para>
///         向导里勾上科目就自动带出时长，绝大多数情况不用手改。查不到的科目（用户在作业板里
///         自建的「班会」「社团」之类）回退到 <see cref="FallbackMinutes" />。
///     </para>
///     <para>
///         匹配忽略大小写与首尾空白，同时认中英文两种写法（作业板科目是用户自己敲的，
///         写「英语」还是「English」都得认）。
///     </para>
/// </summary>
public static class ExamDurationPresets
{
    /// <summary>查不到预设时用多少分钟。与「政史地」那一档一致，是最常见的单科时长。</summary>
    public const int FallbackMinutes = 75;

    /// <summary>时长的合法下限（分钟）。</summary>
    public const int MinMinutes = 10;

    /// <summary>时长的合法上限（分钟）——十个小时，挡一下手改出来的天文数字。</summary>
    public const int MaxMinutes = 600;

    /// <summary>下拉里直接给的几个快捷值（用户点名要的那几档，含 60 与 30）。</summary>
    public static readonly IReadOnlyList<int> QuickMinutes = [30, 60, 75, 120, 150];

    private static readonly Dictionary<string, int> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // 150 分钟（两个半小时）
        ["语文"] = 150,
        ["chinese"] = 150,

        // 120 分钟（两个小时）
        ["数学"] = 120,
        ["math"] = 120,
        ["mathematics"] = 120,
        ["英语"] = 120,
        ["english"] = 120,

        // 75 分钟
        ["物理"] = 75,
        ["physics"] = 75,
        ["化学"] = 75,
        ["chemistry"] = 75,
        ["生物"] = 75,
        ["biology"] = 75,
        ["政治"] = 75,
        ["思想政治"] = 75,
        ["道德与法治"] = 75,
        ["politics"] = 75,
        ["历史"] = 75,
        ["history"] = 75,
        ["地理"] = 75,
        ["geography"] = 75,

        // 60 分钟
        ["信息技术"] = 60,
        ["信息"] = 60,
        ["通用技术"] = 60,
        ["音乐"] = 60,
        ["music"] = 60,
        ["it"] = 60
    };

    /// <summary>这个科目的预设时长；查不到就是 <see cref="FallbackMinutes" />。</summary>
    public static int Resolve(string? subject)
    {
        var key = (subject ?? string.Empty).Trim();
        if (key.Length == 0)
            return FallbackMinutes;

        return Table.TryGetValue(key, out var minutes) ? minutes : FallbackMinutes;
    }

    /// <summary>把时长夹进合法区间（用户手输、老数据带脏值都过一遍）。</summary>
    public static int Clamp(int minutes) => Math.Clamp(minutes, MinMinutes, MaxMinutes);

    /// <summary>时长文案（「120 分钟」）。</summary>
    public static string Describe(int minutes) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture,
            Langs.Common.Resources.Exam_Plan_DurationMinutes, Clamp(minutes));
}
