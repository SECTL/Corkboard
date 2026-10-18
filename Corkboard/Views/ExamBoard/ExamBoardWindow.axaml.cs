using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Corkboard.Core.Abstraction;
using Corkboard.ViewModels.ExamBoard;

namespace Corkboard.Views.ExamBoard;

/// <summary>
///     全屏考试看板窗口：时钟、考试表格、标题副标题，左下角长按退出。
///     <para>
///         退出刻意做成「按住 2 秒」而不是点一下：看板挂在教室 / 书房的大屏上，
///         鼠标划过去、投影笔误触都不该把它关掉（用户明确要求的交互）。
///         进度与配色都归 ViewModel 管（<c>BeginHold</c> / <c>CancelHold</c>），这里只负责
///         把指针事件转过去、并在 <c>ExitRequested</c> 到来时关窗——窗口里不碰任何具名控件。
///     </para>
/// </summary>
public partial class ExamBoardWindow : Window
{
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
            viewModel.Attach();
        };
        Closed += (_, _) =>
        {
            if (ViewModel is not { } viewModel)
                return;

            viewModel.ExitRequested -= OnExitRequested;
            viewModel.Detach();
        };
    }

    private ExamBoardViewModel? ViewModel => DataContext as ExamBoardViewModel;

    /// <summary>长按走满 2 秒：关掉看板（主窗口的置底 / 穿透状态一概不碰）。</summary>
    private void OnExitRequested(object? sender, EventArgs e) => Close();

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

    /// <summary>松手：进度圈退回零，这次长按作废。</summary>
    private void OnExitHintPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        ViewModel?.CancelHold();
    }

    /// <summary>指针被别处抢走（窗口失焦、弹窗弹出）：同样算松手，不然计时器还会自己走完。</summary>
    private void OnExitHintCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ViewModel?.CancelHold();
}
