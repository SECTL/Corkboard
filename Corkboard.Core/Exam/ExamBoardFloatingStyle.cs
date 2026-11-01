namespace Corkboard.Core.Exam;

/// <summary>
///     考试看板最小化之后，屏幕底部那颗浮窗按钮的**尺寸与缩放**：默认值、上下限、夹取和文字归一全在这里。
///     <para>
///         与 <see cref="ExamBoardTextStyle" /> 同一个思路——设置页的输入框上下限与浮窗实际画出来的大小
///         读同一组常量，**不要**在 XAML 里另写一份数字（那样设置说「60–200%」，实际却是别的样子）。
///     </para>
///     <para>
///         浮窗是圆角矩形（不是圆）：「软件图标 + 一行字」横着排，靠 <see cref="BaseFontSize" /> 与内边距
///         撑出自然宽度（窗口 <c>SizeToContent="WidthAndHeight"</c>），缩放只按百分比放大字号、图标、圆角与内边距。
///     </para>
/// </summary>
public static class ExamBoardFloatingStyle
{
    /// <summary>浮窗按钮的默认缩放（百分比）。</summary>
    public const double DefaultScalePercent = 100;

    /// <summary>缩放下限：再小字就看不清了。</summary>
    public const double MinScalePercent = 60;

    /// <summary>缩放上限：再大就挡住看板内容了。</summary>
    public const double MaxScalePercent = 200;

    /// <summary>100% 时的字号。</summary>
    public const double BaseFontSize = 15;

    /// <summary>100% 时的软件图标边长。</summary>
    public const double BaseIconSize = 18;

    /// <summary>100% 时的圆角半径。圆角半径不到半高，所以它一直是「圆角矩形」而不是胶囊。</summary>
    public const double BaseCornerRadius = 12;

    /// <summary>100% 时的左右内边距。</summary>
    public const double BasePaddingHorizontal = 16;

    /// <summary>100% 时的上下内边距。</summary>
    public const double BasePaddingVertical = 10;

    /// <summary>浮窗离屏幕边缘的距离（DIP，朝屏幕内侧）。</summary>
    public const double EdgeInset = 32;

    /// <summary>按钮文字最长多少个字符：再长就横着占掉半个屏幕。</summary>
    public const int MaxTextLength = 24;

    /// <summary>长按多久算「要退出」：与看板上那颗角标同一个承诺，2 秒。</summary>
    public const double HoldSeconds = 2;

    /// <summary>
    ///     无字模式（只画图标）时图标相对正常那一档放大多少倍：字没了，一颗小图标压不住整块按钮。
    /// </summary>
    public const double IconOnlyIconBoost = 1.8;

    /// <summary>按钮里那行操作提示的字号相对主文字的比例（小一档，但不至于看不见）。</summary>
    public const double HintFontRatio = 0.72;

    /// <summary>操作提示的字号下限：缩放调到最小档也算得出来。</summary>
    public const double MinHintFontSize = 9;

    /// <summary>把缩放百分比夹进 <see cref="MinScalePercent" />–<see cref="MaxScalePercent" />。坏值退回默认值。</summary>
    public static double ClampScalePercent(double percent)
    {
        return double.IsFinite(percent) ? Math.Clamp(percent, MinScalePercent, MaxScalePercent) : DefaultScalePercent;
    }

    /// <summary>缩放倍数（1.0 = 100%）。</summary>
    public static double ResolveScale(double percent) => ClampScalePercent(percent) / 100d;

    /// <summary>按缩放算字号。</summary>
    public static double ResolveFontSize(double percent) => Math.Round(BaseFontSize * ResolveScale(percent));

    /// <summary>按缩放算图标边长。</summary>
    public static double ResolveIconSize(double percent) => Math.Round(BaseIconSize * ResolveScale(percent));

    /// <summary>按缩放算圆角半径。</summary>
    public static double ResolveCornerRadius(double percent) => Math.Round(BaseCornerRadius * ResolveScale(percent));

    /// <summary>按缩放算左右内边距。</summary>
    public static double ResolvePaddingHorizontal(double percent) =>
        Math.Round(BasePaddingHorizontal * ResolveScale(percent));

    /// <summary>按缩放算上下内边距。</summary>
    public static double ResolvePaddingVertical(double percent) =>
        Math.Round(BasePaddingVertical * ResolveScale(percent));

    /// <summary>无字模式下的图标边长：比正常那一档放大 <see cref="IconOnlyIconBoost" /> 倍。</summary>
    public static double ResolveIconOnlyIconSize(double percent) =>
        Math.Round(BaseIconSize * ResolveScale(percent) * IconOnlyIconBoost);

    /// <summary>无字模式下的内边距：只留一圈等宽留白，让按钮看上去方正。</summary>
    public static double ResolveIconOnlyPadding(double percent) => ResolvePaddingVertical(percent);

    /// <summary>
    ///     按钮里那行操作提示（「按住 2 秒退出」）的字号：比标题小一档，但小到看不清就白写了，
    ///     所以有个 9 号兜底（缩放调到 60% 时也算得出来）。
    /// </summary>
    public static double ResolveHintFontSize(double percent) =>
        Math.Max(MinHintFontSize, Math.Round(BaseFontSize * ResolveScale(percent) * HintFontRatio));

    /// <summary>
    ///     收拾用户填的按钮文字：去首尾空白、换行压成空格、超长截断。
    ///     返回空串表示「没填」，界面那边会退回默认文案（<c>Settings_Exam_Floating_DefaultText</c>）。
    /// </summary>
    public static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var flattened = text.ReplaceLineEndings(" ").Trim();
        while (flattened.Contains("  ", StringComparison.Ordinal))
            flattened = flattened.Replace("  ", " ", StringComparison.Ordinal);

        return flattened.Length <= MaxTextLength ? flattened : flattened[..MaxTextLength].TrimEnd();
    }
}
