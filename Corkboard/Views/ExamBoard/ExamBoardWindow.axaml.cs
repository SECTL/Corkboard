using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.ViewModels.ExamBoard;

namespace Corkboard.Views.ExamBoard;

/// <summary>
///     全屏考试看板窗口：时钟、考试表格、标题副标题，左下角长按退出（或单击最小化）。
///     <para>
///         退出刻意做成「按住 2 秒」而不是点一下：看板挂在教室 / 书房的大屏上，
///         鼠标划过去、投影笔误触都不该把它关掉（用户明确要求的交互）。
///         进度与配色都归 ViewModel 管（<c>BeginHold</c> / <c>FinishCornerPress</c>），这里只负责
///         把指针事件转过去、并在 <c>ExitRequested</c> 到来时关窗——窗口里不碰任何具名控件。
///     </para>
///     <para>
///         「最小化」是考试场景下的一条新路：设置里打开之后，那颗按钮改成把看板**收起来**
///         （本窗口 Hide 掉），屏幕上只剩一颗浮窗按钮（<see cref="ExamBoardFloatingWindow" />）；
///         点浮窗回来、按住浮窗 2 秒才真的关掉看板。窗口自己管这颗浮窗——不留额外的单例服务，
///         因为「看板收起来了没有」本来就是这扇窗的状态。
///     </para>
/// </summary>
public partial class ExamBoardWindow : Window
{
    private ExamBoardFloatingWindow? _floatingWindow;

    public ExamBoardWindow()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamBoardViewModel>();

        // ViewModel 是 transient、服务是单例：订阅与计时器必须在窗口关闭时一起收掉，
        // 否则每开一次看板就多挂一份处理器、多跑一个每秒的计时器。
        Opened += (_, _) =>
        {
            if (ViewModel is not { } viewModel)
                return;

            viewModel.ExitRequested += OnExitRequested;
            viewModel.MinimizeRequested += OnMinimizeRequested;
            viewModel.Attach();
        };
        Closed += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.ExitRequested -= OnExitRequested;
                viewModel.MinimizeRequested -= OnMinimizeRequested;
                viewModel.Detach();
            }

            CloseFloatingWindow();
        };
    }

    private ExamBoardViewModel? ViewModel => DataContext as ExamBoardViewModel;

    /// <summary>
    ///     是不是「收起来了」：窗口本身藏起来了、屏幕上只剩浮窗按钮。
    ///     <see cref="App.IsExamBoardWindowOpen" /> 要把它算成「看板还在」——不然自动放映的看门狗
    ///     会把最小化当成用户关窗，结束放映、下一拍又弹一个回来。
    /// </summary>
    public bool IsMinimized { get; private set; }

    /// <summary>长按走满 2 秒：关掉看板（主窗口的置底状态一概不碰）。</summary>
    private void OnExitRequested(object? sender, EventArgs e) => Close();

    /// <summary>单击（或按满手势时长）：把看板收起来，换成屏幕上的那颗浮窗按钮。</summary>
    private void OnMinimizeRequested(object? sender, EventArgs e) => Minimize();

    /// <summary>把看板收起来。已经收着时什么都不做。</summary>
    public void Minimize()
    {
        if (IsMinimized)
            return;

        IsMinimized = true;
        ViewModel?.CancelHold();

        Hide();
        ShowFloatingWindow();
    }

    /// <summary>
    ///     从浮窗按钮回到看板。没在最小化状态时什么都不做——
    ///     托盘 / 自动放映那边无条件调它也不会把已经开着的看板重新显示一遍。
    /// </summary>
    public void RestoreFromMinimize()
    {
        if (!IsMinimized)
            return;

        IsMinimized = false;
        HideFloatingWindow();

        // 藏起来之前是整屏，回来仍然要整屏。⚠️ 顺序是「先 Show 再设 FullScreen」：
        // 对一扇已经 Hide 掉的窗口先改 WindowState、再 Show，Windows 那边有一次
        // 状态没落地的机会（表现就是「点了浮窗什么都不出现」）。
        if (!IsVisible)
            Show();

        // 走一遍 Normal 再设回 FullScreen：窗口藏着的这段时间里 WindowState 一直是 FullScreen，
        // 不先摘一次的话「设成全屏」这一下是空操作，Windows 那边的样式（无边框 + 铺满）不会被重新应用。
        WindowState = WindowState.Normal;
        WindowState = WindowState.FullScreen;

        // 从别的窗口堆叠里提到最前：浮窗是 Topmost，看板刚显示出来可能压在它后面。
        Topmost = true;
        Topmost = false;
        Activate();
    }

    /// <summary>
    ///     浮窗按钮露出来之前先把它的外观按当前设置刷一遍：文字、缩放、位置都是用户可改的，
    ///     而它是最小化那一刻才建出来的。
    /// </summary>
    private void ShowFloatingWindow()
    {
        if (ViewModel is not { } viewModel)
            return;

        _floatingWindow ??= CreateFloatingWindow();

        // 订阅在浮窗自己身上（它在最小化期间一直活着），看板这边只负责把它叫出来。
        _floatingWindow.Attach(viewModel.Settings);

        if (!_floatingWindow.IsVisible)
            _floatingWindow.Show();

        // Show 之后才排完版（SizeToContent），再摆一次位置。
        _floatingWindow.Reposition();
    }

    private void HideFloatingWindow()
    {
        if (_floatingWindow is { IsVisible: true } window)
            window.Hide();
    }

    /// <summary>看板窗口关掉时浮窗必须一起收掉，否则屏幕上会留一颗点不动的孤儿按钮。</summary>
    private void CloseFloatingWindow()
    {
        var window = _floatingWindow;
        _floatingWindow = null;

        if (window is null)
            return;

        window.RestoreRequested -= OnFloatingRestoreRequested;
        window.ExitRequested -= OnFloatingExitRequested;
        window.Detach();
        window.Close();
    }

    private ExamBoardFloatingWindow CreateFloatingWindow()
    {
        var window = new ExamBoardFloatingWindow();
        window.RestoreRequested += OnFloatingRestoreRequested;
        window.ExitRequested += OnFloatingExitRequested;
        return window;
    }

    /// <summary>点浮窗：回到看板。</summary>
    private void OnFloatingRestoreRequested(object? sender, EventArgs e) => RestoreFromMinimize();

    /// <summary>按住浮窗满 2 秒：真的关掉看板（浮窗由 <c>Closed</c> 收掉）。</summary>
    private void OnFloatingExitRequested(object? sender, EventArgs e) => Close();

    /// <summary>按下左下角那颗图标：开始计时，进度圈从零长起。</summary>
    private void OnExitHintPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control host)
            return;

        // 只有主键 / 触摸才算「按住」；右键、中键不该能退出。
        if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed && e.Pointer.Type != PointerType.Touch)
            return;

        e.Pointer.Capture(host);
        ViewModel?.BeginHold();
        e.Handled = true;
    }

    /// <summary>
    ///     松手：长按走满的话 ViewModel 已经在计时器里发了退出请求（窗口正在关），
    ///     没走满则交给 ViewModel 判断——手势是「单击最小化」时它会发最小化请求。
    ///     <para>
    ///         ⚠️ **先问 ViewModel、再放掉捕获**：<c>Capture(null)</c> 会同步触发
    ///         <c>PointerCaptureLost</c> → <c>CancelHold()</c> 把 <c>IsHolding</c> 清掉，
    ///         反过来写的话「单击最小化」永远判不出这一下是不是点击（点击看着毫无反应）。
    ///     </para>
    /// </summary>
    private void OnExitHintPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ViewModel?.FinishCornerPress();
        e.Pointer.Capture(null);
    }

    /// <summary>指针被别处抢走（窗口失焦、弹窗弹出）：同样算松手，不然计时器还会自己走完。</summary>
    private void OnExitHintCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ViewModel?.CancelHold();
}
