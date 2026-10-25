using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;

namespace Corkboard.Core.Controls;

/// <summary>
///     粘滞滚动控件：页头跟着内容往上走，滚到只剩 <see cref="CollapsedHeight" /> 那么高就钉住不动。
/// </summary>
/// <remarks>
///     <para>
///         素材来自上游 SecRandom-C 的同名控件（<c>SecRandom.Core/Controls/StickyScrollViewer</c>），
///         「关于」页的大页头就靠它：页头是一整块海报 + 标题，往下滚时它整体上移并逐渐淡出，
///         滚到末尾剩下 <see cref="CollapsedHeight" /> 那一截作为粘滞区，内容从它底下穿过去。
///     </para>
///     <para>
///         模板只有一张：两个 PART，<c>PART_Header</c> 是页头（跨两行、<c>VerticalAlignment=Top</c>），
///         <c>PART_MainScrollViewer</c> 是正文滚动区（第二行）。正文内部顶上垫一块
///         <c>SpaceAreaHeight</c> 高的空块，把页头没滚走时占的位置让出来。
///     </para>
///     <para>
///         <see cref="HeaderFadingOpacityProperty" /> 是 <b>附加属性</b>（挂在 <see cref="Control" /> 上），
///         页头自己在 XAML 里绑 <c>$self.(controls:StickyScrollViewer.HeaderFadingOpacity)</c> 来淡出，
///         控件不替页头结构做主。上游还用它配一层 <c>OpacityMask</c> 渐变，滚到底时页头「化开」。
///     </para>
/// </remarks>
[TemplatePart(MainScrollViewerName, typeof(ScrollViewer))]
[TemplatePart(HeaderName, typeof(ContentPresenter))]
[PseudoClasses(":header-collapsed")]
public class StickyScrollViewer : ContentControl
{
    private const string MainScrollViewerName = "PART_MainScrollViewer";
    private const string HeaderName = "PART_Header";

    /// <summary>页头淡出用的不透明度（0 = 已经完全淡掉，1 = 完全可见）。附加在任意控件上读。</summary>
    /// <remarks>
    ///     名字只能是字面量：附加属性没有对应的 CLR 属性可 <c>nameof</c>，
    ///     取值一律走 <see cref="GetHeaderFadingOpacity" /> / <see cref="SetHeaderFadingOpacity" />。
    /// </remarks>
    public static readonly AttachedProperty<double> HeaderFadingOpacityProperty =
        AvaloniaProperty.RegisterAttached<StickyScrollViewer, Control, double>("HeaderFadingOpacity", 1.0,
            inherits: true);

    /// <summary>滚到底之后页头至少还留在上面那一截的高度。</summary>
    public static readonly StyledProperty<double> CollapsedHeightProperty =
        AvaloniaProperty.Register<StickyScrollViewer, double>(nameof(CollapsedHeight), 150);

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<StickyScrollViewer, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<IDataTemplate?> HeaderTemplateProperty =
        AvaloniaProperty.Register<StickyScrollViewer, IDataTemplate?>(nameof(HeaderTemplate));

    /// <summary>页头当前应该往上偏移多少（模板把 <c>PART_Header</c> 的 Margin 取负值用它）。</summary>
    public static readonly StyledProperty<double> RealHeaderHeightProperty =
        AvaloniaProperty.Register<StickyScrollViewer, double>(nameof(RealHeaderHeight));

    /// <summary>页头占掉、需要正文顶上让开的高度。</summary>
    public static readonly StyledProperty<double> SpaceAreaHeightProperty =
        AvaloniaProperty.Register<StickyScrollViewer, double>(nameof(SpaceAreaHeight));

    private ContentPresenter? _header;
    private ScrollViewer? _mainScrollViewer;

    /// <summary>把 <see cref="RealHeaderHeight" /> 折成页头的负上边距。</summary>
    public static FuncValueConverter<double, Thickness> DoubleToHeaderMarginConverter { get; } =
        new(value => new Thickness(0, -value, 0, 0));

    /// <summary>把滚动偏移量折成页头的负上边距。</summary>
    public static FuncValueConverter<Vector, Thickness> ScrollViewerOffsetToHeaderMarginConverter { get; } =
        new(value => new Thickness(0, -value.Y, 0, 0));

    /// <summary>把 <see cref="SpaceAreaHeight" /> 折成像素行高。</summary>
    public static FuncValueConverter<double, GridLength> DoubleToGridLengthConverter { get; } =
        new(value => new GridLength(value, GridUnitType.Pixel));

    public double CollapsedHeight
    {
        get => GetValue(CollapsedHeightProperty);
        set => SetValue(CollapsedHeightProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public IDataTemplate? HeaderTemplate
    {
        get => GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public double RealHeaderHeight
    {
        get => GetValue(RealHeaderHeightProperty);
        set => SetValue(RealHeaderHeightProperty, value);
    }

    public double SpaceAreaHeight
    {
        get => GetValue(SpaceAreaHeightProperty);
        set => SetValue(SpaceAreaHeightProperty, value);
    }

    public static void SetHeaderFadingOpacity(Control obj, double value) =>
        obj.SetValue(HeaderFadingOpacityProperty, value);

    public static double GetHeaderFadingOpacity(Control obj) => obj.GetValue(HeaderFadingOpacityProperty);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // 模板换掉时先把上一份的订阅退干净，否则模板重建一次就多挂一个处理器。
        DetachTemplateParts();

        _mainScrollViewer = e.NameScope.Find<ScrollViewer>(MainScrollViewerName);
        if (_mainScrollViewer is not null)
            _mainScrollViewer.ScrollChanged += OnMainScrollViewerScrollChanged;

        _header = e.NameScope.Find<ContentPresenter>(HeaderName);
        if (_header is not null)
            _header.SizeChanged += OnHeaderSizeChanged;

        UpdateContent();
    }

    private void DetachTemplateParts()
    {
        if (_mainScrollViewer is not null)
        {
            _mainScrollViewer.ScrollChanged -= OnMainScrollViewerScrollChanged;
            _mainScrollViewer = null;
        }

        if (_header is not null)
        {
            _header.SizeChanged -= OnHeaderSizeChanged;
            _header = null;
        }
    }

    private void OnHeaderSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateContent();

    private void OnMainScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e) => UpdateContent();

    /// <summary>
    ///     按当前滚动偏移重算页头的偏移量、正文要让开的高度与淡出值。
    ///     页头比 <see cref="CollapsedHeight" /> 还矮时（没配海报的页）直接算成「已收」，不除零。
    /// </summary>
    private void UpdateContent()
    {
        if (_mainScrollViewer is null || _header is null)
            return;

        var headerHeight = _header.Bounds.Height;
        var offset = _mainScrollViewer.Offset.Y;
        var collapsible = headerHeight - CollapsedHeight;

        if (collapsible <= 0)
        {
            RealHeaderHeight = 0;
            SpaceAreaHeight = Math.Max(0, headerHeight);
            ApplyHeaderOpacity(1);
            PseudoClasses.Set(":header-collapsed", true);
            return;
        }

        var collapsed = collapsible - offset < CollapsedHeight;
        RealHeaderHeight = collapsed ? collapsible : offset;
        SpaceAreaHeight = collapsible;

        // 淡出用三次方：前半程几乎不动，最后一段迅速化开（与上游同一个手感）。
        ApplyHeaderOpacity(collapsed ? 0 : 1 - Math.Pow(offset / collapsible, 3));
        PseudoClasses.Set(":header-collapsed", collapsed);
    }

    /// <summary>
    ///     把淡出值写到附加属性上，由页头里的海报自己绑过去用。
    /// </summary>
    /// <remarks>
    ///     ⚠️ **不要**顺手把 <c>PART_Header</c> 整体设成半透明：页头底部那一截是钉住的标题，
    ///     它必须一直看得见。上游只让海报区绑这个值淡出（<c>Opacity="{Binding $self.(...)}"</c>），
    ///     整块一起淡会让标题跟着滚没（试过一次，滚下去标题就空了）。
    /// </remarks>
    private void ApplyHeaderOpacity(double value) => SetHeaderFadingOpacity(this, value);
}
