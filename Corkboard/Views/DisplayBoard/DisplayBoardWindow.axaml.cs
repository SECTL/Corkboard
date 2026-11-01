using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Display;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.Display;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Ui;
using Corkboard.ViewModels.DisplayBoard;
using Corkboard.Views.MainPages.Board;
using Corkboard.Views.MainPages.Homework;

namespace Corkboard.Views.DisplayBoard;

/// <summary>
///     全屏展示窗口。ViewModel 只管时钟与显隐开关，指针相关的三件事（长按退出、光标自己藏、弹层宿主）
///     与自动轮播留在 code-behind——它们都要 <see cref="TopLevel" />、可视树与指针捕获，
///     塞进 ViewModel 反而不清楚。
/// </summary>
public partial class DisplayBoardWindow : Window
{
    private readonly PageOverlayService _overlay;
    private readonly PageOverlayHost _overlayHost;

    /// <summary>自动轮播的节拍器：每停一会儿把作业往下翻一页。</summary>
    private DispatcherTimer? _carouselTimer;

    /// <summary>
    ///     最后一次指针活动（移动 / 按下）的时刻，UTC。
    ///     <para>
    ///         ⚠️ 以前这里是 <c>PointerEntered</c> / <c>PointerExited</c> 的「悬停就暂停」，而作业区几乎占满
    ///         整块屏幕——鼠标停在板子上（看板模式那块屏基本都这样）时就<b>永远</b>不恢复，用户看到的就是
    ///         「轮播功能没有」。现在改成「动过之后歇一会儿」（<see cref="DisplayCarousel.PauseAfterActivity" />），
    ///         鼠标杵着不动照样翻页。
    ///     </para>
    /// </summary>
    private DateTime _lastPointerActivity = DateTime.MinValue;

    public DisplayBoardWindow()
    {
        InitializeComponent();

        ViewModel = IAppHost.GetService<DisplayBoardViewModel>();
        DataContext = ViewModel;

        // 弹层要画在**这扇窗**里：弹层服务只有一个，默认落点是那块被全屏挡住的主窗口。
        _overlay = IAppHost.GetService<PageOverlayService>();
        _overlayHost = _overlay.CreateHost(this, PageOverlayService.FullScreenHostPriority);
        _overlayHost.PropertyChanged += OnOverlayHostChanged;

        Opened += OnOpened;
        Closed += OnClosed;

        // 时钟那一块能用多宽，得按窗口实际宽度算（480 号的数字比屏幕还宽，没有上限就会被裁）。
        SizeChanged += (_, _) => UpdateClockMaxWidth();

        // 光标自己藏起来：整窗都要收指针移动，所以挂在窗口上而不是某一颗控件上。
        // 同一个时间戳还决定了「现在要不要暂停轮播」——指针在动就是在用，别自己翻页。
        AddHandler(PointerMovedEvent, OnAnyPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);

        // 一言那块的高度决定作业区底部留多少边界，它的显隐与高度都可能在运行中变（换配置 / 换文案换行）。
        if (this.FindControl<StackPanel>("QuoteBar") is { } quoteBar)
            quoteBar.PropertyChanged += OnQuoteBarChanged;
    }

    private DisplayBoardViewModel? ViewModel { get; }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.ExitRequested += OnExitRequested;
        viewModel.Settings.PropertyChanged += OnSettingsChanged;
        viewModel.Attach();

        UpdateClockMaxWidth();
        SyncBoardHostState();
        UpdateBoardBottomInset();
        RestartCarousel();
    }

    /// <summary>
    ///     给时钟那一块算个宽度上限：作业还在时最多占屏宽的 45%（右边留给作业），
    ///     只剩时钟时放到 92%（就是一块大钟表）。字号再大，Viewbox 也会按这个上限等比缩下来。
    /// </summary>
    private void UpdateClockMaxWidth()
    {
        if (ViewModel is not { } viewModel)
            return;

        var width = Bounds.Width;
        if (width <= 0)
            return;

        viewModel.ClockMaxWidth = width * (viewModel.ShowBoard ? 0.45 : 0.92);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _carouselTimer?.Stop();
        _carouselTimer = null;

        if (ViewModel is { } viewModel)
        {
            viewModel.ExitRequested -= OnExitRequested;
            viewModel.Settings.PropertyChanged -= OnSettingsChanged;
            viewModel.Detach();
        }

        _overlayHost.PropertyChanged -= OnOverlayHostChanged;
        _overlay.ReleaseHost(_overlayHost);
    }

    /// <summary>设置页改「允许编辑作业」「只看今天到期」「轮播」时实时同步，不用关掉重开这扇窗。</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            or nameof(DisplaySettingsConfig.Editable)
            or nameof(DisplaySettingsConfig.BoardScope))
            SyncBoardHostState();

        // 关掉作业之后时钟可以铺得更宽，上限跟着换一档。
        if (e.PropertyName is null or nameof(DisplaySettingsConfig.ShowBoard))
            UpdateClockMaxWidth();

        if (e.PropertyName is null
            or nameof(DisplaySettingsConfig.AutoScroll)
            or nameof(DisplaySettingsConfig.AutoScrollPauseSeconds))
            RestartCarousel();
    }

    private void OnExitRequested(object? sender, EventArgs e) => Close();

    private void OnAnyPointerMoved(object? sender, PointerEventArgs e)
    {
        _lastPointerActivity = DateTime.UtcNow;
        ViewModel?.NotifyPointerActivity();
    }

    /// <summary>按一下也算「在用」：触摸屏上手指落下去不一定带一次移动。</summary>
    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _lastPointerActivity = DateTime.UtcNow;

    /// <summary>
    ///     作业区底部给屏幕上那一行一言让出边界：一言多高，作业区就短多少。
    ///     <para>
    ///         没有这条边界时，翻到最后一页卡片会钻到那行字底下（用户说的「它可能到一言的部分去了」）。
    ///         高度直接读 <see cref="StackPanel" /> 自己的 <c>Bounds</c> 与 <c>Margin</c>，
    ///         XAML 里改内边距这里自动跟上；一言关掉（<c>IsVisible=False</c>）时边界归零、作业区摊开。
    ///     </para>
    /// </summary>
    private void UpdateBoardBottomInset()
    {
        if (this.FindControl<Grid>("BoardHost") is not { } host)
            return;

        var inset = 0d;
        if (this.FindControl<StackPanel>("QuoteBar") is { IsVisible: true, Bounds.Height: > 0 } quoteBar)
            inset = quoteBar.Bounds.Height + quoteBar.Margin.Bottom + DisplayBoardMetrics.BoardBottomGap;

        // 只在真的变了才写：这个方法是挂在布局属性变化上的，无条件赋值会把布局拖进死循环。
        var margin = new Thickness(host.Margin.Left, host.Margin.Top, host.Margin.Right, inset);
        if (!host.Margin.Equals(margin))
            host.Margin = margin;
    }

    private void OnQuoteBarChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty || e.Property == BoundsProperty)
            UpdateBoardBottomInset();
    }

    /// <summary>
    ///     把宿主侧的三件事搬到嵌入的那块板子上：只读、只看今天要交的、空板文案。
    ///     只影响这一块（它是 transient 的独立实例），主窗口那块照旧。
    /// </summary>
    private void SyncBoardHostState()
    {
        if (this.FindControl<BoardPage>("EmbeddedBoard") is not { } board)
            return;

        if (IAppHost.TryGetService<MainConfigHandler>() is not { } handler)
            return;

        var settings = handler.Data.Display;
        board.IsReadOnlyHost = !settings.Editable;
        board.ShowDueTodayOnlyHost = settings.BoardScope == DisplayBoardScope.DueToday;
        board.EmptyHintOverrideHost = Core.Langs.Common.Resources.Display_EmptyBoard;
    }

    /// <summary>按当前配置重排轮播节拍：关掉就停，间隔变了就换。</summary>
    private void RestartCarousel()
    {
        _carouselTimer?.Stop();

        if (ViewModel?.Settings is not { AutoScroll: true } settings)
            return;

        var seconds = Math.Clamp(
            settings.AutoScrollPauseSeconds,
            DisplaySettingsConfig.MinAutoScrollPauseSeconds,
            DisplaySettingsConfig.MaxAutoScrollPauseSeconds);

        _carouselTimer ??= new DispatcherTimer();
        _carouselTimer.Tick -= OnCarouselTick;
        _carouselTimer.Tick += OnCarouselTick;
        _carouselTimer.Interval = TimeSpan.FromSeconds(seconds);
        _carouselTimer.Start();
    }

    /// <summary>
    ///     往下翻一页：落点由 <see cref="DisplayCarousel.NextOffset" /> 算——尽量对齐区块卡片的顶边
    ///     （不把卡片切一半），到底了贴底露完最后那截，再下一拍回顶部从头轮。
    ///     <para>
    ///         一屏放得下、弹层开着、指针刚动过（用户在看 / 在操作）时都不动。
    ///     </para>
    /// </summary>
    private void OnCarouselTick(object? sender, EventArgs e)
    {
        if (_overlayHost.IsOpen || ViewModel is not { ShowBoard: true })
            return;

        if (DateTime.UtcNow - _lastPointerActivity < DisplayCarousel.PauseAfterActivity)
            return;

        if (this.FindControl<BoardPage>("EmbeddedBoard") is not { } board
            || board.NotesScroller is not { } scroller)
            return;

        var next = DisplayCarousel.NextOffset(
            scroller.Offset.Y,
            scroller.Viewport.Height,
            scroller.Extent.Height,
            board.GetSectionAnchors());

        scroller.Offset = new Vector(scroller.Offset.X, next);
    }

    private void OnOverlayHostChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or nameof(PageOverlayHost.IsOpen)))
            return;

        OverlayScrim.IsVisible = _overlayHost.IsOpen;
        OverlayContentHost.Content = _overlayHost.Content;
    }

    private void OnExitHintPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not InputElement host)
            return;

        var point = e.GetCurrentPoint(host);
        if (!point.Properties.IsLeftButtonPressed && e.Pointer.Type != PointerType.Touch)
            return;

        e.Pointer.Capture(host);
        ViewModel?.BeginHold();
        e.Handled = true;
    }

    private void OnExitHintPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        ViewModel?.CancelHold();
    }

    private void OnExitHintCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ViewModel?.CancelHold();

    /// <summary>
    ///     右下角那颗「布置作业」：与卡片上的「编辑」走同一个弹层服务，
    ///     会挑层级最高的宿主——也就是这扇全屏窗口（<see cref="PageOverlayService.FullScreenHostPriority" />），
    ///     表单不会跑到被全屏挡住的主窗口上去。
    ///     <para>
    ///         按钮只在「允许布置作业」开着时画（<see cref="DisplayBoardViewModel.ShowAssignButton" />），
    ///         这里再挡一道：关掉之后不接新建（「允许编辑作业」只管改 / 删，与它是两个开关）。
    ///     </para>
    /// </summary>
    private void OnAddAssignmentClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { ShowAssignButton: true })
            return;

        // 已经有一张表单开着时再点一下不该叠第二张（遮罩会越叠越暗、表单也会各存各的）。
        if (_overlayHost.IsOpen)
            return;

        _overlay.Show(new HomeworkAssignmentForm());
    }
}
