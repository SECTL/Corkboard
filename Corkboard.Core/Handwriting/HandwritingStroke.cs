using Avalonia.Media;

namespace Corkboard.Core.Handwriting;

/// <summary>
///     一笔手写。一笔 = 从按下到抬起之间那段连续轨迹，颜色、粗细、是不是橡皮
///     都记在这一笔上，点是归一化坐标（见 <see cref="HandwritingPoint" />）。
///     <para>
///         约定：创建之后不再改 <see cref="Color" /> / <see cref="Width" /> / <see cref="Erase" />；
///         正在画的那一笔会往 <see cref="Points" /> 末尾追加点，画完就当只读用。
///         撤销快照是「笔画列表的浅拷贝」，靠的就是这个约定——旧笔对象不会被原地改。
///     </para>
/// </summary>
public sealed class HandwritingStroke
{
    /// <summary>笔色。默认用色板里第一个颜色（近黑），免得新建出来是一笔透明的。</summary>
    public Color Color { get; init; } = HandwritingStyle.DefaultColors[0];

    /// <summary>笔宽（DIP）。读的时候过一遍 <see cref="HandwritingStyle.ClampPenWidth" />。</summary>
    public double Width { get; init; } = HandwritingStyle.DefaultPenWidth;

    /// <summary>
    ///     这一笔是不是橡皮痕迹。<b>正常写出来的数据里永远是 false</b>：橡皮擦掉的是已有点，
    ///     不会自己存成一笔看不见的笔画，否则预览与导出会露出擦不掉的墨。
    ///     字段留着是为了读得进别人手写 / 旧版本可能留下的数据（解出来也不参与渲染）。
    /// </summary>
    public bool Erase { get; init; }

    /// <summary>这一笔的点。坐标为归一化的 0..1。</summary>
    public List<HandwritingPoint> Points { get; init; } = [];

    /// <summary>能不能画出来：不是橡皮、且至少有一个点。</summary>
    public bool IsVisible => !Erase && Points.Count > 0;
}
