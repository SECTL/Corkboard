using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.Services.Ui;
using Corkboard.ViewModels.SettingsPages.Board;
using FluentAvalonia.UI.Controls;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.SettingsPages.Board;

/// <summary>「清理与备份」设置页：到期清理、立即清理、清空作业板与自动备份清单。</summary>
[PageInfo("settings.board.cleanup", FluentIcons.DeleteFilled, "settings.board.group")]
public partial class CleanupSettingsPage : UserControl
{
    public CleanupSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<CleanupSettingsPageViewModel>();

        // 清理时刻是草稿式的：用户可能没让选择器失焦就关了页面，离开时补交一次。
        Unloaded += (_, _) => ViewModel?.CommitCleanup();
    }

    private CleanupSettingsPageViewModel? ViewModel => DataContext as CleanupSettingsPageViewModel;

    /// <summary>时刻选择器失焦就把草稿提交掉（理由同其它设置页的输入框）。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitCleanup();

    /// <summary>
    ///     清空作业板：先确认，再备份，备份成功才真的清。
    ///     备份失败时只提示、不动数据——「宁可这次不清，也不能丢作业」。
    /// </summary>
    private async void OnClearAllClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var confirm = new FAContentDialog
        {
            Title = CR.Settings_Board_ClearAllConfirmTitle,
            Content = CR.Settings_Board_ClearAllConfirmMessage,
            PrimaryButtonText = CR.Settings_Board_ClearAll,
            CloseButtonText = CR.Common_Cancel,
            DefaultButton = FAContentDialogButton.Close
        };

        // 主窗口可能设了不透明度，对话框期间要让位给 1，否则框自己也是半透明的。
        using var modalOpacity = ModalOpacityScope.Begin(this);
        if (await confirm.ShowAsync(topLevel) != FAContentDialogResult.Primary)
            return;

        var result = await viewModel.ClearBoardAsync();

        // 成功说备份留在哪个目录（只报目录名更友好）；失败说这次没有清空。
        var message = result.Success
            ? string.Format(CR.Settings_Board_ClearAllDone, Path.GetFileName(result.Directory))
            : CR.Settings_Board_ClearAllFailed;

        await new FAContentDialog
        {
            Title = CR.Settings_Board_ClearAll,
            Content = message,
            CloseButtonText = CR.Common_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(topLevel);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
