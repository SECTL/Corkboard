using Avalonia;
using Avalonia.Media;
using Corkboard.Core.Handwriting;

namespace Corkboard.Core.Controls;

/// <summary>
///     把一页手写画到 <see cref="DrawingContext" /> 上：可编辑画布与只读预览共用这一份，
///     免得两边各写一套「笔怎么画」的代码、修了一边漏一边。
///     <para>
///         <b>笔锋 = 速度笔锋</b>：每个点带一个粗细倍率（<see cref="HandwritingPoint.WidthScale" />，
///         按书写速度算：慢写粗、快写细）。所以一笔不再是一根等宽折线，而是沿中心线左右各推出半个笔宽的多边形；
///         走快的一段自然变细、走慢的一段自然变粗，中间是<b>沿弧长渐变</b>的，不是到笔尾才突然收一下。
///         两端一律补一个整宽的圆帽（就像笔尖落在纸上、提起来各留下一个圆点），
///         <b>不做「收锋包络」</b>——在笔尾强行收细会画出一根尖刺，真实笔迹的收笔是靠手在提笔前
///         自然减速变粗/加速变细带出来的，那个渐变已经在倍率里了。
///         没有笔锋数据的老笔迹倍率都是 1，画出来就是等宽线 + 圆头。
///     </para>
///     <para>
///         笔宽是「参考画布上的 DIP」：用户挑的「4 像素粗」在写的时候就是 4 像素粗。
///         <b>但画到别的尺寸上（卡片预览、导出）时笔宽必须跟着整页一起缩放</b>：
///         <see cref="Render" /> 按 <see cref="HandwritingDocument.ResolveRenderScale" /> 换算，
///         否则 661 DIP 宽的手写缩到 300 DIP 宽的卡片上时笔宽还是原样，笔画相对字会粗一倍，
///         字与字的间隙被墨吃掉（用户反馈过「写的时候间隔正常，卡片上挤在一起」）。
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

        // 预览把整页按卡片宽度缩放，笔宽要乘同一个比例（编辑画布按参考尺寸 1:1 画，比例是 1）。
        var strokeScale = document.ResolveRenderScale(area.Width);

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

            // 笔宽 = 用户挑的粗细 × 整页缩放比例；最细也留 0.5 DIP 的可视下限，
            // 卡片缩得很小时细笔不会变成看不见的一条影子。
            var baseWidth = (double.IsFinite(stroke.Width)
                    ? Math.Max(stroke.Width, 0.5)
                    : HandwritingStyle.DefaultPenWidth) * strokeScale;
            baseWidth = Math.Max(baseWidth, 0.5);

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
            DrawStroke(context, brush, points, radii);
        }
    }

    /// <summary>
    ///     沿中心线画一条粗细变化的带子：按每点的边缘方向往两侧各推半个笔宽，围成一个闭合多边形，
    ///     再在两段端头各补一个整宽的圆点当圆帽。
    ///     <para>
    ///         <b>这里不做任何「收锋」</b>：笔迹的粗细变化全部来自 <paramref name="radii" />
    ///         （也就是书写速度算出来的 <see cref="HandwritingPoint.WidthScale" />）。
    ///         在笔尾再叠一层收锋包络的话，笔宽会在最后十几个像素里从全宽掉到很细，
    ///         看起来就是一根尖刺——真笔的收笔是手在提笔前慢慢减速带出来的渐变，不是几何上的一刀切。
    ///     </para>
    /// </summary>
    private static void DrawStroke(DrawingContext context, IBrush brush, List<Point> points, List<double> radii)
    {
        var count = points.Count;
        if (count < 2)
            return;

        // 几乎没动的一笔：画个圆点，别画成一条看不见的细缝。
        var total = 0.0;
        for (var index = 1; index < count; index++)
            total += Distance(points[index - 1], points[index]);
        if (total < 0.5)
        {
            var dotRadius = Math.Max(radii[0], 0.5);
            context.DrawEllipse(brush, null, points[0], dotRadius, dotRadius);
            return;
        }

        // 笔迹做成「一条连续的带状多边形」：左沿顺着走一遍、右沿反着走回来、首尾闭合。
        // ⚠️ 不要退回「一段一个梯形 + 每个顶点一个圆盘」那套：那些子路径互相重叠，
        // 而重叠的子路径在填充时会被当成内外翻转，重叠区被挖成空洞——放大后整条笔迹
        // 变成一串珠子、边缘一圈缺口（用户说的「尾巴很奇怪」就是它，实测两种绕向都一样）。
        // 一条不自交的闭合多边形没有这个问题；圆头改由两端单独补一个椭圆（叠着画，不参与翻转）。
        var edges = BuildEdgeOffsets(points, count);
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.SetFillRule(FillRule.NonZero);

            geometryContext.BeginFigure(Offset(points[0], edges[0], radii[0]), true);
            for (var index = 1; index < count; index++)
                geometryContext.LineTo(Offset(points[index], edges[index], radii[index]), true);
            for (var index = count - 1; index >= 0; index--)
                geometryContext.LineTo(Offset(points[index], edges[index], -radii[index]), true);
            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geometry);

        // 两端圆帽（笔尖落纸、提笔各留下一个圆点）。单独画，叠着覆盖，不参与多边形的翻转判断。
        if (radii[0] > 0.05)
            context.DrawEllipse(brush, null, points[0], radii[0], radii[0]);
        if (radii[count - 1] > 0.05)
            context.DrawEllipse(brush, null, points[count - 1], radii[count - 1], radii[count - 1]);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    ///     每个点的边缘偏移方向：取相邻两段方向的角平分线（斜接），再乘一个斜接长度系数。
    ///     拐得越急、外沿越要往外伸，所以系数限制上限，免得太尖的转折处内外沿交叉、带子翻出去。
    /// </summary>
    private static Point[] BuildEdgeOffsets(List<Point> points, int count)
    {
        const double miterLimit = 3.0;
        var offsets = new Point[count];

        for (var index = 0; index < count; index++)
        {
            double dx, dy;
            if (index == 0)
            {
                dx = points[1].X - points[0].X;
                dy = points[1].Y - points[0].Y;
            }
            else if (index == count - 1)
            {
                dx = points[count - 1].X - points[count - 2].X;
                dy = points[count - 1].Y - points[count - 2].Y;
            }
            else
            {
                dx = points[index + 1].X - points[index - 1].X;
                dy = points[index + 1].Y - points[index - 1].Y;
            }

            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-9)
            {
                offsets[index] = index > 0 ? offsets[index - 1] : default;
                continue;
            }

            var normalX = -dy / length;
            var normalY = dx / length;

            var miter = 1.0;
            if (index > 0 && index + 1 < count)
            {
                var ax = points[index].X - points[index - 1].X;
                var ay = points[index].Y - points[index - 1].Y;
                var bx = points[index + 1].X - points[index].X;
                var by = points[index + 1].Y - points[index].Y;
                var firstLength = Math.Sqrt(ax * ax + ay * ay);
                var secondLength = Math.Sqrt(bx * bx + by * by);
                if (firstLength > 1e-9 && secondLength > 1e-9)
                {
                    var cosine = Math.Clamp((ax * bx + ay * by) / (firstLength * secondLength), -1, 1);
                    var halfAngleCosine = Math.Sqrt((1 + cosine) / 2);
                    if (halfAngleCosine > 1e-6)
                        miter = Math.Clamp(1 / halfAngleCosine, 1, miterLimit);
                }
            }

            offsets[index] = new Point(normalX * miter, normalY * miter);
        }

        return offsets;
    }

    /// <summary>按「中心点 + 偏移方向 × 半宽」算带子的边沿点。</summary>
    private static Point Offset(Point center, Point edge, double halfWidth)
    {
        return new Point(center.X + edge.X * halfWidth, center.Y + edge.Y * halfWidth);
    }

    /// <summary>
    ///     把笔宽磨一下：速度算出来的倍率有抖动，直接画会出现一粗一细的疙瘩；
    ///     取相邻三点平均，看起来才是自然的提按（笔锋的渐变本来就在倍率里，这里只去毛刺）。
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


