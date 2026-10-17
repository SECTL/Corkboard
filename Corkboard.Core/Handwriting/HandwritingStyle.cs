using Avalonia.Media;

namespace Corkboard.Core.Handwriting;

/// <summary>
///     手写笔的默认值与上下限：笔宽、橡皮宽、默认色板。
///     <para>
///         纯逻辑、不碰界面（有单测）：设置页的滑杆区间、画布上「粗细」那一排按钮、
///         新建笔记的默认笔，都从这里取，免得三处各写一组数字。
///     </para>
/// </summary>
public static class HandwritingStyle
{
    /// <summary>笔迹最细。</summary>
    public const double MinPenWidth = 1;

    /// <summary>笔迹最粗。</summary>
    public const double MaxPenWidth = 24;

    /// <summary>没设过时的笔宽。</summary>
    public const double DefaultPenWidth = 4;

    /// <summary>橡皮最小。</summary>
    public const double MinEraserWidth = 8;

    /// <summary>橡皮最大。</summary>
    public const double MaxEraserWidth = 64;

    /// <summary>没设过时的橡皮宽度：比笔宽大得多，擦起来才知道擦到哪了。</summary>
    public const double DefaultEraserWidth = 24;

    /// <summary>笔迹色板最多几个（和内容色板同一套规则）。</summary>
    public const int MaxColors = RichText.RichTextPalette.MaxColors;

    /// <summary>
    ///     没配置过时手写用的几个颜色。不跟随主题：笔迹是用户挑的颜色，
    ///     黑板上写字时换主题不该把已经画好的笔迹改色（笔迹颜色存在笔迹 JSON 里）。
    /// </summary>
    public static IReadOnlyList<Color> DefaultColors { get; } =
    [
        Color.Parse("#FF1A1A1A"),
        Color.Parse("#FFD13438"),
        Color.Parse("#FF0078D4"),
        Color.Parse("#FF107C10"),
        Color.Parse("#FFFFB900")
    ];

    public static double ClampPenWidth(double width)
    {
        return double.IsFinite(width) ? Math.Clamp(width, MinPenWidth, MaxPenWidth) : DefaultPenWidth;
    }

    public static double ClampEraserWidth(double width)
    {
        return double.IsFinite(width) ? Math.Clamp(width, MinEraserWidth, MaxEraserWidth) : DefaultEraserWidth;
    }
}
