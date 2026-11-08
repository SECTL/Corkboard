using Avalonia.Controls;
using Avalonia.Input;
using Corkboard.ViewModels.Actions;

namespace Corkboard.Views.Actions;

/// <summary>月视图：42 格日格 + 下方明细，格与明细行都能点开编辑器 / 只读详情。</summary>
public partial class ActionMonthView : UserControl
{
    public ActionMonthView() => InitializeComponent();

    private void OnDetailRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ActionOccurrenceItem item }) return;
        if (DataContext is not ActionWindowViewModel viewModel) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        viewModel.EditCommand.Execute(item);
    }
}
