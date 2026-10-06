namespace Corkboard.Core.Models.Board;

/// <summary>
///     作业内容的字号约束与预设档位：设置页的默认字号、布置作业表单里的「整篇字号」与选段字号、
///     渲染时的兜底，三处共用一份，免得三边各写一组数字。
/// </summary>
public static class BoardContentStyle
{
    /// <summary>没设过默认字号时用的字号，和 Avalonia 正文默认值一致。</summary>
    public const double DefaultFontSize = 14;

    /// <summary>字号下限。</summary>
    public const double MinFontSize = 8;

    /// <summary>字号上限。</summary>
    public const double MaxFontSize = 96;

    /// <summary>下拉里给用户挑的档位。落盘/渲染一律夹在上下限里，档位只是省得手打。</summary>
    public static readonly IReadOnlyList<double> PresetFontSizes =
        [12, 14, 16, 18, 20, 24, 28, 32, 40, 48];

    /// <summary>把字号夹进合法区间。NaN / 无穷这类坏值退回默认字号，不让它流到渲染层。</summary>
    public static double ClampFontSize(double size)
    {
        return double.IsFinite(size) ? Math.Clamp(size, MinFontSize, MaxFontSize) : DefaultFontSize;
    }
}
