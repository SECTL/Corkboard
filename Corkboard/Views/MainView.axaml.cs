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
using Corkboard.Core.Board;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Helpers;
using Corkboard.Core.Services;
using Corkboard.Services.Board;
using Corkboard.Services.Ui;
using Corkboard.ViewModels;
using Corkboard.Views.MainPages.Homework;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views;

/// <summary>
///     主界面壳：没有侧边导航栏，内容区只承载注册表里的当前主页面
///     （默认页见 <see cref="AppConsts.DefaultMainPageId" />），页面实例通过键控 DI 取。
///     <para>
///         程序化切换页面走 <see cref="SelectNavigationItemById" />；用户可见入口只有托盘菜单
///         （<c>TaskBarIconService</c>），设置界面是独立窗口（<see cref="App.ShowSettingsWindow" />）。
///     </para>
///     <para>
///         窗口拖动只有一条路径：<b>解锁态下按住顶部标题栏</b>（<c>BasicSettingsConfig.WindowFixed</c>
///         为假时）。锁着的时候窗口位置与大小都钉住，板面按住也不会拖窗口——作业板上按住是要拖卡片、
///         写字的，抢那一下手势只会让人误操作。
///     </para>
///     <para>
///         窗口缩放也归这里管：主窗口是 <c>WindowDecorations.None</c>，没有原生可缩放边框
///         （见 <see cref="MainWindow" /> 里的说明），边缘这几像素由壳自己接管，
///         同样按 <see cref="Window.Position" /> 与宽高直接改（置底到桌面时原生缩放也不生效）。
///     </para>
/// </summary>
public partial class MainView : ContentPage, IFANavigationPageFactory
{
    /// <summary>
    ///     贴边多少 DIP 以内算「抓到了边缘」，按下即进入缩放。
    ///     <para>
    ///         取 8：顶部那条边同时压在 <c>FAAppWindow</c> 的标题栏带上，6 太窄了不好抓
    ///         （用户反馈左上角与顶部拉不动，很大一部分原因是抓不稳）。
    ///     </para>
    /// </summary>
    private const double ResizeBandThickness = 8;

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
    private readonly Button? _moreButton;

    /// <summary>回放时刻的月历浮层宿主：内容在 XAML 里（<see cref="ReplayCalendarView" />）。</summary>
    private readonly Popup? _replayCalendarPopup;

    /// <summary>浮层里的月历（从 Popup.Child 拿到），Unloaded 时要退掉它的事件。</summary>
    private ReplayCalendarView? _replayCalendar;

    /// <summary>浮层上一次被关掉的时刻（UTC），用来挡掉「light dismiss 与按钮 Click 撞在同一下」。</summary>
    private DateTime _replayCalendarClosedAt;

    private InputElement? _dragHost;
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

    /// <summary>页面级弹层服务（单例）。</summary>
    public PageOverlayService Overlay { get; } = IAppHost.GetService<PageOverlayService>();

    /// <summary>
    ///     壳这一层的弹层状态，绑定在根 <c>Panel</c> 的遮罩上。
    ///     <para>
    ///         绑自己的那一份而不是服务上那两个属性：宿主可以有多个（全屏展示窗口层级更高），
    ///         同一时刻只有一个该显示遮罩。
    ///     </para>
    /// </summary>
    public PageOverlayHost OverlayHost { get; }

    /// <summary>
    ///     收纳服务（单例）：「收纳」按钮把主窗口交给它藏起来、换一颗浮在屏幕上的小按钮。
    ///     窗口显隐与浮钮的摆放都在服务里，壳只负责在按钮被点时把自己所在的窗口递过去。
    /// </summary>
    public BoardDockService Dock { get; } = IAppHost.GetService<BoardDockService>();

    /// <summary>
    ///     时空回放状态。标题栏的控制条直接绑它；主页面也从同一个单例读，
    ///     这样「壳控制、页面显示」两边共享同一份进度。
    /// </summary>
    public BoardReplayService Replay { get; } = IAppHost.GetService<BoardReplayService>();

    /// <summary>
    ///     窗口是否被「固定」（标题栏那颗锁处于锁定态）：位置与大小都锁住，拖动与四边缩放一起失效。
    ///     拖动、缩放、光标三处判定都从这里取，不要在别处另读配置。
    /// </summary>
    private bool IsWindowFixed => ViewModel.Config.Basic.WindowFixed;

    public MainView()
    {
        Current = this;
        DataContext = this;

        // 必须在 InitializeComponent 之前建好：XAML 里的绑定要读到它。
        OverlayHost = Overlay.CreateHost(this, PageOverlayService.ShellHostPriority);

        InitializeComponent();

        _navigationFrame = this.FindControl<FAFrame>("NavigationFrame");
        _titleBar = this.FindControl<Border>("TitleBar");
        _moreButton = this.FindControl<Button>("MoreButton");
        _replayCalendarPopup = this.FindControl<Popup>("ReplayCalendarPopup");

        if (_replayCalendarPopup is { } calendarPopup)
        {
            calendarPopup.Closed += OnReplayCalendarPopupClosed;

            // 月历由 XAML 构造（在 Popup 里，这时还不在可视树上），这里把回放状态传给它，
            // 并把「点了某个时刻」接成收起浮层——用户点完马上就该看到那一刻的板子。
            if (calendarPopup.Child is ReplayCalendarView calendar)
            {
                calendar.Replay = Replay;
                calendar.StopSelected += OnReplayCalendarStopSelected;
                _replayCalendar = calendar;
            }
        }

        if (_navigationFrame is not null)
            _navigationFrame.NavigationPageFactory = this;

        // 初始导航同样等首次布局之后再走，见 OnLoadedForInitialNavigation。
        Loaded += OnLoadedForInitialNavigation;
        Loaded += OnLoadedForDragHost;
        Loaded += OnLoadedForDevelopmentAdorner;

        // 弹层一打开就把可能还在进行的长按/拖动收掉，免得拖状态留着把弹层里的点击吃掉。
        Overlay.PropertyChanged += OnOverlayChanged;
        // 回放结束（超时自动退出等）时，月历浮层不能自己留在屏幕上。
        Replay.PropertyChanged += OnReplayChanged;



        Unloaded += (_, _) =>
        {
            Overlay.PropertyChanged -= OnOverlayChanged;
            Overlay.ReleaseHost(OverlayHost);
            Replay.PropertyChanged -= OnReplayChanged;

            if (_replayCalendar is not null)
            {
                _replayCalendar.StopSelected -= OnReplayCalendarStopSelected;
                _replayCalendar.Replay = null;
                _replayCalendar = null;
            }

            if (_replayCalendarPopup is not null)
                _replayCalendarPopup.Closed -= OnReplayCalendarPopupClosed;

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
    ///     于是窗口最上面一条就成了拖不动的死区。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        SetDragHost(TopLevel.GetTopLevel(this) as InputElement);
    }

    /// <summary>
    ///     兜底：个别时序下 <see cref="OnAttachedToVisualTree" /> 还取不到窗口，首次布局后再解析一次；
    ///     仍取不到就退回挂在本壳上，至少标题栏还能拖动。
    /// </summary>
    private void OnLoadedForDragHost(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedForDragHost;
        SetDragHost(TopLevel.GetTopLevel(this) as InputElement ?? this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        EndWindowDrag();
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
        // 窗口级的边缘兜底：顶部 / 左上角那一条同时压在 FAAppWindow 的标题栏上，
        // 模板自己的处理器可能先把它当成「拖窗口」吃掉（把作业板置底到桌面时，顶部和左边就拉不动了）。
        // 这里在窗口层再加一道隧道监听（handledEventsToo = true），命中边缘就由我们开始缩放。
        if (host is Window windowHost)
            windowHost.AddHandler(PointerPressedEvent, OnWindowEdgeGuard, RoutingStrategies.Tunnel, handledEventsToo: true);

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
            return new TextBlock { Text = string.Format(CR.Common_PageNotFound, info.Id) };

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
            _navigationFrame.NavigateFromObject(info);
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
    ///     窗口级边缘兜底：指针在窗口四边 8 DIP 以内按下时优先开始缩放。
    ///     <para>
    ///         用「相对窗口」的坐标判定，而不是相对 MainView：作业板置底成桌面子窗口后，
    ///         MainView 不一定正好贴着窗口四条边，用 MainView 的 Bounds 判定会让顶部 / 左边永远差一点点、
    ///         点上去变成拖窗口或干脆没反应（用户反馈「置底后顶部和左边拉不动」）。
    ///     </para>
    /// </summary>
    private void OnWindowEdgeGuard(object? sender, PointerPressedEventArgs e)
    {
        if (_isResizingWindow || _isDraggingWindow || Overlay.IsOpen)
            return;

        // 固定窗口时四边也没有缩放热区：不然光标照旧变成双向箭头、按下去却没反应。
        if (IsWindowFixed)
            return;

        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (TopLevel.GetTopLevel(this) is not Window { WindowState: WindowState.Normal } window)
            return;

        var point = e.GetPosition(window);
        if (HitTestResizeSides(point, window.Bounds.Size) == ResizeSides.None)
            return;

        if (TryStartWindowResize(e))
            e.Handled = true;
    }

    /// <summary>
    ///     整窗手势入口：先看边缘缩放，再看标题栏拖动，两样都不中就让给内容。
    ///     <para>
    ///         <b>只有标题栏能拖窗口</b>，而且只在解锁态。板面上按住既不拖窗口也不抢手势——
    ///         作业板上的按住是「拖卡片 / 拖区块排序 / 往编辑器里点」，长按拖动那套与它们处处打架。
    ///     </para>
    /// </summary>
    private void OnPointerPressedForDrag(object? sender, PointerPressedEventArgs e)
    {
        // 上一次拖动如果没收到 PointerReleased / PointerCaptureLost（指针在窗口外抬起、
        // 被别的控件抢走捕获等），状态和捕获会留在壳上，表现为「整窗点不动」。
        // 新的按下就是最好的收尾时机：先把它清干净，再按这次按下重新判定。
        if (e.Handled)
            return;

        EndWindowDrag();
        EndWindowResize();

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && e.Pointer.Type != PointerType.Touch)
            return;

        // 抓边缘优先于拖窗口：主窗口没有原生可缩放边框，贴边这几像素就是缩放热区。
        // 命中后必须标 Handled：顶部与左上角那一条同时落在 FAAppWindow 自己的标题栏带上，
        // 不标的话库的标题栏处理器随后会把这一下当成「拖窗口」接走，表现为顶部与左上角拉不长。
        if (TryStartWindowResize(e))
        {
            e.Handled = true;
            return;
        }

        if (!IsInTitleBar(e.Source) || IsWindowFixed)
            return;

        // 标题栏按下即拖；命中标题栏里的交互控件时让出（导航栏去掉后主界面标题栏暂时没有控件，
        // 但以后加按钮就靠这张排除表，见 IsInteractiveSource）。
        // 拖动开始后同样标 Handled：否则库的原生标题栏拖动可能再接管一次。
        if (IsInteractiveSource(e.Source))
            return;

        StartWindowDrag(e);
        if (_isDraggingWindow)
            e.Handled = true;
    }

    /// <summary>
    ///     标题栏上的「导出」：真正的导出逻辑在作业板页面里（它才拿得到内容容器），
    ///     会先弹格式对话框（导图片还是表格）。
    ///     主页面不是作业板时什么都不做（当前只有作业板一个主页面）。
    /// </summary>
    private async void ExportImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_navigationFrame?.Content is MainPages.Board.BoardPage page)
            await page.ExportAsync();
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

        UpdateResizeCursor(e);
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

    /// <summary>
    ///     拖动开始：接管指针并记下窗口与指针的起点。刻意不用 <c>Window.BeginMoveDrag</c>：
    ///     窗口置底到桌面后是桌面宿主的子窗口，原生标题拖动对它不生效（实测不动），
    ///     而按 <see cref="Window.Position" /> 位移在两种形态下都有效。
    /// </summary>
    private void StartWindowDrag(PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
            return;

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
    }

    /// <summary>
    ///     指针的屏幕物理坐标。<c>Window.PointToScreen</c> 走的是原生 <c>ClientToScreen</c>，
    ///     置底成桌面宿主的子窗口后照样对（子窗口的客户区原点不是屏幕原点，
    ///     自己拿 <c>Window.Position + 偏移</c> 相加会差一个父窗口客户区原点的位移）。
    ///     用「绝对坐标」而不是相对位移，是因为窗口会跟着指针走，相对位移会自我抵消。
    /// </summary>
    private PixelPoint GetPointerScreenPoint(PointerEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Window window)
            return window.PointToScreen(e.GetPosition(window));

        var scaling = topLevel?.RenderScaling ?? 1.0;
        var position = e.GetPosition(this);

        return new PixelPoint(
            (int)Math.Round(position.X * scaling),
            (int)Math.Round(position.Y * scaling));
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
    ///     <para>
    ///         ⚠️ 判定必须用<b>相对窗口</b>的坐标与尺寸（<c>e.GetPosition(window)</c> +
    ///         <c>window.Bounds.Size</c>），不能相对 <see cref="MainView" />：作业板置底到桌面后
    ///         MainView 不一定贴着窗口四条边，用 MainView 的 Bounds 判定会让顶部 / 左边永远差一点点，
    ///         点上去变成拖窗口或干脆没反应（用户反馈「置底后顶部和左边拉不动」）。
    ///         这里曾经与窗口级兜底 <see cref="OnWindowEdgeGuard" /> 用了两套坐标系，表现为
    ///         兜底能命中、走到这里又判不中而直接返回。
    ///     </para>
    /// </summary>
    private bool TryStartWindowResize(PointerPressedEventArgs e)
    {
        if (_isResizingWindow || IsWindowFixed)
            return false;

        if (TopLevel.GetTopLevel(this) is not Window { WindowState: WindowState.Normal } window)
            return false;

        var sides = HitTestResizeSides(e.GetPosition(window), window.Bounds.Size);
        if (sides == ResizeSides.None)
            return false;

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
    ///     传进来的坐标与尺寸必须是<b>相对窗口</b>的（见 <see cref="TryStartWindowResize" />）。
    /// </summary>
    private static ResizeSides HitTestResizeSides(Point point, Size size)
    {
        var width = size.Width;
        var height = size.Height;
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
        // 固定窗口时四边拉不动，光标也不该变成双向箭头（否则是「提示能拉、拉不动」）。
        if (IsWindowFixed)
        {
            if (Cursor is not null)
                Cursor = null;

            return;
        }

        // 与 Scaling / 判定同源，统一用窗口坐标（置底后 MainView 不贴窗口四边，见 TryStartWindowResize）。
        var sides = TopLevel.GetTopLevel(this) is Window window
            ? HitTestResizeSides(e.GetPosition(window), window.Bounds.Size)
            : ResizeSides.None;

        var cursor = sides switch
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
    ///     「看板模式」：点一下直接铺全屏，与托盘菜单里那一项、<c>corkboard://display</c> 是同一个入口。
    ///     <para>
    ///         全屏已经开着时 <see cref="App.ShowDisplayBoardWindow" /> 自己会把那扇窗提到前面，不用在这儿判。
    ///     </para>
    /// </summary>
    private void OpenDisplayModeButton_OnClick(object? sender, RoutedEventArgs e) => App.ShowDisplayBoardWindow();

    /// <summary>
    ///     「更多」：按钮本身只负责弹菜单，弹出前把菜单项调成此刻该有的样子。
    ///     <para>
    ///         显隐与勾选都不走 MVVM：菜单项画在弹出层里、不在这个可视树上，所以在<b>弹出这一刻</b>
    ///         按配置写一遍（<see cref="RefreshMoreMenu" />），而不是绑 MenuItem.IsVisible / IsChecked。
    ///     </para>
    /// </summary>
    private void MoreButton_OnClick(object? sender, RoutedEventArgs e) => RefreshMoreMenu();

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
        RefreshMoreMenu();
    }

    /// <summary>「更多」菜单里四项的类名。认类名不认下标：菜单以后增删项不用回来改数字。</summary>
    private const string MoreSortClass = "more-sort";

    private const string MoreExportClass = "more-export";
    private const string MoreReplayClass = "more-replay";
    private const string MoreDockClass = "more-dock";

    /// <summary>
    ///     按当前配置把「更多」菜单调成该有的样子：四项各自显隐、排序子菜单的标题写着当前档、
    ///     子菜单里勾上当前那一项（配置里是越界值时就一个都不勾）。
    /// </summary>
    private void RefreshMoreMenu()
    {
        if (_moreButton?.Flyout is not MenuFlyout flyout)
            return;

        var current = ViewModel.SelectedSortOption.Mode;

        foreach (var item in flyout.Items.OfType<MenuItem>())
        {
            if (item.Classes.Contains(MoreSortClass))
            {
                item.IsVisible = ViewModel.ShowTitleBarSort;

                // 标题带上当前档（「排序方式：按科目」）：不用点开子菜单就知道现在按什么排。
                item.Header = ViewModel.SortTip;

                foreach (var option in item.Items.OfType<MenuItem>())
                    option.IsChecked = option.Tag is BoardSortMode mode && mode == current;
            }
            else if (item.Classes.Contains(MoreExportClass))
                item.IsVisible = ViewModel.ShowTitleBarExport;
            else if (item.Classes.Contains(MoreReplayClass))
                item.IsVisible = ViewModel.ShowTitleBarReplay;
            else if (item.Classes.Contains(MoreDockClass))
                item.IsVisible = ViewModel.ShowTitleBarDock;
        }
    }

    /// <summary>
    ///     「布置作业」：表单由 <see cref="PageOverlayService" /> 挂到壳这一层显示，
    ///     遮罩才能盖住自绘标题栏（页面里画会漏掉标题栏那一条）。
    /// </summary>
    private void AddAssignmentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OpenAddAssignmentForm();
    }

    /// <summary>
    ///     打开布置作业表单。标题栏那颗「布置作业」与右下角浮标都走它。
    /// </summary>
    public void OpenAddAssignmentForm()
    {
        // 回放期间看的是历史快照，此时不接受新建。
        if (Replay.IsActive)
            return;

        Overlay.Show(new HomeworkAssignmentForm());
    }

    /// <summary>
    ///     解锁态遮罩上那颗「锁定窗口」：把窗口锁回去（<c>WindowFixed</c> 置真）。
    ///     锁上之后遮罩自己消失，板子重新可编辑——它是解锁态里唯一的收尾动作，
    ///     所以必须画在遮罩上，不能让人先去找标题栏那颗锁。
    /// </summary>
    private void LockMaskButton_OnClick(object? sender, RoutedEventArgs e) => ViewModel.IsWindowLocked = true;

    private void ReplayEnterButton_OnClick(object? sender, RoutedEventArgs e) => Replay.Enter();

    private void ReplayExitButton_OnClick(object? sender, RoutedEventArgs e) => Replay.Exit();

    /// <summary>
    ///     「回放时刻」：展开 / 收起月历浮层。
    ///     <para>
    ///         浮层刚被「点外面」收掉的那一下别再弹回来：light dismiss 与这一次按钮 Click
    ///         走的是同一次点击，两个都会触发，不挡一下会表现为「点一下闪一下又开」。
    ///     </para>
    /// </summary>
    private void ReplayCalendarButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_replayCalendarPopup is null)
            return;

        if (!_replayCalendarPopup.IsOpen
            && DateTime.UtcNow - _replayCalendarClosedAt < TimeSpan.FromMilliseconds(250))
            return;

        _replayCalendarPopup.IsOpen = !_replayCalendarPopup.IsOpen;
    }

    /// <summary>月历里点中某个时刻：把浮层收起来，用户马上看到那一刻的板子。</summary>
    private void OnReplayCalendarStopSelected(object? sender, EventArgs e) => CloseReplayCalendar();

    private void OnReplayCalendarPopupClosed(object? sender, EventArgs e) =>
        _replayCalendarClosedAt = DateTime.UtcNow;

    /// <summary>
    ///     回放结束（超时自动退出、或在别处点了退出）时把月历浮层收掉：
    ///     它所在的那一条控制条已经藏起来了，浮层不能自己留在屏幕上。
    /// </summary>
    private void OnReplayChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoardReplayService.IsActive) && !Replay.IsActive)
            CloseReplayCalendar();
    }

    private void CloseReplayCalendar()
    {
        if (_replayCalendarPopup is not null)
            _replayCalendarPopup.IsOpen = false;
    }

    /// <summary>
    ///     「还原到此刻」：把当前停靠点那一刻板子上的作业整份放回当前页面（清掉它们的「已清理」标记）。
    ///     <para>
    ///         先确认：回放里看到的是那一刻的快照，点一下这些作业就会全部重新出现在板子上。
    ///         已经单独删掉的作业不在内存里，还原不回来——文案里说明这一点。
    ///     </para>
    /// </summary>
    private async void ReplayRestoreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel || !Replay.IsActive || Replay.CurrentAt is null)
            return;

        var dialog = new FAContentDialog
        {
            Title = CR.Board_ReplayRestoreConfirmTitle,
            Content = CR.Board_ReplayRestoreConfirmMessage,
            PrimaryButtonText = CR.Board_ReplayRestore,
            CloseButtonText = CR.Common_Cancel,
            DefaultButton = FAContentDialogButton.Close
        };

        // 对话框显示期间让窗口不透明：整窗 alpha 会把对话框一起变透明（见 ModalOpacityScope）。
        using var modalOpacity = ModalOpacityScope.Begin(this);
        if (await dialog.ShowAsync(topLevel) != FAContentDialogResult.Primary)
            return;

        Replay.RestoreCurrentSnapshot();
    }

    /// <summary>
    ///     「收纳」：把主窗口收起来，屏幕上只留一颗展开浮钮（见 <see cref="BoardDockService" />）。
    ///     <para>
    ///         窗口的隐藏 / 恢复、浮钮的显隐与位置都在服务里：视图只把当前所在的窗口递过去，
    ///         不直接碰 <c>Window.Hide</c>，也不管浮钮窗口长什么样。
    ///     </para>
    /// </summary>
    private void DockButton_OnClick(object? sender, RoutedEventArgs e) =>
        Dock.Collapse(TopLevel.GetTopLevel(this) as Window);

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
    ///     命中标题栏里的交互控件时不要启动窗口拖动，否则控件收不到点击。
    ///     <para>
    ///         标题栏现在有「设置 / 布置作业 / 更多」三颗按钮和「锁定」那颗开关，
    ///         所以这张排除表是真在用的：漏掉任何一类控件，那个控件就会变成「点不动」。
    ///     </para>
    ///     <para>
    ///         ⚠️ <see cref="ToggleSwitch" /> 不是 <see cref="Button" /> 的子类，必须单独列出来，
    ///         否则解锁态下按住锁定开关会被判成拖窗口。菜单项画在弹出层里、不在标题栏上，不走这里。
    ///     </para>
    /// </summary>
    private bool IsInteractiveSource(object? source)
    {
        if (source is not Visual visual)
            return false;

        for (Visual? current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Button or ToggleButton or ToggleSwitch or TextBox or ComboBox or Slider)
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
