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
///     「窗口与收纳」设置页：主窗口不透明度 / 置底 / 圆角 / 长按拖动 / 点击穿透，以及收纳浮钮的
///     开关、位置（九宫格示意图）、左右 / 上下偏移与按钮大小。
///     <para>
///         窗口那几项是从「基础设置」整块搬过来的（基础设置里不再放作业板专属项），
///         收纳那几项是从原来的「收纳」页搬过来的，事件都转发给子 VM。
///     </para>
/// </summary>
[PageInfo("settings.board.window", FluentIcons.WindowFilled, "settings.board.group")]
public partial class BoardWindowSettingsPage : UserControl
{
    public BoardWindowSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<BoardWindowSettingsPageViewModel>();

        // 收纳的子 VM 是 transient、配置是单例：订阅必须在离开可视树时断开，
        // 否则每开一次这个页面就往配置上多挂一个处理器。
        Loaded += (_, _) =>
        {
            ViewModel?.Attach();
            ViewModel?.Dock.Attach();
        };
        Unloaded += (_, _) =>
        {
            // 拖完滑杆马上关页面时最后一次拖动不能丢。
            ViewModel?.FlushOpacityDraft();
            ViewModel?.Dock.CommitAll();
            ViewModel?.Dock.Detach();
            ViewModel?.Detach();
        };
    }

    private BoardWindowSettingsPageViewModel? ViewModel => DataContext as BoardWindowSettingsPageViewModel;

    /// <summary>偏移 / 大小输入框失焦就提交：离开输入框后浮钮立刻挪到新位置。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.Dock.CommitAll();

    private void ResetButton_OnClick(object? sender, RoutedEventArgs e) => ViewModel?.Dock.ResetToDefault();

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
