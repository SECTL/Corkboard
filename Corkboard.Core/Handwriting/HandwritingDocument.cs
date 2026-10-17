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

    /// <summary>所有笔画。橡皮擦掉的墨会直接从列表里剪掉，不会在这里留看不见的擦除笔。</summary>
    public List<HandwritingStroke> Strokes { get; set; } = [];

    /// <summary>有没有能画出来的墨。</summary>
    public bool HasInk => Strokes.Exists(stroke => stroke.IsVisible);

    /// <summary>能画出来的笔画数（不含橡皮历史记录）。</summary>
    public int VisibleStrokeCount => Strokes.Count(stroke => stroke.IsVisible);

    /// <summary>把高宽比夹进合法区间；非有限值退回 <see cref="DefaultAspect" />。</summary>
    public static double ClampAspect(double aspect)
    {
        return double.IsFinite(aspect) ? Math.Clamp(aspect, MinAspect, MaxAspect) : DefaultAspect;
    }
}
