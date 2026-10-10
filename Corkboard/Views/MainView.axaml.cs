using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Controls;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Services;
using Corkboard.Helpers;
using Corkboard.Services.Ui;
using Corkboard.ViewModels;
using Corkboard.Views.MainPages;

namespace Corkboard.Views;

/// <summary>
///     主界面壳：没有侧边导航栏，内容区只承载注册表里的当前主页面
///     （默认页见 <see cref="AppConsts.DefaultMainPageId" />），页面实例通过键控 DI 取。
///     <para>
///         程序化切换页面走 <see cref="SelectNavigationItemById" />；用户可见入口只有托盘菜单
///         （<c>TaskBarIconService</c>），设置界面是独立窗口（<see cref="App.ShowSettingsWindow" />）。
///     </para>
///     <para>
///         窗口拖动有两条路径：顶部标题栏按下即拖；其余位置按 <c>BasicSettingsConfig.HoldToDragWindow</c>
///         长按判定后拖动。<b>开关打开时是整窗任意位置</b>——命中按钮、输入框、列表也一样——
///         见 <see cref="OnPointerPressedForDrag" />。
///     </para>
///     <para>
///         窗口缩放也归这里管：主窗口是 <c>WindowDecorations.None</c>，没有原生可缩放边框
///         （见 <see cref="MainWindow" /> 里的说明），边缘这几像素由壳自己接管，
///         同样按 <see cref="Window.Position" /> 与宽高直接改（置底到桌面时原生缩放也不生效）。
///     </para>
/// </summary>
public partial class MainView : ContentPage, IFANavigationPageFactory
{
    /// <summary>长按判定时间：按住不动多久算长按。</summary>
    private const double HoldToDragMilliseconds = 500;

    /// <summary>长按判定期间允许的指针抖动（DIP）：超过就认为用户在拖内容，不再抢拖动。</summary>
    private const double HoldToDragMoveTolerance = 4;

    /// <summary>贴边多少 DIP 以内算「抓到了边缘」，按下即进入缩放。</summary>
    private const double ResizeBandThickness = 6;

    /// <summary>缩放时抓的是哪几条边（可以同时抓两条，那就是角）。</summary>
    [Flags]
    private enum ResizeSides
    {
        None = 0,
        West = 1,
        East = 2,
        North = 4,
        South = 8
    }

    private static readonly Cursor SizeHorizontalCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor SizeVerticalCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor SizeTopLeftCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor SizeTopRightCursor = new(StandardCursorType.TopRightCorner);
    private static readonly Cursor SizeBottomLeftCursor = new(StandardCursorType.BottomLeftCorner);
    private static readonly Cursor SizeBottomRightCursor = new(StandardCursorType.BottomRightCorner);

    private readonly FAFrame? _navigationFrame;
    private readonly Border? _titleBar;
    private readonly Button? _sortButton;
    private InputElement? _dragHost;
    private DispatcherTimer? _holdToDragTimer;
    private PointerPressedEventArgs? _holdToDragPressedEvent;
    private Point _holdToDragOrigin;
    private bool _isDraggingWindow;
    private IPointer? _dragPointer;
    private PixelPoint _dragStartWindowPosition;
    private PixelPoint _dragStartPointerScreen;
    private bool _isResizingWindow;
    private IPointer? _resizePointer;
    private ResizeSides _resizeSides;
    private PixelPoint _resizeStartPointerScreen;
    private PixelPoint _resizeStartWindowPosition;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    /// <summary>Debug 构建的版本水印是否已经挂上（见 <see cref="OnLoadedForDevelopmentAdorner" />）。</summary>
    private bool _isDevelopmentAdornerAdded;

    /// <summary>页面级弹层的宿主状态，绑定在壳的根 Panel 上。</summary>
    public PageOverlayService Overlay { get; } = IAppHost.GetService<PageOverlayService>();

    /// <summary>
    ///     时空回放状态。标题栏的控制条直接绑它；主页面也从同一个单例读，
    ///     这样「壳控制、页面显示」两边共享同一份进度。
    /// </summary>
    public BoardReplayService Replay { get; } = IAppHost.GetService<BoardReplayService>();

    public MainView()
    {
        Current = this;
        DataContext = this;
        InitializeComponent();

        _navigationFrame = this.FindControl<FAFrame>("NavigationFrame");
        _titleBar = this.FindControl<Border>("TitleBar");
        _sortButton = this.FindControl<Button>("SortButton");

        if (_navigationFrame is not null)
            _navigationFrame.NavigationPageFactory = this;

        // 初始导航同样等首次布局之后再走，见 OnLoadedForInitialNavigation。
        Loaded += OnLoadedForInitialNavigation;
        Loaded += OnLoadedForDragHost;
        Loaded += OnLoadedForDevelopmentAdorner;

        // 弹层一打开就把可能还在进行的长按/拖动收掉，免得拖状态留着把弹层里的点击吃掉。
        Overlay.PropertyChanged += OnOverlayChanged;

        Unloaded += (_, _) =>
        {
            CancelHoldToDrag();
            Overlay.PropertyChanged -= OnOverlayChanged;
            ViewModel.Detach();

            if (ReferenceEquals(Current, this))
                Current = null;
        };
    }

    private void OnOverlayChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PageOverlayService.IsOpen) && Overlay.IsOpen)
            EndWindowDrag();
    }

    /// <summary>
    ///     拖动手势挂在<b>窗口</b>上而不是这个壳上：主窗口顶部那条标题栏带宽是
    ///     <c>FAAppWindow</c> 自己的标题栏层（在内容之上、不属于本壳的可视子树），
    ///     只挂在 <see cref="MainView" /> 上时按在那里的事件根本到不了这里，
    ///     于是窗口最上面一条就成了长按拖不动的死区。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        SetDragHost(TopLevel.GetTopLevel(this) as InputElement);
    }

    /// <summary>
    ///     兜底：个别时序下 <see cref="OnAttachedToVisualTree" /> 还取不到窗口，首次布局后再解析一次；
    ///     仍取不到就退回挂在本壳上，至少内容区还能长按拖动。
    /// </summary>
    private void OnLoadedForDragHost(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedForDragHost;
        SetDragHost(TopLevel.GetTopLevel(this) as InputElement ?? this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        CancelHoldToDrag();
        EndWindowResize();
        SetDragHost(null);
    }

    /// <summary>手势宿主换人时把旧的四个隧道处理器摘干净，避免重复触发。</summary>
    private void SetDragHost(InputElement? host)
    {
        if (ReferenceEquals(_dragHost, host))
            return;

        if (_dragHost is not null)
        {
            _dragHost.RemoveHandler(PointerPressedEvent, OnPointerPressedForDrag);
            _dragHost.RemoveHandler(PointerMovedEvent, OnPointerMovedForDrag);
            _dragHost.RemoveHandler(PointerReleasedEvent, OnPointerReleasedForDrag);
            _dragHost.RemoveHandler(PointerCaptureLostEvent, OnPointerCaptureLostForDrag);
        }

        _dragHost = host;
        if (host is null)
            return;

        // 隧道阶段 + handledEventsToo：内容里的按钮、滚动区、输入框，以及窗口标题栏层，
        // 都不会在我们之前把按下事件吃掉。
        host.AddHandler(PointerPressedEvent, OnPointerPressedForDrag, RoutingStrategies.Tunnel,
            handledEventsToo: true);
        host.AddHandler(PointerMovedEvent, OnPointerMovedForDrag, RoutingStrategies.Tunnel,
            handledEventsToo: true);
        host.AddHandler(PointerReleasedEvent, OnPointerReleasedForDrag, RoutingStrategies.Tunnel,
            handledEventsToo: true);
        host.AddHandler(PointerCaptureLostEvent, OnPointerCaptureLostForDrag, RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    public static MainView? Current { get; private set; }

    public MainViewModel ViewModel { get; } = IAppHost.GetService<MainViewModel>();
    public bool IsMacOs => OperatingSystem.IsMacOS();

    public Control? GetPage(Type srcType)
    {
        return Activator.CreateInstance(srcType) as Control;
    }

    /// <summary>页面工厂：<see cref="PageInfo.Id" /> 就是键控 DI 的 key。</summary>
    public Control? GetPageFromObject(object target)
    {
        if (target is not PageInfo info) return null;

        var page = IAppHost.Host!.Services.GetKeyedService<UserControl>(info.Id);
        if (page == null)
            return new TextBlock { Text = $"页面 {info.Id} 未找到" };

        return page;
    }

    /// <summary>按注册表 Id 导航；Id 不在主页面注册表里时保持当前页面不动。</summary>
    public void SelectNavigationItemById(string id)
    {
        var info = PagesRegistryService.MainItems.FirstOrDefault(item => item.Id == id);
        if (info is not null) Navigate(info);
    }

    private void Navigate(PageInfo info)
    {
        ViewModel.FrameContent = null;
        ViewModel.SelectedPageInfo = info;
        if (_navigationFrame is not null)
            UiMotion.NavigateFromObject(_navigationFrame, info);
    }

    /// <summary>
    ///     初始导航推迟到首次布局之后：那时 <see cref="FAFrame" /> 已经进入可视树，
    ///     页面工厂与入场动效都就绪；在构造期直接导航会丢掉这些容器状态。
    /// </summary>
    private void OnLoadedForInitialNavigation(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedForInitialNavigation;

        Dispatcher.UIThread.Post(
            () =>
            {
                _navigationFrame?.UpdateLayout();
                SelectNavigationItemById(AppConsts.DefaultMainPageId);
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    ///     整窗拖动入口。顶部标题栏按下即拖（窗口自身还有一个原生标题栏路径兜底）；
    ///     其余位置在开关打开时长按判定：<b>整窗任意位置</b>——包括按钮、输入框、列表——
    ///     按住不动到时间后都开始拖动，期间移动超过阈值就放弃，这样短按点击、输入、滚动
    ///     仍然归控件，只有「按住不动」才被判成长按拖动。
    /// </summary>
    private void OnPointerPressedForDrag(object? sender, PointerPressedEventArgs e)
    {
        // 上一次拖动如果没收到 PointerReleased / PointerCaptureLost（指针在窗口外抬起、
        // 被别的控件抢走捕获等），状态和捕获会留在壳上，表现为「整窗点不动」。
        // 新的按下就是最好的收尾时机：先把它清干净，再按这次按下重新判定。
        EndWindowDrag();
        EndWindowResize();

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && e.Pointer.Type != PointerType.Touch)
            return;

        // 抓边缘优先于拖窗口：主窗口没有原生可缩放边框，贴边这几像素就是缩放热区。
        if (TryStartWindowResize(e))
            return;

        if (IsInTitleBar(e.Source))
        {
            // 标题栏按下即拖；命中标题栏里的交互控件时让出（导航栏去掉后主界面标题栏暂时没有控件，
            // 但以后加按钮就靠这张排除表，见 IsInteractiveSource）。
            if (!IsInteractiveSource(e.Source))
                StartWindowDrag(e);

            return;
        }

        // 不再按命中控件过滤：整窗任意位置都能长按拖动，控件自己的手势靠
        // 「长按判定期间移动超过阈值即放弃」让出（见 OnPointerMovedForDrag）。
        //
        // 但弹层开着时必须让出：模态期间拖窗口没意义，而且长按会把弹层里输入框的点击抢走，
        // 用户会觉得「输入框点不动、打不了字」。
        if (Overlay.IsOpen)
            return;

        // 标了「这块地方的手势归我」的子树也要让出：作业板科目区块的标题行是
        // 「按住就拖起来排序」，不让出就会变成拖窗口与换区块同时发生（见 WindowDragGesture）。
        if (WindowDragGesture.IsSuppressed(e.Source))
            return;

        if (!ViewModel.Config.Basic.HoldToDragWindow)
            return;

        _holdToDragPressedEvent = e;
        _holdToDragOrigin = e.GetPosition(this);
        _holdToDragTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoldToDragMilliseconds) };
        _holdToDragTimer.Tick += OnHoldToDragTimerTick;
        _holdToDragTimer.Start();
    }

    private void OnHoldToDragTimerTick(object? sender, EventArgs e)
    {
        var pressedEvent = _holdToDragPressedEvent;
        CancelHoldToDrag();

        if (pressedEvent is not null)
            StartWindowDrag(pressedEvent);
    }

    private void OnPointerMovedForDrag(object? sender, PointerEventArgs e)
    {
        if (_isResizingWindow)
        {
            // 同拖动：左键已经在外面抬起来时收不到 Released，见到「没按键的移动」就收尾。
            if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                EndWindowResize();
                return;
            }

            MoveWindowResize(e);
            return;
        }

        if (_isDraggingWindow)
        {
            // 左键已经抬起来了（窗口外抬起、捕获被抢走时收不到 Released）就当场收尾，
            // 别让壳一直处在「拖动中」——那种状态会把指针捕获和整窗输入一起卡住。
            if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                EndWindowDrag();
                return;
            }

            MoveWindowDrag(e);
            return;
        }

        if (_holdToDragTimer is null)
        {
            UpdateResizeCursor(e);
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _holdToDragOrigin.X) > HoldToDragMoveTolerance
            || Math.Abs(position.Y - _holdToDragOrigin.Y) > HoldToDragMoveTolerance)
            CancelHoldToDrag();
    }

    private void OnPointerReleasedForDrag(object? sender, PointerReleasedEventArgs e)
    {
        EndWindowDrag();
        EndWindowResize();
    }

    private void OnPointerCaptureLostForDrag(object? sender, PointerCaptureLostEventArgs e)
    {
        EndWindowDrag();
        EndWindowResize();
    }

    private void CancelHoldToDrag()
    {
        if (_holdToDragTimer is not null)
        {
            _holdToDragTimer.Stop();
            _holdToDragTimer.Tick -= OnHoldToDragTimerTick;
            _holdToDragTimer = null;
        }

        _holdToDragPressedEvent = null;
    }

    /// <summary>
    ///     拖动开始：接管指针并记下窗口与指针的起点。刻意不用 <c>Window.BeginMoveDrag</c>：
    ///     窗口置底到桌面后是桌面宿主的子窗口，原生标题拖动对它不生效（实测不动），
    ///     而按 <see cref="Window.Position" /> 位移在两种形态下都有效。
    /// </summary>
    private void StartWindowDrag(PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
            return;

        CancelHoldToDrag();
        _isDraggingWindow = true;
        _dragPointer = e.Pointer;
        _dragStartWindowPosition = window.Position;
        _dragStartPointerScreen = GetPointerScreenPoint(e);
        e.Pointer.Capture(this);
    }

    private void MoveWindowDrag(PointerEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
            return;

        var current = GetPointerScreenPoint(e);
        var target = new PixelPoint(
            _dragStartWindowPosition.X + (current.X - _dragStartPointerScreen.X),
            _dragStartWindowPosition.Y + (current.Y - _dragStartPointerScreen.Y));

        if (target != window.Position)
            window.Position = target;
    }

    /// <summary>
    ///     结束拖动。<b>必须显式把指针捕获还回去</b>：指针如果在窗口外抬起，
    ///     <c>PointerReleased</c> 就收不到，捕获会一直留在壳上，
    ///     之后整窗的控件都点不动、输入框也没法聚焦——这是「输入框无法输入」的真凶。
    /// </summary>
    private void EndWindowDrag()
    {
        _dragPointer?.Capture(null);
        _dragPointer = null;
        _isDraggingWindow = false;
        CancelHoldToDrag();
    }

    /// <summary>
    ///     指针的屏幕物理坐标：窗口左上角 + 指针相对窗口的偏移（按缩放折算）。
    ///     用「绝对坐标」而不是相对位移，是因为窗口会跟着指针走，相对位移会自我抵消。
    /// </summary>
    private PixelPoint GetPointerScreenPoint(PointerEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var windowPosition = (topLevel as Window)?.Position ?? default;
        var scaling = topLevel?.RenderScaling ?? 1.0;
        var position = e.GetPosition(this);

        return new PixelPoint(
            windowPosition.X + (int)Math.Round(position.X * scaling),
            windowPosition.Y + (int)Math.Round(position.Y * scaling));
    }

    #region 窗口边缘缩放

    /// <summary>
    ///     按下时先看是不是抓到了边缘，命中就接管指针进入缩放。
    ///     <para>
    ///         主窗口是 <c>WindowDecorations.None</c>：保留原生边框（<c>BorderOnly</c>）会连
    ///         <c>WS_THICKFRAME</c> 一起留下，Windows 于是在客户区外留出那一圈原生边框——
    ///         实测左/右/下各 11 物理像素——而分层半透明窗口不画那一圈，看上去就是一整条黑边。
    ///         去掉原生边框的代价是原生缩放热区也没了，所以由壳自己顶上来（见 <see cref="MainWindow" />）。
    ///     </para>
    /// </summary>
    private bool TryStartWindowResize(PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window { WindowState: WindowState.Normal } window)
            return false;

        var sides = HitTestResizeSides(e.GetPosition(this));
        if (sides == ResizeSides.None)
            return false;

        CancelHoldToDrag();
        _isResizingWindow = true;
        _resizeSides = sides;
        _resizePointer = e.Pointer;
        _resizeStartPointerScreen = GetPointerScreenPoint(e);
        _resizeStartWindowPosition = window.Position;
        _resizeStartWidth = window.Bounds.Width;
        _resizeStartHeight = window.Bounds.Height;
        e.Pointer.Capture(this);
        return true;
    }

    /// <summary>
    ///     缩放中：按<b>屏幕绝对位移</b>算新尺寸与新位置。
    ///     这里不能像普通控件那样用「相对窗口的位移」——West/North 缩放会同时移动窗口，
    ///     相对位移会跟着一起抵消（跟拖动是同一个坑，见 <see cref="GetPointerScreenPoint" />）。
    /// </summary>
    private void MoveWindowResize(PointerEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window || _resizeSides == ResizeSides.None)
            return;

        var current = GetPointerScreenPoint(e);
        var scaling = window.RenderScaling is > 0 and var renderScaling ? renderScaling : 1.0;
        var deltaX = (current.X - _resizeStartPointerScreen.X) / scaling;
        var deltaY = (current.Y - _resizeStartPointerScreen.Y) / scaling;

        var width = _resizeStartWidth;
        var height = _resizeStartHeight;

        if (_resizeSides.HasFlag(ResizeSides.West))
            width = Math.Max(window.MinWidth, _resizeStartWidth - deltaX);
        else if (_resizeSides.HasFlag(ResizeSides.East))
            width = Math.Max(window.MinWidth, _resizeStartWidth + deltaX);

        if (_resizeSides.HasFlag(ResizeSides.North))
            height = Math.Max(window.MinHeight, _resizeStartHeight - deltaY);
        else if (_resizeSides.HasFlag(ResizeSides.South))
            height = Math.Max(window.MinHeight, _resizeStartHeight + deltaY);

        // 位置按「没被抓的那条边不动」补偿；只在抓了 West/North 时才需要挪窗口。
        // 位置是物理像素、尺寸是 DIP，所以补偿要乘缩放（与拖动里的算法一致）。
        var leftOffset = _resizeSides.HasFlag(ResizeSides.West)
            ? (int)Math.Round((_resizeStartWidth - width) * scaling)
            : 0;
        var topOffset = _resizeSides.HasFlag(ResizeSides.North)
            ? (int)Math.Round((_resizeStartHeight - height) * scaling)
            : 0;
        var target = new PixelPoint(
            _resizeStartWindowPosition.X + leftOffset,
            _resizeStartWindowPosition.Y + topOffset);

        // 尺寸要写窗口的 Width/Height（而不是只改 Bounds）：尺寸记忆订阅的是窗口属性变化。
        if (Math.Abs(window.Width - width) > 0.5)
            window.Width = width;

        if (Math.Abs(window.Height - height) > 0.5)
            window.Height = height;

        if (target != window.Position)
            window.Position = target;
    }

    /// <summary>
    ///     结束缩放。<b>必须显式把指针捕获还回去</b>：指针在窗口外抬起时收不到
    ///     <c>PointerReleased</c>，捕获留在壳上会让整窗控件点不动（与 <see cref="EndWindowDrag" /> 同因）。
    /// </summary>
    private void EndWindowResize()
    {
        _resizePointer?.Capture(null);
        _resizePointer = null;
        _resizeSides = ResizeSides.None;
        _isResizingWindow = false;
    }

    /// <summary>
    ///     边缘热区判定：贴边 <see cref="ResizeBandThickness" /> 以内算命中，角上两条边都算。
    ///     窗口很小时按最短边的三分之一收窄，免得热区把标题栏整个吃掉。
    /// </summary>
    private ResizeSides HitTestResizeSides(Point point)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return ResizeSides.None;

        var band = Math.Min(ResizeBandThickness, Math.Min(width, height) / 3);
        var sides = ResizeSides.None;

        if (point.X <= band)
            sides |= ResizeSides.West;
        else if (point.X >= width - band)
            sides |= ResizeSides.East;

        if (point.Y <= band)
            sides |= ResizeSides.North;
        else if (point.Y >= height - band)
            sides |= ResizeSides.South;

        return sides;
    }

    /// <summary>没有手势在跑时按指针所在边缘换光标，用户才知道这里能拉。</summary>
    private void UpdateResizeCursor(PointerEventArgs e)
    {
        var cursor = HitTestResizeSides(e.GetPosition(this)) switch
        {
            ResizeSides.West or ResizeSides.East => SizeHorizontalCursor,
            ResizeSides.North or ResizeSides.South => SizeVerticalCursor,
            ResizeSides.West | ResizeSides.North => SizeTopLeftCursor,
            ResizeSides.North | ResizeSides.East => SizeTopRightCursor,
            ResizeSides.East | ResizeSides.South => SizeBottomRightCursor,
            ResizeSides.South | ResizeSides.West => SizeBottomLeftCursor,
            _ => null
        };

        if (!ReferenceEquals(Cursor, cursor))
            Cursor = cursor;
    }

    #endregion

    /// <summary>
    ///     Debug 构建的左下角版本水印：往壳的 <c>AdornerLayer</c> 里塞一个
    ///     <see cref="DevelopmentBuildAdorner" />，它铺满整壳、不吃命中测试，只画一行字。
    ///     与上游 SecRandom-C 一致；Release 构建下 <see cref="GlobalConstants.IsDevelopment" />
    ///     为 false，整个方法直接返回。
    /// </summary>
    private void OnLoadedForDevelopmentAdorner(object? sender, RoutedEventArgs e)
    {
        if (!GlobalConstants.IsDevelopment || _isDevelopmentAdornerAdded || Content is not Control element)
            return;

        // AdornerLayer 由窗口模板提供；取不到就什么都不做（不抛、也不反复重试同一次加载）。
        if (AdornerLayer.GetAdornerLayer(element) is not { } layer)
            return;

        var adorner = new DevelopmentBuildAdorner();
        layer.Children.Add(adorner);
        AdornerLayer.SetAdornedElement(adorner, this);
        _isDevelopmentAdornerAdded = true;
    }

    #region 标题栏按钮

    /// <summary>
    ///     「设置」：打开设置窗口（主窗口里唯一的设置入口，另一个在托盘菜单里）。
    ///     回放期间也能点：改设置不影响回放进度。
    /// </summary>
    private void OpenSettingsButton_OnClick(object? sender, RoutedEventArgs e) => App.ShowSettingsWindow();

    /// <summary>
    ///     「排序」：按钮本身只负责弹菜单，弹出前把当前档勾上。
    ///     <para>
    ///         勾选不走 MVVM：菜单项画在弹出层里、不在这个可视树上，所以在<b>弹出这一刻</b>
    ///         按配置写一遍（<see cref="RefreshSortMenuChecks" />），而不是绑 MenuItem.IsChecked。
    ///     </para>
    /// </summary>
    private void SortButton_OnClick(object? sender, RoutedEventArgs e) => RefreshSortMenuChecks();

    /// <summary>
    ///     排序菜单里的一项被点了：写进配置，再按配置把勾重新点一遍。
    ///     <para>
    ///         写配置之后不用手动刷新板子：作业板页面自己订阅了这条变化，会按新顺序重建列表
    ///         （见 <c>BoardPageViewModel</c>）。勾选这里自己再写一次，是不想依赖 MenuItem 的
    ///         Radio 分组逻辑——那套逻辑只在菜单里生效，跟配置不是同一个来源。
    ///     </para>
    /// </summary>
    private void SortMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: BoardSortMode mode })
            return;

        ViewModel.SetSortModeCommand.Execute(mode);
        RefreshSortMenuChecks();
    }

    /// <summary>按配置给菜单里的五项写勾选状态（配置里是越界值时就一个都不勾）。</summary>
    private void RefreshSortMenuChecks()
    {
        if (_sortButton?.Flyout is not MenuFlyout flyout)
            return;

        var current = ViewModel.SelectedSortOption.Mode;
        foreach (var item in flyout.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag is BoardSortMode mode && mode == current;
    }

    /// <summary>
    ///     「布置作业」：表单由 <see cref="PageOverlayService" /> 挂到壳这一层显示，
    ///     遮罩才能盖住自绘标题栏（页面里画会漏掉标题栏那一条）。
    /// </summary>
    private void AddAssignmentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        // 回放期间看的是历史快照，此时不接受新建。
        if (Replay.IsActive)
            return;

        Overlay.Show(new BoardAssignmentForm());
    }

    private void ReplayEnterButton_OnClick(object? sender, RoutedEventArgs e) => Replay.Enter();

    private void ReplayExitButton_OnClick(object? sender, RoutedEventArgs e) => Replay.Exit();

    /// <summary>
    ///     「锁定窗口」：开点击穿透。只写配置——窗口样式与显隐都由 <c>MainWindow</c> 订阅配置变化后处理，
    ///     视图不直接碰平台能力，也不需要知道徽标窗口的存在（<c>MainView</c> 会把这一排连同自己一起让开）。
    /// </summary>
    private void LockWindowButton_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel.Config.Basic.ClickThrough = true;

    #endregion

    private bool IsInTitleBar(object? source)
    {
        if (_titleBar is null || source is not Visual visual)
            return false;

        for (Visual? current = visual; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, _titleBar))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     命中标题栏里的交互控件时不要启动窗口拖动，否则按钮收不到点击。
    ///     <para>
    ///         标题栏现在有「设置 / 排序 / 布置作业 / 时空回放 / 锁窗口」五类入口（按钮、下拉），
    ///         所以这张排除表是真在用的：漏掉任何一类控件，那个控件就会变成「点不动」。
    ///         排序菜单的菜单项画在弹出层里、不在标题栏上，不走这里。
    ///     </para>
    /// </summary>
    private bool IsInteractiveSource(object? source)
    {
        if (source is not Visual visual)
            return false;

        for (Visual? current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Button or ToggleButton or TextBox or ComboBox or Slider)
                return true;

            if (ReferenceEquals(current, _titleBar))
                return false;
        }

        return false;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
