using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Corkboard.ViewModels.SettingsPages.Duty;
using Corkboard.Views.Duty;

namespace Corkboard.Views.SettingsPages.Duty;

/// <summary>
///     当前名单抽屉：改名、看统计与来源、从别处再导入或整份清空。
///     <para>DataContext 复用名单页的 ViewModel；业务动作全在 ViewModel 里，这里只弹确认框与开关抽屉。</para>
/// </summary>
public partial class DutyRosterInfoDrawer : UserControl
{
    public DutyRosterInfoDrawer()
    {
        InitializeComponent();
    }

    private DutyMembersSettingsPageViewModel? ViewModel => DataContext as DutyMembersSettingsPageViewModel;

    private void OnImportClick(object? sender, RoutedEventArgs e) =>
        SettingsView.Current?.OpenDrawer(new DutyImportDrawer());

    private void OnClearClick(object? sender, RoutedEventArgs e) => _ = ClearAsync();

    private async Task ClearAsync()
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var dialog = new FAContentDialog
        {
            Title = "清空名单",
            Content = $"将清空名单“{viewModel.CurrentRosterLabel}”；此操作不可撤销",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Close
        };

        if (await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary)
            viewModel.ClearRoster();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => SettingsView.Current?.CloseDrawer();
}