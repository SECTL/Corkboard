using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Avalonia.Input;

namespace Corkboard.Core.Controls;

/// <summary>抽屉从哪一侧滑出来。</summary>
public enum DrawerPlacement
{
    Left,
    Right
}

/// <summary>
///     右侧抽屉宿主：内容照常铺满，抽屉按需从侧边滑出来盖在上面。
/// </summary>
/// <remarks>
///     <para>
///         素材来自上游 SecRandom-C 的同名控件（<c>SecRandom.Core/Controls/DrawerHost</c>）。
///         「关于」页的贡献人员名单就装在里面：点一下从右边滑出一块 320 宽的列表，
///         点遮罩、按 Esc 或点里面的关闭按钮收起。
///     </para>
///     <para>
///         为什么要一个宿主而不是页面自己画：抽屉必须盖住壳的标题栏与整个内容区才能算「模态」，
///         而页面在 <c>FAFrame</c> 里、盖不到外层——所以宿主挂在设置壳那一层（见 SettingsView）。
///     </para>
///     <para>
///         抽屉宽度不写死：内容量出来多宽就是多宽（<see cref="ActualDrawerWidth" />），
///         模板靠它在关闭时把抽屉推到屏幕外。
///     </para>
/// </remarks>
[PseudoClasses(":drawer-left", ":drawer-right", ":open")]
[TemplatePart(IgnoreLayerName, typeof(Border))]
[TemplatePart(DrawerContentBorderName, typeof(Border))]
public class DrawerHost : ContentControl
{
    private const string IgnoreLayerName = "PART_IgnoreLayer";
    private const string DrawerContentBorderName = "PART_DrawerContentBorder";

    public static readonly StyledProperty<object?> DrawerContentProperty =
        AvaloniaProperty.Register<DrawerHost, object?>(nameof(DrawerContent));

    public static readonly StyledProperty<IDataTemplate?> DrawerContentTemplateProperty =
        AvaloniaProperty.Register<DrawerHost, IDataTemplate?>(nameof(DrawerContentTemplate));

    public static readonly StyledProperty<bool> IsDrawerOpenProperty =
        AvaloniaProperty.Register<DrawerHost, bool>(nameof(IsDrawerOpen));

    public static readonly StyledProperty<DrawerPlacement> DrawerPlacementProperty =
        AvaloniaProperty.Register<DrawerHost, DrawerPlacement>(nameof(DrawerPlacement));

    /// <summary>抽屉内容实测出来的宽度（模板用它把关闭态的抽屉推到屏幕外）。</summary>
    public static readonly StyledProperty<double> ActualDrawerWidthProperty =
        AvaloniaProperty.Register<DrawerHost, double>(nameof(ActualDrawerWidth));

    /// <summary>把 <see cref="ActualDrawerWidth" /> 取负，供模板的 <c>Canvas.Left/Right</c> 用。</summary>
    public static FuncValueConverter<double, double> NegativeDoubleConverter { get; } = new(value => -value);

    private Border? _drawerContentBorder;
    private Border? _ignoreLayer;

    public DrawerHost()
    {
        KeyDown += OnKeyDown;
        UpdateDrawerPlacement();
    }

    public object? DrawerContent
    {
        get => GetValue(DrawerContentProperty);
        set => SetValue(DrawerContentProperty, value);
    }

    public IDataTemplate? DrawerContentTemplate
    {
        get => GetValue(DrawerContentTemplateProperty);
        set => SetValue(DrawerContentTemplateProperty, value);
    }

    public bool IsDrawerOpen
    {
        get => GetValue(IsDrawerOpenProperty);
        set => SetValue(IsDrawerOpenProperty, value);
    }

    public DrawerPlacement DrawerPlacement
    {
        get => GetValue(DrawerPlacementProperty);
        set => SetValue(DrawerPlacementProperty, value);
    }

    public double ActualDrawerWidth
    {
        get => GetValue(ActualDrawerWidthProperty);
        set => SetValue(ActualDrawerWidthProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // 用 OnPropertyChanged 而不是 Rx 的 Subscribe：Core 不引 System.Reactive，
        // 这里只需要「属性变了就同步一次伪类」，没必要为此多一条依赖。
        if (change.Property == DrawerPlacementProperty)
            UpdateDrawerPlacement();
        else if (change.Property == IsDrawerOpenProperty)
            PseudoClasses.Set(":open", IsDrawerOpen);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_drawerContentBorder is not null)
            _drawerContentBorder.SizeChanged -= OnDrawerContentBorderSizeChanged;
        if (_ignoreLayer is not null)
            _ignoreLayer.PointerPressed -= OnIgnoreLayerPointerPressed;

        _ignoreLayer = e.NameScope.Find<Border>(IgnoreLayerName);
        _drawerContentBorder = e.NameScope.Find<Border>(DrawerContentBorderName);

        if (_drawerContentBorder is not null)
            _drawerContentBorder.SizeChanged += OnDrawerContentBorderSizeChanged;
        if (_ignoreLayer is not null)
            _ignoreLayer.PointerPressed += OnIgnoreLayerPointerPressed;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || !IsDrawerOpen)
            return;

        IsDrawerOpen = false;
        e.Handled = true;
    }

    private void OnIgnoreLayerPointerPressed(object? sender, PointerPressedEventArgs e) => IsDrawerOpen = false;

    private void OnDrawerContentBorderSizeChanged(object? sender, SizeChangedEventArgs e) =>
        ActualDrawerWidth = e.NewSize.Width;

    private void UpdateDrawerPlacement()
    {
        PseudoClasses.Set(":drawer-left", DrawerPlacement == DrawerPlacement.Left);
        PseudoClasses.Set(":drawer-right", DrawerPlacement == DrawerPlacement.Right);
    }
}
