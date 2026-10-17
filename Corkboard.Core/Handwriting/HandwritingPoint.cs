namespace Corkboard.Core.Handwriting;

/// <summary>
///     笔迹上的一个点。坐标是归一化的：<see cref="X" /> / <see cref="Y" /> 都在 0..1 之间，
///     相对画布的宽 / 高，所以画布（预览、卡片、表单）换多宽都不会把字拉丢。
///     <para>
///         用结构体而不是类：一页手写可能有上千个点，每个点一个对象太浪费。
///         坐标在创建时就夹进 0..1（<see cref="NaN" /> / 无穷这类坏值按 0 处理），
///         所以拿到的点永远能直接画。
///     </para>
/// </summary>
public readonly record struct HandwritingPoint(double X, double Y)
{
    /// <summary>按归一化坐标建一个点，顺手夹进 0..1。</summary>
    public static HandwritingPoint Create(double x, double y)
    {
        return new HandwritingPoint(Clamp01(x), Clamp01(y));
    }

    /// <summary>把坐标夹进 0..1；非有限值（<see cref="double.NaN" /> / 无穷）按 0 处理。</summary>
    public static double Clamp01(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    }

    /// <summary>两点之间的线性插值（都在归一化坐标系里）。</summary>
    public static HandwritingPoint Lerp(HandwritingPoint from, HandwritingPoint to, double progress)
    {
        return new HandwritingPoint(
            from.X + (to.X - from.X) * progress,
            from.Y + (to.Y - from.Y) * progress);
    }

    /// <summary>返回夹过坐标的自己（正常创建的点已经是夹过的，只有外部直接 new 时才需要再夹一次）。</summary>
    public HandwritingPoint Clamped()
    {
        return Create(X, Y);
    }
}
