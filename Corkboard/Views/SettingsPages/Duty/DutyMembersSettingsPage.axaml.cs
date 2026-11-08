using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Duty;
using Corkboard.Views.Duty;

namespace Corkboard.Views.SettingsPages.Duty;

/// <summary>
///     「名单与导入」设置页：值日候选人名单的增删改、搜索与当前名单抽屉。
///     <para>
///         页面只做 UI 接线：按钮开抽屉、删除前弹确认框、把序号冲突弹窗的选择交回 ViewModel。
///         真正的增删改都在 ViewModel 里经 <c>DutyRosterService</c> 完成。
///     </para>
/// </summary>
[PageInfo("settings.duty.members", FluentIcons.PeopleFilled, "settings.duty.group")]
public partial class DutyMembersSettingsPage : UserControl
{
    public DutyMembersSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<DutyMembersSettingsPageViewModel>();

        // ViewModel 是 transient、配置 handler 与领域服务是单例：订阅必须在离开可视树时断开。
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private DutyMembersSettingsPageViewModel? ViewModel => DataContext as DutyMembersSettingsPageViewModel;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.Attach();
        viewModel.ImportRequested += OnImportRequested;
        viewModel.Saved += OnMemberSaved;
        viewModel.SequenceConflictRequested += OnSequenceConflictRequested;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.Detach();
        viewModel.ImportRequested -= OnImportRequested;
        viewModel.Saved -= OnMemberSaved;
        viewModel.SequenceConflictRequested -= OnSequenceConflictRequested;
    }

    /// <summary>成员保存 / 名单重命名成功：抽屉那边该做的事已经做完，这里统一收起抽屉。</summary>
    private void OnMemberSaved() => SettingsView.Current?.CloseDrawer();

    /// <summary>工具栏「当前名单」：打开名单信息抽屉（名称、来源、统计、清空）。</summary>
    private void OnShowRosterClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.PrepareRosterDrawer();
        SettingsView.Current?.OpenDrawer(new DutyRosterInfoDrawer { DataContext = viewModel });
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.Reload();
        viewModel.StatusText = "已刷新";
    }

    private void OnAddMemberClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.BeginAdd();
        SettingsView.Current?.OpenDrawer(new DutyMemberFormDrawer { DataContext = viewModel });
    }

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: DutyMemberItem item } || ViewModel is not { } viewModel)
            return;

        viewModel.BeginEdit(item);
        SettingsView.Current?.OpenDrawer(new DutyMemberFormDrawer { DataContext = viewModel });
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DutyMemberItem item })
            _ = DeleteMemberAsync(item);
    }

    private async Task DeleteMemberAsync(DutyMemberItem item)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var dialog = new FAContentDialog
        {
            Title = "删除成员",
            Content = $"将从名单“{viewModel.CurrentRosterLabel}”中删除成员“{item.DisplayName}”；此操作不可撤销",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Close
        };

        if (await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary)
            viewModel.Remove(item.RecordId);
    }

    private void OnImportClick(object? sender, RoutedEventArgs e) => OnImportRequested();

    private void OnImportRequested() => SettingsView.Current?.OpenDrawer(new DutyImportDrawer());

    private void OnExportClick(object? sender, RoutedEventArgs e) =>
        SettingsView.Current?.OpenDrawer(new DutyExportDrawer());

    /// <summary>内联编辑提交：只把这一行写回服务，不整表刷新（会丢焦点）。</summary>
    private void OnCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
            return;

        if (e.Row.DataContext is DutyMemberItem item)
            ViewModel?.CommitRow(item);
    }

    /// <summary>新增成员时序号冲突：弹四档确认框，再把选择交回 ViewModel。</summary>
    private async void OnSequenceConflictRequested(DutySequenceConflict conflict, System.Collections.Generic.IReadOnlyList<string> displayNames)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var choice = await DutySequenceConflictPrompt.ShowAsync(
            topLevel, conflict, displayNames, DutySequenceConflictPrompt.CreateDefaultText());
        viewModel.ResolvePendingAddConflict(choice);
    }
}