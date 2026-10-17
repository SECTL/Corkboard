using Avalonia;
using Avalonia.Media;
using Corkboard.Core.Handwriting;

namespace Corkboard.Core.Controls;

/// <summary>
///     把一页手写画到 <see cref="DrawingContext" /> 上：可编辑画布与只读预览共用这一份，
///     免得两边各写一套「笔怎么画」的代码、修了一边漏一边。
///     <para>
///         <b>笔锋</b>：每个点带一个粗细倍率（<see cref="HandwritingPoint.WidthScale" />，按书写速度算：
///         慢写粗、快写细）。所以一笔不再是一根等宽折线，而是沿中心线左右各推出半个笔宽的多边形
///         （两头再补圆帽）。没有笔锋数据的老笔迹倍率都是 1，画出来就是等宽线。
///     </para>
///     <para>
///         笔宽是绝对 DIP（不随画布宽度缩放）：用户挑的「4 像素粗」就是 4 像素粗，
///         跟内容字号的处理一致。
///     </para>
///     <para>
///         性能：每一笔只建一个 <see cref="StreamGeometry" />（不是一个点一个控件/图元）。
///         画的时候只重画这一层，笔画上千条也不至于一卡一卡。
///     </para>
/// </summary>
internal static class HandwritingRenderer
{
    /// <summary>把文档画进给定的矩形区域（坐标从归一化的 0..1 映射到区域里）。</summary>
    public static void Render(DrawingContext context, HandwritingDocument document, Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0)
            return;

        // 同一次渲染里按颜色复用画刷。
        var brushes = new Dictionary<Color, IBrush>();

        foreach (var stroke in document.Strokes)
        {
            if (!stroke.IsVisible)
                continue;

            if (!brushes.TryGetValue(stroke.Color, out var brush))
            {
                brush = new SolidColorBrush(stroke.Color);
                brushes[stroke.Color] = brush;
            }

            var baseWidth = double.IsFinite(stroke.Width)
                ? Math.Max(stroke.Width, 0.5)
                : HandwritingStyle.DefaultPenWidth;

            var points = new List<Point>(stroke.Points.Count);
            var radii = new List<double>(stroke.Points.Count);
            for (var index = 0; index < stroke.Points.Count; index++)
            {
                var point = stroke.Points[index];
                points.Add(new Point(area.X + point.X * area.Width, area.Y + point.Y * area.Height));
                radii.Add(Math.Max(baseWidth * point.WidthScale / 2, 0.25));
            }

            // 单点笔画 = 一个圆点：画多边形需要有方向。
            if (points.Count == 1)
            {
                context.DrawEllipse(brush, null, points[0], radii[0], radii[0]);
                continue;
            }

            SmoothRadii(radii);
            SmoothRadii(radii);
            DrawTaperedStroke(context, brush, points, radii, baseWidth);
        }
    }

    /// <summary>
    ///     沿中心线画一条粗细变化的带子：按点的法线往两侧各推半个笔宽，围成一个多边形。
    ///     <para>
    ///         <b>笔锋是「尖」的</b>：两端各留一小段收锋区，笔宽按幂函数收到接近 0，
    ///         多边形自己收到一个点——不用圆帽（圆头看起来像棉签，不像笔）。
    ///         中间的粗细跟书写速度走（慢粗快细）。
    ///     </para>
    /// </summary>
    private static void DrawTaperedStroke(
        DrawingContext context,
        IBrush brush,
        List<Point> points,
        List<double> radii,
        double baseWidth)
    {
        var count = points.Count;
        if (count < 2)
            return;

        // 累计弧长：收锋区按「走了多远」算，而不是按点数，快慢不同的段落看起来才一致。
        var lengths = new double[count];
        for (var index = 1; index < count; index++)
        {
            var dx = points[index].X - points[index - 1].X;
            var dy = points[index].Y - points[index - 1].Y;
            lengths[index] = lengths[index - 1] + Math.Sqrt(dx * dx + dy * dy);
        }

        var total = lengths[count - 1];
        if (total < 0.5)
        {
            // 几乎没动的一笔：画个圆点，别画成一条看不见的细缝。
            var radius = Math.Max(radii[0], 0.5);
            context.DrawEllipse(brush, null, points[0], radius, radius);
            return;
        }

        // 收锋长度：太短不尖，太长整笔都在收；大致 2.5 倍笔宽，最多占全长的 35%。
        var tipLength = Math.Clamp(baseWidth * 2.5, 4, Math.Max(4, total * 0.35));

        var widths = new double[count];
        for (var index = 0; index < count; index++)
        {
            var fromStart = Math.Clamp(lengths[index] / tipLength, 0, 1);
            var fromEnd = Math.Clamp((total - lengths[index]) / tipLength, 0, 1);

            // 平方根曲线：离开端点时快速长粗，到收锋尽头自然接回正常笔宽。
            var envelope = Math.Sqrt(Math.Min(fromStart, fromEnd));
            widths[index] = Math.Max(radii[index] * envelope, 0.08);
        }

        var left = new Point[count];
        var right = new Point[count];

        // 上一段的有效方向：重合点没有方向时沿用，免得在那里戳出一个洞。
        var normalX = 0.0;
        var normalY = 0.0;

        for (var index = 0; index < count; index++)
        {
            var previous = points[Math.Max(index - 1, 0)];
            var next = points[Math.Min(index + 1, count - 1)];

            var direction = next - previous;
            var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length >= 1e-6)
            {
                normalX = -direction.Y / length;
                normalY = direction.X / length;
            }

            left[index] = new Point(points[index].X + normalX * widths[index], points[index].Y + normalY * widths[index]);
            right[index] = new Point(points[index].X - normalX * widths[index], points[index].Y - normalY * widths[index]);
        }

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(left[0], true);
            for (var index = 1; index < count; index++)
                geometryContext.LineTo(left[index], true);

            geometryContext.LineTo(right[count - 1], true);
            for (var index = count - 2; index >= 0; index--)
                geometryContext.LineTo(right[index], true);

            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geometry);
    }
    /// <summary>
    ///     把笔宽磨一下：速度算出来的倍率有抖动，直接画会出现一粗一细的疙瘩；
    ///     取相邻三点平均，看起来才是自然的提按（两端的收锋由画的时候再叠一层包络）。
    /// </summary>
    private static void SmoothRadii(List<double> radii)
    {
        if (radii.Count < 3)
            return;

        var source = radii.ToArray();
        for (var index = 1; index < radii.Count - 1; index++)
        {
            radii[index] = (source[index - 1] + source[index] * 2 + source[index + 1]) / 4;
        }
    }
}