namespace Corkboard.Core.Handwriting;

/// <summary>
///     笔迹上的一个点。坐标是归一化的：<see cref="X" /> / <see cref="Y" /> 都在 0..1 之间，
///     相对画布的宽 / 高，所以画布（预览、卡片、表单）换多宽都不会把字拉丢。
///     <para>
///         用结构体而不是类：一页手写可能有上千个点，每个点一个对象太浪费。
///         坐标在创建时就夹进 0..1（<see cref="NaN" /> / 无穷这类坏值按 0 处理），
///         所以拿到的点永远能直接画。
///     </para>
///     <para>
///         <see cref="Width" /> 是这个点的「笔锋」粗细倍率（相对这一笔的 <c>HandwritingStroke.Width</c>）：
///         画的时候按速度算出来，慢写粗、快写细，0.45–1.4 之间。旧数据没有这一项，
///         读出来就是 1（等宽），所以老笔迹和新笔迹能一起画。
///     </para>
/// </summary>
public readonly record struct HandwritingPoint(double X, double Y)
{
    /// <summary>笔锋粗细倍率的默认值：1 = 与这一笔的笔宽一样粗（旧数据都按这个画）。</summary>
    public const double DefaultWidthScale = 1;

    /// <summary>笔锋倍率的下限：再细就细成一条线了。</summary>
    public const double MinWidthScale = 0.4;

    /// <summary>笔锋倍率的上限：再粗就糊成一团了。</summary>
    public const double MaxWidthScale = 1.5;

    /// <summary>
    ///     坐标上限（归一化单位）。正常情况下坐标在 0..1，但用户把内容框拖大之后
    ///     还能往新露出来的地方继续写，那部分坐标就会大于 1——上限留一点余量，
    ///     不能把它们夹回 1 堆在边上（那样笔迹会「跳位置」）。
    /// </summary>
    public const double MaxCoordinate = 4;

    private readonly double _widthScale = DefaultWidthScale;

    /// <summary>这个点的笔锋粗细倍率（1 = 等宽）。</summary>
    public double WidthScale
    {
        get => double.IsFinite(_widthScale) ? Math.Clamp(_widthScale, MinWidthScale, MaxWidthScale) : DefaultWidthScale;
        init => _widthScale = double.IsFinite(value) ? Math.Clamp(value, MinWidthScale, MaxWidthScale) : DefaultWidthScale;
    }

    /// <summary>按归一化坐标建一个点，顺手夹进合法区间（等宽）。</summary>
    public static HandwritingPoint Create(double x, double y)
    {
        return new HandwritingPoint(ClampCoordinate(x), ClampCoordinate(y));
    }

    /// <summary>按归一化坐标与笔锋倍率建一个点；倍率会夹进允许范围。</summary>
    public static HandwritingPoint Create(double x, double y, double widthScale)
    {
        return new HandwritingPoint(ClampCoordinate(x), ClampCoordinate(y)) { WidthScale = widthScale };
    }

    /// <summary>把坐标夹进合法区间；非有限值（<see cref="double.NaN" /> / 无穷）按 0 处理。</summary>
    public static double ClampCoordinate(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, 0, MaxCoordinate) : 0;
    }

    /// <summary>两点之间的线性插值（都在归一化坐标系里，笔锋倍率也一起插值）。</summary>
    public static HandwritingPoint Lerp(HandwritingPoint from, HandwritingPoint to, double progress)
    {
        return new HandwritingPoint(
            from.X + (to.X - from.X) * progress,
            from.Y + (to.Y - from.Y) * progress)
        {
            WidthScale = from.WidthScale + (to.WidthScale - from.WidthScale) * progress
        };
    }

    /// <summary>返回夹过坐标的自己（正常创建的点已经是夹过的，只有外部直接 new 时才需要再夹一次）。</summary>
    public HandwritingPoint Clamped()
    {
        return Create(X, Y, WidthScale);
    }
}
