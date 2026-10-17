using Avalonia;
using Avalonia.Media;
using Corkboard.Core.Handwriting;

namespace Corkboard.Core.Controls;

/// <summary>
///     把一页手写画到 <see cref="DrawingContext" /> 上：可编辑画布与只读预览共用这一份，
///     免得两边各写一套「笔怎么画」的代码、修了一边漏一边。
///     <para>
///         笔宽是绝对 DIP（不随画布宽度缩放）：预览是缩略图、画布是实际大小，但用户挑的
///         「4 像素粗」就是 4 像素粗，跟内容字号的处理一致。
///     </para>
///     <para>
///         性能：每一笔只建一个 <see cref="StreamGeometry" />（不是一个点一个控件/图元），
///         同色同粗细的 <see cref="Pen" /> 在同一次渲染里复用。画的时候只重画这一层，
///         笔画上千条也不至于一卡一卡。
///     </para>
/// </summary>
internal static class HandwritingRenderer
{
    /// <summary>把文档画进给定的矩形区域（坐标从归一化的 0..1 映射到区域里）。</summary>
    public static void Render(DrawingContext context, HandwritingDocument document, Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0)
            return;

        // 一次渲染里的笔刷与画笔缓存：按颜色 / 颜色+粗细复用。
        var brushes = new Dictionary<Color, IBrush>();
        var pens = new Dictionary<(Color Color, int Width), IPen>();

        foreach (var stroke in document.Strokes)
        {
            if (!stroke.IsVisible)
                continue;

            var width = double.IsFinite(stroke.Width) ? Math.Max(stroke.Width, 0.5) : HandwritingStyle.DefaultPenWidth;

            if (!brushes.TryGetValue(stroke.Color, out var brush))
            {
                brush = new SolidColorBrush(stroke.Color);
                brushes[stroke.Color] = brush;
            }

            var widthKey = (int)Math.Round(width * 100, MidpointRounding.AwayFromZero);
            if (!pens.TryGetValue((stroke.Color, widthKey), out var pen))
            {
                pen = new Pen(brush, width, null, PenLineCap.Round, PenLineJoin.Round);
                pens[(stroke.Color, widthKey)] = pen;
            }

            if (stroke.Points.Count == 1)
            {
                // 单点笔画 = 一个圆点：用圆头笔画一笔零长度线不会出墨，只能自己画个圆。
                var center = Map(stroke.Points[0], area);
                var radius = width / 2;
                context.DrawEllipse(brush, null, center, radius, radius);
                continue;
            }

            var geometry = new StreamGeometry();
            using (var geometryContext = geometry.Open())
            {
                var first = Map(stroke.Points[0], area);
                geometryContext.BeginFigure(first, false);

                for (var index = 1; index < stroke.Points.Count; index++)
                    geometryContext.LineTo(Map(stroke.Points[index], area), true);

                geometryContext.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }
    }

    private static Point Map(HandwritingPoint point, Rect area)
    {
        return new Point(area.X + point.X * area.Width, area.Y + point.Y * area.Height);
    }
}
