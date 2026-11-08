using Avalonia.Controls;
using Avalonia.Interactivity;
using Corkboard.ViewModels.SettingsPages.Duty;

namespace Corkboard.Views.SettingsPages.Duty;

/// <summary>
///     「小组与职务」页的右侧抽屉：三套表单（小组 / 职务 / 固定人员）共用一个视图，
///     真正保存与校验都在 <see cref="DutyGroupsPostsSettingsPageViewModel" /> 里；
///     保存成功后由页面关掉抽屉，这里只处理「取消」。
/// </summary>
public partial class DutyGroupsPostsFormDrawer : UserControl
{
    public DutyGroupsPostsFormDrawer() => InitializeComponent();

    private DutyGroupsPostsSettingsPageViewModel? ViewModel =>
        DataContext as DutyGroupsPostsSettingsPageViewModel;

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CancelEditorCommand.Execute(null);
        SettingsView.Current?.CloseDrawer();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e) =>
        ViewModel?.SaveEditorCommand.Execute(null);
}
