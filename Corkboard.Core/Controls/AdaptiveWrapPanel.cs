using Avalonia;
using Avalonia.Controls;

namespace Corkboard.Core.Controls;

/// <summary>
///     按可用宽度自适应的等宽区块面板：每一块一样宽，一块块放进当前最矮的那一列，
///     上一块下面空出来的地方由后面的区块补上。
///     <para>
///         和定宽 <c>WrapPanel</c> 的区别在宽度从哪来：这里每块的宽度由
///         <see cref="AdaptiveGridMetrics" /> 按当前可用宽度现算——窗口宽就多排几列、
///         每列平分宽度，窗口窄就自己收成一列，不会在窄窗口里横向溢出，也不会在宽窗口里
///         空着一大片。
///     </para>
///     <para>
///         纵向不按行对齐：区块高度是各自内容的高度（不拉成等高），矮的那块下面不留空行，
///         后面的区块直接补进来，落位规则见 <see cref="AdaptiveGridMetrics.ResolveMasonry" />。
///     </para>
/// </summary>
public class AdaptiveWrapPanel : Panel
{
    /// <summary>单个区块的宽度下限：窗口够宽时一排能排下几列由它决定。</summary>
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<AdaptiveWrapPanel, double>(
            nameof(MinItemWidth), Models.Board.BoardBlockStyle.DefaultMinItemWidth);

    /// <summary>单个区块的宽度上限：窗口很宽、区块又少时，别把余量全给一块。</summary>
    public static readonly StyledProperty<double> MaxItemWidthProperty =
        AvaloniaProperty.Register<AdaptiveWrapPanel, double>(
            nameof(MaxItemWidth), Models.Board.BoardBlockStyle.DefaultMinItemWidth
                                  * Models.Board.BoardBlockStyle.MaxItemWidthRatio);

    /// <summary>同一排里相邻两块之间的间距。</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AdaptiveWrapPanel, double>(nameof(Spacing), 8d);

    /// <summary>同一列里上下两块之间的间距。</summary>
    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<AdaptiveWrapPanel, double>(nameof(LineSpacing), 8d);

    /// <summary>
    ///     上一次测量算出来的列数与区块宽度。排布直接照用：两次算出来的结果必须一致，
    ///     否则落位会和测量时对不上（表现为最后一块跑出列外）。
    /// </summary>
    private int _columns;

    private double _itemWidth = Models.Board.BoardBlockStyle.DefaultMinItemWidth;

    /// <summary>每一块落在哪一列、纵向起点是多少（测量时算好，排布时照用）。</summary>
    private (int Column, double Y)[] _placements = [];

    /// <summary>每一块量出来的高度，复用一个数组少一点垃圾（测量很频繁）。</summary>
    private double[] _heights = [];

    static AdaptiveWrapPanel()
    {
        // 这几个值一改就得重新量（量变了排布也跟着重排）。
        AffectsMeasure<AdaptiveWrapPanel>(
            MinItemWidthProperty,
            MaxItemWidthProperty,
            SpacingProperty,
            LineSpacingProperty);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double MaxItemWidth
    {
        get => GetValue(MaxItemWidthProperty);
        set => SetValue(MaxItemWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children;
        if (children.Count == 0)
        {
            _placements = [];
            _columns = 0;
            return default;
        }

        var (columns, itemWidth) = AdaptiveGridMetrics.Resolve(
            availableSize.Width, MinItemWidth, MaxItemWidth, children.Count, Spacing);

        _columns = columns;
        _itemWidth = itemWidth;

        EnsureHeightBuffer(children.Count);

        for (var index = 0; index < children.Count; index++)
        {
            // 宽度给定、高度不限：区块高度是自己内容决定的，不跟同一列的邻居拉齐。
            children[index].Measure(new Size(itemWidth, double.PositiveInfinity));
            _heights[index] = children[index].DesiredSize.Height;
        }

        _placements = AdaptiveGridMetrics.ResolveMasonry(_heights, columns, LineSpacing);

        // 外层给得出宽度（正常情况）就铺满，给不出（横向无限）才用实际排出来的宽度。
        var width = double.IsPositiveInfinity(availableSize.Width)
            ? GetUsedWidth(columns, itemWidth)
            : availableSize.Width;

        return new Size(width, GetContentHeight());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children;

        // 正常流程里排布紧跟在测量之后；万一内容在两者之间变了（长度对不上），
        // 按当前高度重算一次落位，免得把块排到列外去。
        if (_placements.Length != children.Count)
        {
            EnsureHeightBuffer(children.Count);

            for (var index = 0; index < children.Count; index++)
                _heights[index] = children[index].DesiredSize.Height;

            _placements = AdaptiveGridMetrics.ResolveMasonry(_heights, _columns, LineSpacing);
        }

        for (var index = 0; index < children.Count; index++)
        {
            var (column, y) = _placements[index];
            var child = children[index];

            child.Arrange(new Rect(
                column * (_itemWidth + Spacing), y, _itemWidth, child.DesiredSize.Height));
        }

        return finalSize;
    }

    private void EnsureHeightBuffer(int count)
    {
        if (_heights.Length < count)
            _heights = new double[count];
    }

    /// <summary>排出来的总宽度（几列加上列间距）。</summary>
    private double GetUsedWidth(int columns, double itemWidth) =>
        (columns * itemWidth) + (Math.Max(0, columns - 1) * Spacing);

    /// <summary>最深的那一列的底部就是内容总高度。</summary>
    private double GetContentHeight()
    {
        var bottom = 0d;

        for (var index = 0; index < _placements.Length; index++)
        {
            var height = _heights[index];
            if (!double.IsFinite(height) || height < 0d)
                height = 0d;

            bottom = Math.Max(bottom, _placements[index].Y + height);
        }

        return bottom;
    }
}
