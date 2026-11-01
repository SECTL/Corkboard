using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Background;

/// <summary>
///     全屏板子背景的配置字段（考试看板与看板模式<b>各存一份</b>，都继承这个类）。
///     <para>
///         为什么做成基类而不是一个嵌套对象：<c>ConfigHandlerBase.Attach()</c> 只认顶层那几个子配置，
///         再嵌一层的对象改了不一定会落盘；而这些字段平铺在各自的配置类上就直接是自动保存的。
///         用继承而不是复制一份，是为了让「归一化」与「解析成 <see cref="BoardBackgroundAppearance" />」
///         只有一份实现——两块板子的背景行为必须一致，改一处不能忘另一处。
///     </para>
///     <para>
///         JSON 键由 <c>SnakeCaseLower</c> 机械推导：<c>background_mode</c> / <c>background_color</c> /
///         <c>background_gradient_start</c> / <c>background_gradient_end</c> / <c>background_gradient_angle</c> /
///         <c>background_image_path</c> / <c>background_dim</c> / <c>background_blur</c>。
///     </para>
/// </summary>
public partial class BoardBackgroundSettings : ObservableObject
{
    /// <summary>暗化的上限：再高整张图就看不见了，用户要的是「压一点、字看得清」。</summary>
    public const double MaxDim = 0.9;

    /// <summary>模糊半径的上限（像素）。</summary>
    public const double MaxBlur = 60;

    /// <summary>纯色模式的默认色（近黑的深灰，两块板子的默认观感一致）。</summary>
    public const string DefaultSolidColor = "#FF101418";

    /// <summary>渐变默认起点色。</summary>
    public const string DefaultGradientStart = "#FF1E3A5F";

    /// <summary>渐变默认终点色。</summary>
    public const string DefaultGradientEnd = "#FF0B1220";

    /// <summary>默认渐变角度（135° = 从左上往右下）。</summary>
    public const double DefaultGradientAngle = 135;

    /// <summary>默认暗化程度：图片模式开箱就是压过一点的，不然白底照片上白字看不见。</summary>
    public const double DefaultDim = 0.25;

    /// <summary>背景模式：跟随主题 / 纯色 / 渐变 / 图片。默认跟随主题。</summary>
    [ObservableProperty] private BoardBackgroundMode _backgroundMode = BoardBackgroundModes.Default;

    /// <summary>纯色模式的底色（<c>#AARRGGBB</c> 或 <c>#RRGGBB</c>）。</summary>
    [ObservableProperty] private string _backgroundColor = DefaultSolidColor;

    /// <summary>渐变模式的起点色。</summary>
    [ObservableProperty] private string _backgroundGradientStart = DefaultGradientStart;

    /// <summary>渐变模式的终点色。</summary>
    [ObservableProperty] private string _backgroundGradientEnd = DefaultGradientEnd;

    /// <summary>渐变角度（度，0 = 从左往右，顺时针转）。</summary>
    [ObservableProperty] private double _backgroundGradientAngle = DefaultGradientAngle;

    /// <summary>
    ///     图片模式的图片路径（绝对路径，指向软件数据目录下复制过来的那份）。
    ///     <para>
    ///         用户从文件夹里挑的图会被<b>复制</b>进数据目录：原图被删、被移动、在 U 盘上拔掉，
    ///         板上也不会突然变成一片黑。路径是本机的绝对路径，换台机器就是空的——
    ///         读不到图时退回主题底色，不报错、不弹窗。
    ///     </para>
    /// </summary>
    [ObservableProperty] private string _backgroundImagePath = "";

    /// <summary>图片上盖的那层黑的浓度（0–0.9）。字看不清就往大调。</summary>
    [ObservableProperty] private double _backgroundDim = DefaultDim;

    /// <summary>图片的模糊半径（0–60 像素）。大屏上模糊一点，后面的字更清楚。</summary>
    [ObservableProperty] private double _backgroundBlur;

    /// <summary>读盘后把背景相关的字段收拾一遍（模式折成合法值、数字夹进上下限）。</summary>
    public void NormalizeBackground()
    {
        BackgroundMode = BoardBackgroundModes.Normalize(BackgroundMode);

        BackgroundDim = Clamp(BackgroundDim, 0, MaxDim, DefaultDim);
        BackgroundBlur = Clamp(BackgroundBlur, 0, MaxBlur, 0);
        BackgroundGradientAngle = Clamp(BackgroundGradientAngle, 0, 360, DefaultGradientAngle);

        BackgroundColor = NormalizeColorText(BackgroundColor, DefaultSolidColor);
        BackgroundGradientStart = NormalizeColorText(BackgroundGradientStart, DefaultGradientStart);
        BackgroundGradientEnd = NormalizeColorText(BackgroundGradientEnd, DefaultGradientEnd);

        BackgroundImagePath = BackgroundImagePath?.Trim() ?? "";
    }

    /// <summary>
    ///     把当前配置算成渲染层能直接用的样子（颜色解析、模式归一化、上下限夹取）。
    ///     <b>解析不了的颜色一律退回默认色</b>——一块坏配置不该把整屏画成透明的。
    /// </summary>
    public BoardBackgroundAppearance ResolveAppearance()
    {
        var mode = BoardBackgroundModes.Normalize(BackgroundMode);
        var dim = Clamp(BackgroundDim, 0, MaxDim, DefaultDim);
        var blur = Clamp(BackgroundBlur, 0, MaxBlur, 0);
        var angle = Clamp(BackgroundGradientAngle, 0, 360, DefaultGradientAngle);

        return mode switch
        {
            BoardBackgroundMode.Solid => BoardBackgroundAppearance.Theme with
            {
                Mode = BoardBackgroundMode.Solid,
                SolidColor = ParseColor(BackgroundColor, DefaultSolidColor)
            },
            BoardBackgroundMode.Gradient => BoardBackgroundAppearance.Theme with
            {
                Mode = BoardBackgroundMode.Gradient,
                GradientStart = ParseColor(BackgroundGradientStart, DefaultGradientStart),
                GradientEnd = ParseColor(BackgroundGradientEnd, DefaultGradientEnd),
                GradientAngle = angle
            },
            BoardBackgroundMode.Image => BoardBackgroundAppearance.Theme with
            {
                Mode = BoardBackgroundMode.Image,
                ImagePath = string.IsNullOrWhiteSpace(BackgroundImagePath) ? null : BackgroundImagePath,
                Dim = dim,
                Blur = blur
            },
            _ => BoardBackgroundAppearance.Theme
        };
    }

    /// <summary>这个背景要不要真的去读一张图。</summary>
    public bool UsesImage => BoardBackgroundModes.Normalize(BackgroundMode) == BoardBackgroundMode.Image;

    /// <summary>把 <c>#AARRGGBB</c> / <c>#RRGGBB</c> 解析成颜色；解析不了给 <paramref name="fallback" /> 那一色。</summary>
    public static Color ParseColor(string? text, string fallback) =>
        Color.TryParse(text, out var color)
            ? color
            : Color.TryParse(fallback, out var fallbackColor)
                ? fallbackColor
                : Colors.Black;

    private static string NormalizeColorText(string? text, string fallback) =>
        Color.TryParse(text, out var color)
            ? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}"
            : fallback;

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
