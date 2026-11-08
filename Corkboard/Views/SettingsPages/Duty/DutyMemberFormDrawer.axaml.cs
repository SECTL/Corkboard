using Avalonia.Controls;
using Avalonia.Interactivity;
using Corkboard.ViewModels.SettingsPages.Duty;

namespace Corkboard.Views.SettingsPages.Duty;

/// <summary>添加 / 编辑成员的右侧抽屉，DataContext 复用名单页的 ViewModel。</summary>
public partial class DutyMemberFormDrawer : UserControl
{
    public DutyMemberFormDrawer()
    {
        InitializeComponent();
    }

    private DutyMembersSettingsPageViewModel? ViewModel => DataContext as DutyMembersSettingsPageViewModel;

    private void OnSaveClick(object? sender, RoutedEventArgs e) => ViewModel?.SubmitMemberCommand.Execute(null);

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CancelEditCommand.Execute(null);
        SettingsView.Current?.CloseDrawer();
    }
}
