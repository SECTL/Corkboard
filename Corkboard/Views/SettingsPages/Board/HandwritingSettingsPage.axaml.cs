using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Board;
using Corkboard.ViewModels.SettingsPages.RichText;

namespace Corkboard.Views.SettingsPages.Board;

/// <summary>「手写」设置页：默认笔迹颜色/粗细、橡皮粗细与常用色板。</summary>
[PageInfo("settings.board.handwriting", FluentIcons.EditFilled, "settings.board.group")]
public partial class HandwritingSettingsPage : UserControl
{
    public HandwritingSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<HandwritingSettingsPageViewModel>();

        // 离开页面时把草稿提交掉：数字框可能还停在防抖里，用户也可能没让输入框失焦就关窗口。
        Unloaded += (_, _) => ViewModel?.SaveAll();
    }

    private HandwritingSettingsPageViewModel? ViewModel => DataContext as HandwritingSettingsPageViewModel;

    /// <summary>数字框失焦就落一次盘（笔宽 / 橡皮宽是草稿式的）。</summary>
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
