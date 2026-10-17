namespace Corkboard.Core.Handwriting;

/// <summary>
///     橡皮的擦除逻辑（纯计算、有单测）：把已有笔迹里被圆形橡皮扫到的部分剪掉，
///     剩下的碎段各自成为新的一笔。
///     <para>
///         <b>橡皮不自己存成一笔</b>：擦除的产物是「被剪短的、真的少了的墨」，
///         而不是一笔画上去盖住看不见的线。否则预览与导出时会露出擦不掉的墨——
///         预览只认识「笔画 + 颜色 + 宽度」，根本没有「擦除」这个概念。
///     </para>
///     <para>
///         笔画坐标是归一化的，而橡皮半径是 DIP，所以这里要带着画布宽高换算成同一套坐标；
///         命中阈值是「橡皮半径 + 半个笔宽」：粗笔画的边缘也算在笔迹里。
///     </para>
/// </summary>
public static class HandwritingEraser
{
    /// <summary>一个像素点（内部计算用；不引 Avalonia 的 Point 是为了这层保持纯计算）。</summary>
    private readonly record struct PixelPoint(double X, double Y);

    /// <summary>
    ///     擦掉落在圆里的笔迹。
    ///     <paramref name="centerX" /> / <paramref name="centerY" /> 是归一化的圆心，
    ///     <paramref name="radius" /> 是像素半径（橡皮粗细的一半），
    ///     <paramref name="canvasWidth" /> / <paramref name="canvasHeight" /> 是当前画布尺寸。
    ///     返回处理后的新笔画列表（原列表不动）；<paramref name="changed" /> 表示真的擦掉了东西。
    /// </summary>
    public static List<HandwritingStroke> Erase(
        IReadOnlyList<HandwritingStroke> strokes,
        double centerX,
        double centerY,
        double radius,
        double canvasWidth,
        double canvasHeight,
        out bool changed)
    {
        changed = false;

        var result = new List<HandwritingStroke>(strokes.Count);

        // 参数不成立时原样返回，别在除零 / NaN 里算出乱七八糟的坐标。
        if (!double.IsFinite(radius) || radius <= 0
            || !double.IsFinite(canvasWidth) || canvasWidth <= 0
            || !double.IsFinite(canvasHeight) || canvasHeight <= 0
            || !double.IsFinite(centerX) || !double.IsFinite(centerY))
        {
            result.AddRange(strokes);
            return result;
        }

        var centerPixelX = centerX * canvasWidth;
        var centerPixelY = centerY * canvasHeight;

        foreach (var stroke in strokes)
        {
            // 橡皮自己的历史记录与空笔画原样留着（它们本来就不参与渲染）。
            if (stroke.Erase || stroke.Points.Count == 0)
            {
                result.Add(stroke);
                continue;
            }

            var hitRadius = radius + Math.Max(0, double.IsFinite(stroke.Width) ? stroke.Width : 0) / 2;

            var pixels = new List<PixelPoint>(stroke.Points.Count);
            foreach (var point in stroke.Points)
                pixels.Add(new PixelPoint(point.X * canvasWidth, point.Y * canvasHeight));

            if (pixels.Count == 1)
            {
                // 单点笔画（一个圆点）：在圆里就整笔没了。
                if (DistanceSquared(pixels[0], centerPixelX, centerPixelY) <= hitRadius * hitRadius)
                    changed = true;
                else
                    result.Add(stroke);

                continue;
            }

            var pieces = new List<List<PixelPoint>>();
            List<PixelPoint>? current = null;
            var strokeHit = false;

            // 收尾：凑够两个点的段才算一笔（一个点画不出线，会变成看不见的碎渣）。
            void Flush()
            {
                if (current is { Count: >= 2 })
                    pieces.Add(current);

                current = null;
            }

            for (var index = 0; index + 1 < pixels.Count; index++)
            {
                var start = pixels[index];
                var end = pixels[index + 1];

                if (!TryGetHitInterval(start, end, centerPixelX, centerPixelY, hitRadius, out var enter, out var exit))
                {
                    if (current is null)
                        current = [start];

                    current.Add(end);
                    continue;
                }

                strokeHit = true;

                var hitStart = Lerp(start, end, enter);
                var hitEnd = Lerp(start, end, exit);

                // 进圆之前那一段：从这一段的起点保到与圆的交点。
                if (enter > 0)
                {
                    if (current is null)
                        current = [start];

                    current.Add(hitStart);
                    Flush();
                }
                else
                {
                    // 起点就在圆里：前面攒着的那一段到此为止。
                    Flush();
                }

                // 出圆之后那一段：从与圆的交点保到这一段的终点。
                if (exit < 1)
                    current = [hitEnd, end];
            }

            Flush();

            if (!strokeHit)
            {
                result.Add(stroke);
                continue;
            }

            changed = true;

            foreach (var piece in pieces)
            {
                if (piece.Count < 2)
                    continue;

                var normalized = new List<HandwritingPoint>(piece.Count);
                foreach (var point in piece)
                    normalized.Add(HandwritingPoint.Create(point.X / canvasWidth, point.Y / canvasHeight));

                result.Add(new HandwritingStroke
                {
                    Color = stroke.Color,
                    Width = stroke.Width,
                    Erase = stroke.Erase,
                    Points = normalized
                });
            }
        }

        return result;
    }

    /// <summary>
    ///     线段与圆相交的那一段在 [0, 1] 参数区间里的范围（0 = 起点，1 = 终点）。
    ///     返回 <see langword="false" /> 表示这一段没有被圆碰到。
    /// </summary>
    private static bool TryGetHitInterval(
        PixelPoint start,
        PixelPoint end,
        double centerX,
        double centerY,
        double radius,
        out double enter,
        out double exit)
    {
        enter = 0;
        exit = 0;

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        var offsetX = start.X - centerX;
        var offsetY = start.Y - centerY;
        var constant = offsetX * offsetX + offsetY * offsetY - radius * radius;

        // 退化线段（两个点重合）：当成一个点判断在不在圆里。
        if (lengthSquared <= double.Epsilon)
        {
            if (constant > 0)
                return false;

            exit = 1;
            return true;
        }

        var linear = 2 * (offsetX * dx + offsetY * dy);
        var discriminant = linear * linear - 4 * lengthSquared * constant;

        // 相切（判别式为 0）不算擦到：差之毫厘的墨留着，比吃掉一整笔强。
        if (discriminant <= 0)
            return false;

        var root = Math.Sqrt(discriminant);
        var inverse = 1 / (2 * lengthSquared);
        var first = (-linear - root) * inverse;
        var second = (-linear + root) * inverse;

        enter = Math.Max(first, 0);
        exit = Math.Min(second, 1);
        return enter <= exit;
    }

    private static PixelPoint Lerp(PixelPoint start, PixelPoint end, double progress)
    {
        return new PixelPoint(
            start.X + (end.X - start.X) * progress,
            start.Y + (end.Y - start.Y) * progress);
    }

    private static double DistanceSquared(PixelPoint point, double x, double y)
    {
        var dx = point.X - x;
        var dy = point.Y - y;
        return dx * dx + dy * dy;
    }
}
