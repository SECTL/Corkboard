using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Board;

namespace Corkboard.Views.SettingsPages.Board;

[PageInfo("settings.board", FluentIcons.BoardFilled, "settings.board.group")]
public partial class BoardSettingsPage : UserControl
{
    public BoardSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<BoardSettingsPageViewModel>();

        // 离开页面时把草稿提交掉：名称与区块宽度是草稿式的，用户可能没按回车、也没让输入框失焦就关窗口。
        Unloaded += (_, _) => ViewModel?.SaveAll();
    }

    private BoardSettingsPageViewModel? ViewModel => DataContext as BoardSettingsPageViewModel;

    private void OnBoardNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        ViewModel?.SaveAll();
    }

    /// <summary>区块宽度这类输入框失焦就落一次盘。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SaveAll();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
