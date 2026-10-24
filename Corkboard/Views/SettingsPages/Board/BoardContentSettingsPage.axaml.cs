using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Board;
using Corkboard.ViewModels.SettingsPages.RichText;

namespace Corkboard.Views.SettingsPages.Board;

/// <summary>
///     「内容与样式」设置页：内容的默认样式（字号 / 颜色）、5 个字号、工具浮窗色板，以及手写设置。
///     <para>
///         手写那部分是 <see cref="HandwritingSettingsPageViewModel" /> 原样搬过来的（笔宽、橡皮、笔色、色板），
///         所以这一页的草稿提交要把两个 VM 都收一遍。
///     </para>
/// </summary>
[PageInfo("settings.board.content", FluentIcons.TextFontFilled, "settings.board.group")]
public partial class BoardContentSettingsPage : UserControl
{
    public BoardContentSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<BoardContentSettingsPageViewModel>();

        // 离开页面时把草稿提交掉：数字框可能还停在防抖里，用户也可能没让输入框失焦就关窗口。
        Unloaded += (_, _) =>
        {
            ViewModel?.SaveAll();
            ViewModel?.Handwriting.SaveAll();
        };
    }

    private BoardContentSettingsPageViewModel? ViewModel => DataContext as BoardContentSettingsPageViewModel;

    /// <summary>数字框失焦就落一次盘（字号、笔宽、橡皮宽都是草稿式的）。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SaveAll();
        ViewModel?.Handwriting.SaveAll();
    }

    #region 工具浮窗色板

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

    #endregion

    #region 手写色板

    /// <summary>手写色板行的按钮：转发给子 VM（手写那一套逻辑不在这页重写）。</summary>
    private void OnMoveHandwritingColorUpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RichTextPaletteColorItem item })
            ViewModel?.Handwriting.MovePaletteColorUp(item);
    }

    private void OnMoveHandwritingColorDownClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RichTextPaletteColorItem item })
            ViewModel?.Handwriting.MovePaletteColorDown(item);
    }

    private void OnDeleteHandwritingColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RichTextPaletteColorItem item })
            ViewModel?.Handwriting.RemovePaletteColor(item);
    }

    #endregion

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
