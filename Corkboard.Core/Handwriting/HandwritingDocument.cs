namespace Corkboard.Core.Handwriting;

/// <summary>
///     一页手写的完整数据：画布的高宽比 + 一笔笔的轨迹。
///     <para>
///         落盘时由 <see cref="HandwritingCodec" /> 编成一段 JSON（见格式说明），
///         存进 <c>Note.Handwriting</c>；预览控件解析同一份 JSON 画图。
///     </para>
///     <para>
///         为什么要 <see cref="Aspect" />：点只记 0..1 的相对位置，不知道画布有多高宽的话，
///         同一份笔迹在宽扁的卡片和竖长的编辑框里会被拉成两种形状。
///     </para>
/// </summary>
public sealed class HandwritingDocument
{
    /// <summary>当前 JSON 格式版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>没有 aspect 信息时按常见的横版便签比例来。</summary>
    public const double DefaultAspect = 1.6;

    /// <summary>aspect（高 / 宽）的下限：再扁下去笔迹会被压成一条线。</summary>
    public const double MinAspect = 0.1;

    /// <summary>aspect 的上限。</summary>
    public const double MaxAspect = 10;

    /// <summary>画布的高宽比（高 / 宽）。创建时默认 <see cref="DefaultAspect" />。</summary>
    public double Aspect { get; set; } = DefaultAspect;

    /// <summary>
    ///     JSON 里到底有没有写 aspect。<see cref="Aspect" /> 在两种情况下都可能是 1.6：
    ///     老数据没写（读出来是默认值）、或者恰好画的就是 1.6。
    ///     预览没有别的线索时用这个标志决定是「用文档自己的」还是「用控件的 Aspect 属性」。
    /// </summary>
    public bool HasAspect { get; set; }

    /// <summary>没有参考宽度信息时按这个宽度（DIP）画。</summary>
    public const double DefaultReferenceWidth = 540;

    /// <summary>参考宽度下限：再小坐标会粗得没法看。</summary>
    public const double MinReferenceWidth = 80;

    /// <summary>参考宽度上限：再大归一化坐标的有效精度就不够了。</summary>
    public const double MaxReferenceWidth = 8000;

    /// <summary>
    ///     这页手写是按多宽（DIP）画的。笔迹坐标是相对这个宽度归一化的，
    ///     **不是**相对当前控件的宽度——把布置作业表单里的内容框拖大拖小时，
    ///     已经写上去的笔迹不能跟着缩放 / 挪位置（用户明确要求）。
    /// </summary>
    public double ReferenceWidth { get; set; } = DefaultReferenceWidth;

    /// <summary>落盘的数据里有没有写参考宽度；老数据没有，载入时按控件宽度补一个。</summary>
    public bool HasReferenceWidth { get; set; }

    /// <summary>所有笔画。橡皮擦掉的墨会直接从列表里剪掉，不会在这里留看不见的擦除笔。</summary>
    public List<HandwritingStroke> Strokes { get; set; } = [];

    /// <summary>有没有能画出来的墨。</summary>
    public bool HasInk => Strokes.Exists(stroke => stroke.IsVisible);

    /// <summary>能画出来的笔画数（不含橡皮历史记录）。</summary>
    public int VisibleStrokeCount => Strokes.Count(stroke => stroke.IsVisible);

    /// <summary>
    ///     把整页笔迹画进 <paramref name="renderWidth" /> 宽的画面时，笔宽要乘的比例。
    ///     <para>
    ///         坐标本来就是相对 <see cref="ReferenceWidth" /> 归一化的，预览控件把它们铺到卡片宽度上时
    ///         位置已经跟着缩放了；<b>笔宽必须乘同一个比例</b>，否则卡片上笔画相对字变粗、
    ///         字与字的空隙被墨吃掉（用户反馈：「写的时候间隔正常，卡片上挤成一团」）。
    ///         编辑画布按参考尺寸 1:1 画，算出来正好是 1，所以对它没有影响。
    ///     </para>
    /// </summary>
    /// <param name="renderWidth">实际用来画整页的宽度（DIP）。</param>
    /// <returns>笔宽缩放比例；没有参考宽度或宽度离谱时退回 1。</returns>
    public double ResolveRenderScale(double renderWidth)
    {
        if (!HasReferenceWidth || !double.IsFinite(renderWidth) || renderWidth <= 0)
            return 1;

        var referenceWidth = ClampReferenceWidth(ReferenceWidth);
        return referenceWidth > 0 ? renderWidth / referenceWidth : 1;
    }

    /// <summary>把高宽比夹进合法区间；非有限值退回 <see cref="DefaultAspect" />。</summary>
    public static double ClampAspect(double aspect)
    {
        return double.IsFinite(aspect) ? Math.Clamp(aspect, MinAspect, MaxAspect) : DefaultAspect;
    }

    /// <summary>把参考宽度夹进合法区间；非有限值退回 <see cref="DefaultReferenceWidth" />。</summary>
    public static double ClampReferenceWidth(double width)
    {
        return double.IsFinite(width) ? Math.Clamp(width, MinReferenceWidth, MaxReferenceWidth) : DefaultReferenceWidth;
    }
}
