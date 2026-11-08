using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Duty;

namespace Corkboard.Views.SettingsPages.Duty;

/// <summary>
///     「小组与职务」设置页：命令栏开新增表单，行内的上移/下移/改名/删除按钮在这里处理
///     （要拿 TopLevel 弹确认框），真正的增删改都在 ViewModel 里经 <c>DutyRosterService</c> 完成。
/// </summary>
[PageInfo("settings.duty.groups", FluentIcons.PeopleFilled, "settings.duty.group")]
public partial class DutyGroupsPostsSettingsPage : UserControl
{
    public DutyGroupsPostsSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<DutyGroupsPostsSettingsPageViewModel>();

        // ViewModel 是 transient、配置 handler 与领域服务是单例：订阅必须在离开可视树时断开。
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private DutyGroupsPostsSettingsPageViewModel? ViewModel =>
        DataContext as DutyGroupsPostsSettingsPageViewModel;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.Attach();
        viewModel.Saved += OnEditorSaved;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.Detach();
        viewModel.Saved -= OnEditorSaved;
    }

    /// <summary>抽屉里保存成功：收起抽屉（状态行留在页面上）。</summary>
    private void OnEditorSaved() => SettingsView.Current?.CloseDrawer();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => ViewModel?.Reload();

    private void OnAddGroupClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.BeginAddGroup();
        SettingsView.Current?.OpenDrawer(new DutyGroupsPostsFormDrawer { DataContext = viewModel });
    }

    private void OnAddPostClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.BeginAddPost();
        SettingsView.Current?.OpenDrawer(new DutyGroupsPostsFormDrawer { DataContext = viewModel });
    }

    private void OnAddFixedClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || !viewModel.BeginAddFixedAssignment())
            return;

        SettingsView.Current?.OpenDrawer(new DutyGroupsPostsFormDrawer { DataContext = viewModel });
    }

    private void OnGroupUpClick(object? sender, RoutedEventArgs e) => MoveGroup(sender, -1);

    private void OnGroupDownClick(object? sender, RoutedEventArgs e) => MoveGroup(sender, +1);

    private void MoveGroup(object? sender, int delta)
    {
        if (sender is Control { DataContext: DutyGroupRow row })
            ViewModel?.MoveGroup(row, delta);
    }

    private void OnGroupRenameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: DutyGroupRow row } || ViewModel is not { } viewModel)
            return;

        viewModel.BeginEditGroup(row);
        SettingsView.Current?.OpenDrawer(new DutyGroupsPostsFormDrawer { DataContext = viewModel });
    }

    private async void OnGroupDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: DutyGroupRow row } || ViewModel is not { } viewModel)
            return;

        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var dialog = new FAContentDialog
        {
            Title = "删除小组",
            Content = $"确定删除小组「{row.Name}」吗？组内成员会变回「未分组」，排班要重新生成。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        };

        if (await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary)
            viewModel.RemoveGroup(row);
    }

    private void OnPostUpClick(object? sender, RoutedEventArgs e) => MovePost(sender, -1);

    private void OnPostDownClick(object? sender, RoutedEventArgs e) => MovePost(sender, +1);

    private void MovePost(object? sender, int delta)
    {
        if (sender is Control { DataContext: DutyPostRow row })
            ViewModel?.MovePost(row, delta);
    }

    private void OnPostEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: DutyPostRow row } || ViewModel is not { } viewModel)
            return;

        viewModel.BeginEditPost(row);
        SettingsView.Current?.OpenDrawer(new DutyGroupsPostsFormDrawer { DataContext = viewModel });
    }

    private async void OnPostDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: DutyPostRow row } || ViewModel is not { } viewModel)
            return;

        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var dialog = new FAContentDialog
        {
            Title = "删除职务",
            Content = $"确定删除职务「{row.Name}」吗？各小组里这个职务的分工、固定人员与相关兑换会一起清掉。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        };

        if (await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary)
            viewModel.RemovePost(row);
    }

    private void OnFixedDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DutyFixedAssignmentRow row })
            ViewModel?.RemoveFixedAssignment(row);
    }
}
