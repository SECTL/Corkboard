using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Corkboard.Core.Handwriting;

namespace Corkboard.Core.Controls;

/// <summary>
///     只读的笔迹预览控件：把作业里存的笔迹 JSON 画成一张不可交互的图，用在作业卡片上。
///     <para>
///         笔迹数据格式与写入方见手写功能（<see cref="HandwritingCanvas" />）：归一化坐标点集 +
///         画布高宽比 + 每一笔的颜色与粗细。<see cref="Aspect" /> 是画布的高宽比，控件按它定高，
///         保证笔迹不被拉变形；数据里自带 aspect 时以数据里的为准（画的时候画布可能和现在卡片不一样高）。
///     </para>
///     <para>
///         空数据、坏数据一律什么都不画，也不抛异常——卡片上一条解析不了的笔迹不该让整个作业板崩掉。
///     </para>
/// </summary>
public class HandwritingPreview : Control
{
    /// <summary>量不到宽度时先按这个宽排版（比如放在横向滚动区里，可用宽度是无穷大）。</summary>
    private const double FallbackWidth = 240;

    /// <summary>笔迹数据（JSON 文本）。为空或解析不了时什么都不画。</summary>
    public static readonly StyledProperty<string?> DataProperty =
        AvaloniaProperty.Register<HandwritingPreview, string?>(nameof(Data));

    /// <summary>画布的高宽比（高 / 宽），默认按常见的横版便签比例。</summary>
    public static readonly StyledProperty<double> AspectProperty =
        AvaloniaProperty.Register<HandwritingPreview, double>(nameof(Aspect), HandwritingDocument.DefaultAspect);

    static HandwritingPreview()
    {
        AffectsRender<HandwritingPreview>(DataProperty, AspectProperty);

        // 数据里的 aspect 会改期望高度，所以变了要重新量。
        AffectsMeasure<HandwritingPreview>(DataProperty, AspectProperty);
    }

    public HandwritingPreview()
    {
        // 笔迹按宽度定高，卡片上的空间不够时裁掉，别画到旁边去。
        ClipToBounds = true;
    }

    public string? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Aspect
    {
        get => GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    /// <summary>
    ///     期望尺寸：宽度铺满可用宽度，高度 = 宽度 × aspect。
    ///     不能返回 0——作业卡片靠这个高度把笔迹区撑出来，返回 0 就什么都看不见了。
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var aspect = ResolveAspect();
        var width = double.IsFinite(availableSize.Width) && availableSize.Width > 0
            ? availableSize.Width
            : FallbackWidth;

        return new Size(width, width * aspect);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!HandwritingCodec.TryDecode(Data, out var document) || !document.HasInk)
            return;

        var width = Bounds.Width;
        if (!double.IsFinite(width) || width <= 0)
            return;

        // 数据里有 aspect 就用它（画的时候画布的真实比例），否则退回控件自己的属性。
        var aspect = document.HasAspect
            ? HandwritingDocument.ClampAspect(document.Aspect)
            : HandwritingDocument.ClampAspect(Aspect);

        // 字面要求：按控件宽度等比缩放（height = width * aspect），上下居中画。
        var contentHeight = width * aspect;
        var top = (Bounds.Height - contentHeight) / 2;

        HandwritingRenderer.Render(context, document, new Rect(0, top, width, contentHeight));
    }

    /// <summary>实际用来定高的 aspect：优先数据里的，数据坏了才看属性。</summary>
    private double ResolveAspect()
    {
        if (HandwritingCodec.TryDecode(Data, out var document) && document.HasAspect)
            return HandwritingDocument.ClampAspect(document.Aspect);

        return HandwritingDocument.ClampAspect(Aspect);
    }
}
