namespace Corkboard.Core.Controls;

/// <summary>
///     弹层卡片的拖动范围：整张卡片能被挪多远、到边界怎么夹。
///     <para>
///         卡片的布局位置是居中定死的（<c>Border.sheet</c> 的对齐），拖动只在它身上叠一层位移，
///         布局不动——所以「能挪多远」必须自己算。规矩是<b>把卡片整张留在宿主里</b>；
///         卡片比宿主还大时反过来，让卡片始终盖住宿主。不夹的话用户能把卡片拖出视野、再也抓不回来
///         （它没有系统标题栏，也没有别的归位入口）。
///     </para>
///     <para>
///         拖动时夹还不够：卡片被拉大、宿主被缩小都会让原来的位移越界，
///         所以 <c>BoardAssignmentForm</c> 在卡片尺寸或宿主尺寸一变时会拿它再夹一次。
///     </para>
///     <para>纯计算、不碰 UI，有单测（见 <c>SheetDragLimitsTests</c>）。</para>
/// </summary>
public static class SheetDragLimits
{
    /// <summary>
    ///     算某一个轴上的合法位移。
    /// </summary>
    /// <param name="offset">想要的位移（相对卡片不含位移时的位置）。</param>
    /// <param name="basePosition">卡片不含位移时在该轴上的位置（宿主坐标系）。</param>
    /// <param name="cardSize">卡片在该轴上的尺寸。</param>
    /// <param name="available">宿主在该轴上的可用尺寸。</param>
    /// <returns>夹进合法区间后的位移。</returns>
    public static double ClampOffset(double offset, double basePosition, double cardSize, double available)
    {
        // 区间的两个端点：卡片近边贴宿主近边、卡片远边贴宿主远边。
        // 卡片比宿主大时端点前后颠倒，min/max 兜住方向——Math.Clamp 要求 low <= high。
        var near = -basePosition;
        var far = available - cardSize - basePosition;
        var min = Math.Min(near, far);
        var max = Math.Max(near, far);

        // 宿主还没量出来（ClientSize 为 0）或量到 NaN 时退化成「不许动」，
        // 总比让卡片飞出去强。
        if (double.IsNaN(min) || double.IsNaN(max) || min > max)
            return 0d;

        return Math.Clamp(offset, min, max);
    }
}
