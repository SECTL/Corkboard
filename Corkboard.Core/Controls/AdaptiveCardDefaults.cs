

namespace Corkboard.Core.Controls;

/// <summary>
///     自适应区块面板的排布默认值：面板的宽度上下限从这里取，板子语义（<c>BoardBlockStyle</c>）
///     再把它们当作自己的默认值用。这样面板本身不认识「板子」，换个场景也能复用。
/// </summary>
public static class AdaptiveCardDefaults
{
    /// <summary>没设过最小宽度时用的区块宽度（DIP）。</summary>
    public const double DefaultItemWidth = 300;

    /// <summary>区块宽度上限相对最小宽度的倍数。</summary>
    public const double MaxWidthRatio = 1.4;
}