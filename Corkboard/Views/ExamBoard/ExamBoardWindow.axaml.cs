using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Corkboard.Core.Abstraction;
using Corkboard.ViewModels.ExamBoard;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.ExamBoard;

/// <summary>
///     全屏考试看板窗口：时钟、考试表格、标题副标题，左下角长按退出。
///     <para>
///         退出刻意做成「按住 2 秒」而不是点一下：看板挂在教室 / 书房的大屏上，
///         鼠标划过去、投影笔误触都不该把它关掉（用户明确要求的交互）。
///         按住期间文字换成「松开取消」，松手或失去指针捕获都会取消。
///     </para>
/// </summary>
public partial class ExamBoardWindow : Window
{
    /// <summary>长按判定的时长。短了容易误触，长了退出显得费劲。</summary>
    private static readonly TimeSpan HoldToExitDelay = TimeSpan.FromSeconds(2);

    private DispatcherTimer? _holdTimer;

    public ExamBoardWindow()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamBoardViewModel>();

        // ViewModel 是 transient、服务是单例：订阅与计时器必须在窗口关闭时一起收掉，
        // 否则每开一次看板就多挂一份处理器、多跑一个每秒的计时器。
        Opened += (_, _) => ViewModel?.Attach();
        Closed += (_, _) =>
        {
            CancelHold();
            ViewModel?.Detach();
        };
    }

    private ExamBoardViewModel? ViewModel => DataContext as ExamBoardViewModel;

    /// <summary>按下左下角热区：开始计时，满 2 秒就关窗口。</summary>
    private void OnExitHintPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border)
            return;

        // 只有主键 / 触摸才算「按住」；右键、中键不该能退出。
        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed && e.Pointer.Type != PointerType.Touch)
            return;

        e.Pointer.Capture(border);
        StartHold(border);
        e.Handled = true;
    }

    /// <summary>松手：取消这次长按。</summary>
    private void OnExitHintPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        CancelHold();
    }

    /// <summary>指针被别处抢走（窗口失焦、弹窗弹出）：同样算松手，不然计时器还会自己走完。</summary>
    private void OnExitHintCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CancelHold();

    private void StartHold(Border border)
    {
        CancelHold();

        border.Classes.Add("holding");
        ExitHintText.Text = CR.Exam_Board_ExitHintActive;

        _holdTimer = new DispatcherTimer { Interval = HoldToExitDelay };
        _holdTimer.Tick += (_, _) =>
        {
            CancelHold();
            Close();
        };
        _holdTimer.Start();
    }

    private void CancelHold()
    {
        if (_holdTimer is { } timer)
        {
            timer.Stop();
            _holdTimer = null;
        }

        ExitHint.Classes.Remove("holding");
        ExitHintText.Text = CR.Exam_Board_ExitHint;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
