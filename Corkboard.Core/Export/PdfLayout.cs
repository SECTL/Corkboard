using Avalonia;

namespace Corkboard.Core.Export;

/// <summary>PDF 排版里的块种类。枚举按数字落盘，只能在末尾追加成员。</summary>
public enum PdfBlockKind
{
    /// <summary>科目标题。它可以另起一页，并且不允许和紧随其后的卡片分在两页。</summary>
    SubjectHeader = 0,

    /// <summary>一张作业卡片。</summary>
    Note = 1
}

/// <summary>
///     参与 PDF 排版的一个块。<see cref="Height" /> 是块自己的高度（DIP），宽度由列宽决定，
///     所以这里不需要宽度。
/// </summary>
public readonly record struct PdfBlock(PdfBlockKind Kind, double Height);

/// <summary>
///     一个块在某一页上的落点（DIP，相对页面左上角）。宽度是列宽，高度是块高。
/// </summary>
public readonly record struct PdfPlacement(int BlockIndex, double X, double Y, double Width, double Height);

/// <summary>一页的排版结果：页面尺寸（整页，不是内容区）与该页上所有块的位置。</summary>
public sealed class PdfPageLayout
{
    public PdfPageLayout(double width, double height, IReadOnlyList<PdfPlacement> placements)
    {
        Width = width;
        Height = height;
        Placements = placements ?? [];
    }

    /// <summary>整页宽度（DIP）。</summary>
    public double Width { get; }

    /// <summary>整页高度（DIP）。</summary>
    public double Height { get; }

    /// <summary>该页上的块，按放置顺序（也就是阅读顺序）排列。</summary>
    public IReadOnlyList<PdfPlacement> Placements { get; }
}

/// <summary>
///     PDF 排版的输入参数。非法值（列数越界、边距过大、间距为负、页尺寸非正）不会抛异常，
///     而是按默认值夹取，见 <see cref="PdfLayout.Build" />。
/// </summary>
public sealed class PdfLayoutOptions
{
    /// <summary>整页尺寸（DIP）。必填。</summary>
    public required Size PageSize { get; init; }

    /// <summary>四周留白（DIP）。</summary>
    public double Margin { get; init; }

    /// <summary>列数，夹进 1..4。</summary>
    public int Columns { get; init; }

    /// <summary>列与列之间的间距（DIP）。</summary>
    public double ColumnGap { get; init; }

    /// <summary>同一列里块与块之间的间距（DIP）。</summary>
    public double BlockGap { get; init; }

    /// <summary>每个科目标题是否另起一页。</summary>
    public bool SubjectStartsNewPage { get; init; }
}

/// <summary>
///     PDF 的纯分页排版引擎：输入一串块的高度，输出每页每个块的落点（DIP）。
///     <para>
///         规则（与界面上的「列数」「页边距」设置对应）：
///         <list type="bullet">
///             <item>内容区 = 页尺寸 − 四周边距；列宽 =（内容宽 − 列间距 ×（列数 − 1））/ 列数。</item>
///             <item>从上到下、从左到右按列填（第 1 列填满再第 2 列），列满了换列，所有列都满才换页。</item>
///             <item>一个块绝不跨页、不拆开；块比整列还高时也整块落在列首（溢出部分由调用方裁）。</item>
///             <item>科目标题后面的卡片不能和标题分在两页：放不下「标题 + 紧随卡片」时标题整体挪到下一列 / 下一页。</item>
///             <item>没有块的页不生成；空输入返回空列表。</item>
///         </list>
///     </para>
///     <para>全程确定性、无 IO、无 UI 依赖，只做算术。</para>
/// </summary>
public static class PdfLayout
{
    private const double Epsilon = 1e-6;

    /// <summary>
    ///     把块排到页上。返回的页按先后顺序排列，每页的 <see cref="PdfPageLayout.Placements" />
    ///     按放置顺序排列（<see cref="PdfPlacement.BlockIndex" /> 指的是 <paramref name="blocks" /> 里的下标）。
    /// </summary>
    public static IReadOnlyList<PdfPageLayout> Build(IReadOnlyList<PdfBlock> blocks, PdfLayoutOptions? options)
    {
        var metrics = Resolve(options);

        if (blocks is null || blocks.Count == 0)
            return Array.Empty<PdfPageLayout>();

        var pages = new List<List<PdfPlacement>>();
        List<PdfPlacement>? page = null;
        var column = 0;
        var cursorY = metrics.Margin;
        var columnHasBlocks = false;

        void StartPage()
        {
            page = [];
            pages.Add(page);
            column = 0;
            cursorY = metrics.Margin;
            columnHasBlocks = false;
        }

        void AdvanceColumn()
        {
            // 本页还有下一列就换列；已经是最后一列就换页。
            if (column + 1 < metrics.Columns)
            {
                column++;
                cursorY = metrics.Margin;
                columnHasBlocks = false;
                return;
            }

            StartPage();
        }

        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];

            if (page is null)
                StartPage();

            // 标题另起一页：本页已经有内容时才翻页，第一个块不用白翻一页。
            if (block.Kind == PdfBlockKind.SubjectHeader
                && metrics.SubjectStartsNewPage
                && page!.Count > 0)
            {
                StartPage();
            }

            // 孤立标题保护：把「标题 + 紧随其后的卡片」当成一个整体判断放不放得下。
            var required = RequiredHeight(blocks, index, metrics, columnHasBlocks);

            // 本列放不下就换列 / 换页；换到新列后若还放不下（块比整列还高）就整块溢出放置。
            while (columnHasBlocks && cursorY + required > metrics.Bottom + Epsilon)
            {
                AdvanceColumn();
                required = RequiredHeight(blocks, index, metrics, columnHasBlocks);
            }

            var gap = columnHasBlocks ? metrics.BlockGap : 0;
            var height = NormalizeHeight(block.Height);
            var x = metrics.Margin + column * (metrics.ColumnWidth + metrics.ColumnGap);
            var y = cursorY + gap;

            page!.Add(new PdfPlacement(index, x, y, metrics.ColumnWidth, height));
            cursorY = y + height;
            columnHasBlocks = true;
        }

        var result = new List<PdfPageLayout>(pages.Count);
        foreach (var placements in pages)
            result.Add(new PdfPageLayout(metrics.PageWidth, metrics.PageHeight, placements));

        return result;
    }

    /// <summary>算一列的宽度（DIP）：同一套夹取规则，设置页预览与实际排版用同一个数。</summary>
    public static double GetColumnWidth(PdfLayoutOptions options)
    {
        return Resolve(options).ColumnWidth;
    }

    /// <summary>
    ///     这一块需要多少竖直空间：本列已有内容时含前置间距；标题还可能要算上紧随其后的卡片
    ///     （孤立标题保护）。
    /// </summary>
    private static double RequiredHeight(
        IReadOnlyList<PdfBlock> blocks,
        int index,
        in LayoutMetrics metrics,
        bool columnHasBlocks)
    {
        var height = NormalizeHeight(blocks[index].Height);
        var required = (columnHasBlocks ? metrics.BlockGap : 0) + height;

        if (blocks[index].Kind != PdfBlockKind.SubjectHeader || index + 1 >= blocks.Count)
            return required;

        // 只保护「标题 + 卡片」这一对；后面还是标题的情况留给那个标题自己的判断。
        if (blocks[index + 1].Kind != PdfBlockKind.Note)
            return required;

        var following = NormalizeHeight(blocks[index + 1].Height);
        var pairHeight = height + metrics.BlockGap + following;
        if (pairHeight > metrics.ContentHeight + Epsilon)
            return required;

        return required + metrics.BlockGap + following;
    }

    /// <summary>把选项收拾成能直接算的数值：非法值按默认值夹取，绝不抛异常。</summary>
    private static LayoutMetrics Resolve(PdfLayoutOptions? options)
    {
        var pageSize = options?.PageSize ?? new Size(PdfOptions.A4WidthDip, PdfOptions.A4HeightDip);
        var pageWidth = PositiveOr(pageSize.Width, PdfOptions.A4WidthDip);
        var pageHeight = PositiveOr(pageSize.Height, PdfOptions.A4HeightDip);

        // 边距最多把短边收掉到只剩 2 DIP 内容区；再大就会算出负的列宽。
        var requestedMargin = options?.Margin ?? 0;
        var maxMargin = Math.Max(0, (Math.Min(pageWidth, pageHeight) - 2) / 2);
        var margin = double.IsFinite(requestedMargin) ? Math.Clamp(requestedMargin, 0, maxMargin) : 0;

        var columns = PdfOptions.GetColumnCount(options?.Columns ?? 0);
        var contentWidth = pageWidth - margin * 2;
        var contentHeight = pageHeight - margin * 2;

        // 列间距再大也得给每一列留下宽度，否则列宽会是 0 或负数。
        var requestedColumnGap = options?.ColumnGap ?? 0;
        var columnGap = double.IsFinite(requestedColumnGap) && requestedColumnGap > 0 ? requestedColumnGap : 0;
        var maxTotalGap = columns > 1 ? contentWidth * (columns - 1) / columns : 0;
        var totalGap = Math.Min(columnGap * (columns - 1), maxTotalGap);
        columnGap = columns > 1 ? totalGap / (columns - 1) : 0;
        var columnWidth = Math.Max(0, (contentWidth - totalGap) / columns);

        var requestedBlockGap = options?.BlockGap ?? 0;
        var blockGap = double.IsFinite(requestedBlockGap) && requestedBlockGap > 0 ? requestedBlockGap : 0;

        return new LayoutMetrics(
            pageWidth,
            pageHeight,
            margin,
            columns,
            columnGap,
            blockGap,
            columnWidth,
            contentHeight,
            options?.SubjectStartsNewPage ?? false);
    }

    /// <summary>块高：负数 / NaN 当成 0（宁可挨着排，也不要往页外丢）。</summary>
    private static double NormalizeHeight(double height)
    {
        return double.IsFinite(height) && height > 0 ? height : 0;
    }

    /// <summary>页面尺寸：非正 / NaN 退回给定兜底值。</summary>
    private static double PositiveOr(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0 ? value : fallback;
    }

    /// <summary>排版要用到的全部数值，算一次给整趟用。</summary>
    private readonly record struct LayoutMetrics(
        double PageWidth,
        double PageHeight,
        double Margin,
        int Columns,
        double ColumnGap,
        double BlockGap,
        double ColumnWidth,
        double ContentHeight,
        bool SubjectStartsNewPage)
    {
        /// <summary>内容区底边的 Y 坐标（DIP）。</summary>
        public double Bottom => Margin + ContentHeight;
    }
}
