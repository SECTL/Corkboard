namespace Corkboard.Core.Controls;

/// <summary>
///     自适应区块排布的「分几列、每块多宽、每块落在哪」计算。
///     <para>
///         先按最小区块宽算出这一排能塞下几列（至少一列、至多就这么几块），再把可用宽度平分给
///         这些区块——于是区块宽度随窗口宽度连续变化，不会排到一半突然换行，也不会定死在某个宽度上
///         让窄窗口横向溢出。最后用最大区块宽兜住上限，免得窗口很宽、区块又少时把余量全灌给一块。
///     </para>
///     <para>纯计算、不碰 UI，有单测（见 <c>AdaptiveGridMetricsTests</c>）。</para>
/// </summary>
public static class AdaptiveGridMetrics
{
    /// <summary>算出列数与单个区块的宽度。</summary>
    /// <param name="availableWidth">可用宽度（DIP）。量不到（NaN / 非正）时退回一列、取最小宽度。</param>
    /// <param name="minItemWidth">单个区块的宽度下限，一排最多排几列由它决定。</param>
    /// <param name="maxItemWidth">单个区块的宽度上限。</param>
    /// <param name="itemCount">要排的区块总数。</param>
    /// <param name="spacing">同一排里相邻两块之间的间距。</param>
    public static (int Columns, double ItemWidth) Resolve(
        double availableWidth, double minItemWidth, double maxItemWidth, int itemCount, double spacing)
    {
        var min = Math.Max(1d, minItemWidth);
        var max = Math.Max(min, maxItemWidth);
        var gap = Math.Max(0d, spacing);

        if (itemCount <= 0)
            return (0, min);

        // 外层不给宽度约束：一排排完，每块取下限（等真的量到宽度再重排）。
        if (double.IsPositiveInfinity(availableWidth))
            return (itemCount, min);

        // 还没量到宽度：先按一块最小宽度报，别报 0 把自己量没了。
        if (double.IsNaN(availableWidth) || availableWidth <= 0d)
            return (1, min);

        // n 块占 n * 宽 + (n - 1) * 间距，反解出这一排放得下几块。
        var columns = (int)Math.Floor((availableWidth + gap) / (min + gap));
        columns = Math.Clamp(columns, 1, itemCount);

        var width = (availableWidth - ((columns - 1) * gap)) / columns;
        width = Math.Clamp(width, min, max);

        // 窗口比一块的下限还窄时宁可把区块压窄，也不横向溢出被裁掉。
        return (columns, Math.Min(width, availableWidth));
    }

    /// <summary>
    ///     错落排布的落位：逐块放进当前最矮的那一列（几列一样高时靠左优先）。
    ///     <para>
    ///         区块高度是各自内容的高度，按「一行一行对齐」排会在矮的那块下面空出一条横向空白；
    ///     按最矮列落位就是让后面的区块往空出来的地方补，于是每行不必对齐，页面上也不会留下
    ///     为了凑齐一行而空着的位置。
    ///     </para>
    /// </summary>
    /// <param name="itemHeights">每一块量出来的高度（DIP），顺序就是排列顺序。</param>
    /// <param name="columns">列数，小于 1 时按一列处理。</param>
    /// <param name="lineSpacing">同一列里上下两块之间的间距。</param>
    /// <returns>每一块占的列号与纵向起点（DIP）。</returns>
    public static (int Column, double Y)[] ResolveMasonry(
        IReadOnlyList<double>? itemHeights, int columns, double lineSpacing)
    {
        if (itemHeights is null || itemHeights.Count == 0)
            return [];

        var count = itemHeights.Count;
        var placements = new (int Column, double Y)[count];

        // 一块都不落空：列数比块数还多时多出来的列不会有人放，直接按块数收窄。
        var columnCount = Math.Clamp(columns, 1, count);
        var gap = double.IsFinite(lineSpacing) && lineSpacing > 0d ? lineSpacing : 0d;
        var bottoms = new double[columnCount];

        for (var index = 0; index < count; index++)
        {
            var column = 0;
            for (var candidate = 1; candidate < columnCount; candidate++)
            {
                if (bottoms[candidate] < bottoms[column])
                    column = candidate;
            }

            // 空列直接贴顶，不凭空留一段间距；下面已经有块了才留间距。
            var y = bottoms[column] <= 0d ? 0d : bottoms[column] + gap;
            placements[index] = (column, y);
            bottoms[column] = y + NormalizeHeight(itemHeights[index]);
        }

        return placements;
    }

    /// <summary>高度量不到（NaN / 负数）时当 0 处理：否则一列的高度会变成 NaN，把整页的排布都带歪。</summary>
    private static double NormalizeHeight(double height) =>
        double.IsFinite(height) && height > 0d ? height : 0d;
}
