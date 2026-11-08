using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Corkboard.ViewModels.Actions;

namespace Corkboard.Views.Actions;

/// <summary>
///     列表页：表头 + 两组行（系统日程在前、我的行动在后）+ 空态。
///     行本身是 <c>Border</c>，点空白处打开编辑器；行内的开关与图标按钮自己吃掉指针，不会误触。
/// </summary>
public partial class ActionListView : UserControl
{
    public ActionListView() => InitializeComponent();

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ActionOccurrenceItem item }) return;
        if (DataContext is not ActionWindowViewModel viewModel) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // 开关 / 操作按钮按下时事件已被它们消费，走到这里的才是「点行」。
        viewModel.EditCommand.Execute(item);
    }

    private void OnHistoryPopupClosed(object? sender, EventArgs e)
    {
        if (sender is Popup { DataContext: ActionOccurrenceItem item })
            item.IsHistoryOpen = false;
    }
}
