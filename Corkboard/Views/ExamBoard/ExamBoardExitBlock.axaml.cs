using Avalonia.Controls;
using Avalonia.Input;
using Corkboard.ViewModels.ExamBoard;

namespace Corkboard.Views.ExamBoard;

public partial class ExamBoardExitBlock : UserControl
{
    public ExamBoardExitBlock()
    {
        InitializeComponent();
    }

    private ExamBoardViewModel? ViewModel => DataContext as ExamBoardViewModel;

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
    ///     ⚠️ 先问 ViewModel、再放掉捕获（Capture(null) 会同步触发 PointerCaptureLost → CancelHold）。
    /// </summary>
    private void OnExitHintPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ViewModel?.FinishCornerPress();
        e.Pointer.Capture(null);
    }

    /// <summary>指针被别处抢走（窗口失焦、弹窗弹出）：同样算松手。</summary>
    private void OnExitHintCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ViewModel?.CancelHold();
}