using Avalonia.Media;

namespace Corkboard.Core.Background;

/// <summary>
///     一块板子的背景「算出来的样子」：模式已经归一化、颜色已经解析成 <see cref="Color" />。
///     <para>
///         渲染层只认这个记录，不碰配置——配置里存的是字符串色值，解析失败的兜底全在
///         <see cref="BoardBackgroundSettings.ResolveAppearance" /> 一处做完，窗口 code-behind 别再解析一遍。
///     </para>
/// </summary>
/// <param name="Mode">背景模式（已归一化）。</param>
/// <param name="SolidColor">纯色模式的底色；其余模式为 <c>null</c>。</param>
/// <param name="GradientStart">渐变起点色；非渐变模式为 <c>null</c>。</param>
/// <param name="GradientEnd">渐变终点色；非渐变模式为 <c>null</c>。</param>
/// <param name="GradientAngle">渐变角度（度，0 = 从左往右，顺时针）。</param>
/// <param name="ImagePath">图片的绝对路径；非图片模式或没选图时为 <c>null</c>。</param>
/// <param name="Dim">暗化程度 0–0.9：图片上盖一层黑，0 = 不压。</param>
/// <param name="Blur">模糊半径（像素），0 = 不模糊。</param>
public sealed record BoardBackgroundAppearance(
    BoardBackgroundMode Mode,
    Color? SolidColor,
    Color? GradientStart,
    Color? GradientEnd,
    double GradientAngle,
    string? ImagePath,
    double Dim,
    double Blur)
{
    /// <summary>跟随主题——渲染层什么笔刷都不设，让窗口自己的底色透出来。</summary>
    public static BoardBackgroundAppearance Theme { get; } =
        new(BoardBackgroundMode.Theme, null, null, null, 135, null, 0, 0);

    /// <summary>要不要真去读一张图（模式是图片、且路径不是空的）。</summary>
    public bool HasImage => Mode == BoardBackgroundMode.Image && !string.IsNullOrWhiteSpace(ImagePath);

    /// <summary>要不要盖那层暗化。</summary>
    public bool HasDim => Dim > 0.001;

    /// <summary>要不要模糊。</summary>
    public bool HasBlur => Blur > 0.001;
}
