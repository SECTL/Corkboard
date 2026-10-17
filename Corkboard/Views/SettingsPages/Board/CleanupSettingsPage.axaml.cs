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

/// <summary>「清理」设置页：到期清理、立即清理与清空作业板。</summary>
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
    ///     清空作业板：先确认，再把作业从板子上收起来。收起来只是打「已清理」标记，
    ///     数据全都还在（之后能在时空回放里还原），所以这一步不会失败，也没有备份要管。
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

        string message;
        try
        {
            viewModel.ClearBoard();
            // 说清「只是收起来、数据还在」：不然用户会以为作业被删了。
            message = CR.Settings_Board_ClearAllDone;
        }
        catch (Exception ex)
        {
            // 清空要重写板子的作业文件，磁盘出错时别让异常从 async void 里冒出去。
            System.Diagnostics.Debug.WriteLine($"清空作业板失败：{ex}");
            message = CR.Settings_Board_ClearAllFailed;
        }

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
