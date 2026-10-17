using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Board;

namespace Corkboard.Views.SettingsPages.Board;

/// <summary>
///     「收纳」设置页：开关、按钮位置（九宫格示意图）、左右 / 上下偏移与按钮大小。
/// </summary>
[PageInfo("settings.board.dock", FluentIcons.ArrowMinimizeFilled, "settings.board.group")]
public partial class DockSettingsPage : UserControl
{
    public DockSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<DockSettingsPageViewModel>();

        // ViewModel 是 transient、配置是单例：订阅必须在离开可视树时断开，
        // 否则每开一次这个页面就往配置上多挂一个处理器。
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) =>
        {
            ViewModel?.CommitAll();
            ViewModel?.Detach();
        };
    }

    private DockSettingsPageViewModel? ViewModel => DataContext as DockSettingsPageViewModel;

    /// <summary>偏移 / 大小输入框失焦就提交：离开输入框后浮钮立刻挪到新位置。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitAll();

    private void ResetButton_OnClick(object? sender, RoutedEventArgs e) => ViewModel?.ResetToDefault();

    /// <summary>点下拉样按钮：展开 / 收起九宫格浮层。</summary>
    private void OnAnchorPickerClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Popup>("AnchorPopup") is { } popup)
            popup.IsOpen = !popup.IsOpen;
    }

    /// <summary>九宫格圆点：位置已经写进配置，点完顺手把浮层收起来。</summary>
    private void OnAnchorDotClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Popup>("AnchorPopup") is { } popup)
            popup.IsOpen = false;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
