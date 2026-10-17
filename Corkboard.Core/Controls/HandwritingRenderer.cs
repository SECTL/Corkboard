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

        // 收锋长度：太短不尖，太长整笔都在收、粗笔看起来就像拖把。取 1.6 倍笔宽，
        // 最多占全长的 22%——粗笔的端头不会收成一大片斜边。
        var tipLength = Math.Clamp(baseWidth * 1.6, 3, Math.Max(3, total * 0.22));

        var widths = new double[count];
        for (var index = 0; index < count; index++)
        {
            var fromStart = Math.Clamp(lengths[index] / tipLength, 0, 1);
            var fromEnd = Math.Clamp((total - lengths[index]) / tipLength, 0, 1);

            // 平方根曲线：离开端点时快速长粗，到收锋尽头自然接回正常笔宽。
            var envelope = Math.Sqrt(Math.Min(fromStart, fromEnd));

            // 最细处也留 0.35 DIP：收锋要尖，但不能细成一根看不见的头发丝。
            widths[index] = Math.Clamp(radii[index] * envelope, 0.35, radii[index] * 1.6);
        }

        // 粗笔时把中心线往两侧推成「一条带子」的多边形会自交：笔越粗、拐得越急，交叉越厉害，
        // 边缘就出现毛刺、缺口和「毛笔分叉」那种脏边（用户反馈过）。
        // 所以改成「一段一个梯形 + 每个顶点一个圆盘」：梯形永远是凸的、不自交，
        // 顶点圆盘把两段之间的接缝补圆（等于圆角连接），两者用 NonZero 填充合成一条笔迹。
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.SetFillRule(FillRule.NonZero);

            for (var index = 0; index + 1 < count; index++)
            {
                var startPoint = points[index];
                var endPoint = points[index + 1];
                var dx = endPoint.X - startPoint.X;
                var dy = endPoint.Y - startPoint.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-6)
                    continue;

                // 这一段的法线只用这一段自己的方向：不跟相邻段平均，梯形才是凸的。
                var normalX = -dy / length;
                var normalY = dx / length;
                var startWidth = widths[index];
                var endWidth = widths[index + 1];

                geometryContext.BeginFigure(
                    new Point(startPoint.X + normalX * startWidth, startPoint.Y + normalY * startWidth), true);
                geometryContext.LineTo(
                    new Point(endPoint.X + normalX * endWidth, endPoint.Y + normalY * endWidth), true);
                geometryContext.LineTo(
                    new Point(endPoint.X - normalX * endWidth, endPoint.Y - normalY * endWidth), true);
                geometryContext.LineTo(
                    new Point(startPoint.X - normalX * startWidth, startPoint.Y - normalY * startWidth), true);
                geometryContext.EndFigure(true);
            }

            // 顶点圆盘：补上接缝和起止端头。半径跟着这一点的实际笔宽走，所以收锋处自然收成尖点。
            for (var index = 0; index < count; index++)
                AppendDisc(geometryContext, points[index], widths[index]);
        }

        context.DrawGeometry(brush, null, geometry);
    }

    /// <summary>
    ///     在 <paramref name="center" /> 处补一个实心圆（多边形近似），用来做圆角连接与端头。
    ///     分段数按半径定：细笔 10 段就够，粗笔才需要更多段，免得边缘看出棱角。
    /// </summary>
    private static void AppendDisc(StreamGeometryContext context, Point center, double radius)
    {
        if (!double.IsFinite(radius) || radius <= 0)
            return;

        var segments = Math.Clamp((int)Math.Ceiling(radius * 3) + 8, 10, 48);
        var step = Math.PI * 2 / segments;
        var normalX = 0.0;
        var normalY = 0.0;

        for (var index = 0; index < segments; index++)
        {
            var angle = index * step;
            normalX = Math.Cos(angle);
            normalY = Math.Sin(angle);

            var point = new Point(center.X + normalX * radius, center.Y + normalY * radius);
            if (index == 0)
                context.BeginFigure(point, true);
            else
                context.LineTo(point, true);
        }

        context.EndFigure(true);
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