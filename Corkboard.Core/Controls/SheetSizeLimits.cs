namespace Corkboard.Core.Controls;

/// <summary>
///     弹层卡片的尺寸上限：卡片整体居中，宽高都留出余量，不顶到边、也不长成一条超长的板子。
///     <para>
///         高度 = <b>可用高度 × 比例</b>（比例上限是主约束：屏幕／窗口再高，卡片也只占七成，
///         上下各留一成半的余量），低于下限时托到下限。宽度 = <b>设计上限</b> 与
///         「可用宽度 − 留边」取小，再兜住下限。
///     </para>
///     <para>
///         「可用高度」由调用方给，因为两种承载形态量到的东西不一样：
///         壳内弹层用宿主窗口的内容区（卡片只留在<b>看得见</b>的地方，窗口小也不会被裁掉一半），
///         独立顶层窗口用屏幕工作区（那个窗口是 <c>SizeToContent</c> 的，量它自己会和卡片互相追着长）。
///     </para>
///     <para>纯计算、不碰 UI，有单测（见 <c>SheetSizeLimitsTests</c>）。</para>
/// </summary>
public static class SheetSizeLimits
{
    /// <summary>
    ///     算高度上限。<paramref name="available" /> 量不到（<c>0</c> / <c>NaN</c> / 无穷）时只兜下限，
    ///     绝不返回负值或 <c>NaN</c>——弹层显示这一步不该因为量不到屏幕就抛出去。
    /// </summary>
    /// <param name="available">可用高度（DIP）。</param>
    /// <param name="ratio">卡片最多能占可用高度的比例（0～1）。</param>
    /// <param name="minHeight">卡片高度下限（DIP）。</param>
    public static double ResolveMaxHeight(double available, double ratio, double minHeight)
    {
        if (!IsUsable(available))
            return minHeight;

        // 下限托底：可用高度比下限还小时卡片会略微溢出，但那总比缩成一条什么都看不清强。
        return Math.Max(minHeight, available * ratio);
    }

    /// <summary>
    ///     算宽度上限：设计上限再收进「可用宽度 − 留边」里，但不低于下限。
    ///     量不到可用宽度时就只用设计上限。
    /// </summary>
    /// <param name="available">可用宽度（DIP）。</param>
    /// <param name="preferred">设计上限（DIP），即窗口够宽时卡片最多长到多宽。</param>
    /// <param name="minWidth">卡片宽度下限（DIP）。</param>
    /// <param name="margin">卡片到可用区域左右边缘一共留的余量（DIP）。</param>
    public static double ResolveMaxWidth(double available, double preferred, double minWidth, double margin)
    {
        if (!IsUsable(available))
            return preferred;

        return Math.Max(minWidth, Math.Min(preferred, available - margin));
    }

    private static bool IsUsable(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }
}
