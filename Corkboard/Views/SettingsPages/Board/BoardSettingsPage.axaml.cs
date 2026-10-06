using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.Core.Models.Board;
using Corkboard.ViewModels.SettingsPages;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.SettingsPages.Board;

[PageInfo("settings.board", FluentIcons.BoardFilled)]
public partial class BoardSettingsPage : UserControl
{
    public BoardSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<BoardSettingsPageViewModel>();

        // 离开页面时把草稿提交掉：名称与科目是草稿式的，用户可能没按回车、也没让输入框失焦就关窗口。
        Unloaded += (_, _) => ViewModel?.SaveAll();

        // 色块取色器的图标在展开的色板里也可能还没失焦，离开页面时把防抖里的颜色一起落盘。
        Unloaded += (_, _) => ViewModel?.FlushPaletteCommit();
    }

    private BoardSettingsPageViewModel? ViewModel => DataContext as BoardSettingsPageViewModel;

    private void OnBoardNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        ViewModel?.SaveAll();
    }

    /// <summary>类型名、字段名、科目这些输入框失焦就落一次盘。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SaveAll();
    }

    private void OnAddFieldClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BoardTypeDef type })
            ViewModel?.AddField(type);
    }

    /// <summary>色板行的按钮：色块对象在行的 DataContext 上。</summary>
    private void OnMovePaletteColorUpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BoardPaletteColorItem item })
            ViewModel?.MovePaletteColorUp(item);
    }

    private void OnMovePaletteColorDownClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BoardPaletteColorItem item })
            ViewModel?.MovePaletteColorDown(item);
    }

    private void OnDeletePaletteColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BoardPaletteColorItem item })
            ViewModel?.RemovePaletteColor(item);
    }

    private void OnDeleteFieldClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BoardTypeField field })
            ViewModel?.RemoveField(field);
    }

    /// <summary>
    ///     删类型要先确认：类型里的字段是用户一条条敲出来的，
    ///     而且删掉之后用它建的作业就只剩个名字了。
    /// </summary>
    private async void OnDeleteTypeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: BoardTypeDef type } || ViewModel is not { } viewModel)
            return;

        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            var dialog = new FAContentDialog
            {
                Title = CR.Settings_Board_TypeDelete,
                Content = CR.Settings_Board_TypeDeleteMessage,
                PrimaryButtonText = CR.Settings_Board_TypeDelete,
                CloseButtonText = CR.Common_Cancel
            };

            if (await dialog.ShowAsync(topLevel) != FAContentDialogResult.Primary)
                return;
        }

        viewModel.RemoveType(type);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
