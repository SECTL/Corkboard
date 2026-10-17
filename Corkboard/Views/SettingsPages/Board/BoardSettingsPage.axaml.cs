using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Board;
using Corkboard.ViewModels.SettingsPages.Homework;
using Corkboard.ViewModels.SettingsPages.RichText;

namespace Corkboard.Views.SettingsPages.Board;

[PageInfo("settings.board", FluentIcons.BoardFilled, "settings.board.group")]
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

    /// <summary>科目这些输入框失焦就落一次盘。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SaveAll();
    }

    /// <summary>色板行的按钮：色块对象在行的 DataContext 上。</summary>
    private void OnMovePaletteColorUpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RichTextPaletteColorItem item })
            ViewModel?.MovePaletteColorUp(item);
    }

    private void OnMovePaletteColorDownClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RichTextPaletteColorItem item })
            ViewModel?.MovePaletteColorDown(item);
    }

    private void OnDeletePaletteColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RichTextPaletteColorItem item })
            ViewModel?.RemovePaletteColor(item);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
