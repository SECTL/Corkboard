using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Display;

namespace Corkboard.Views.SettingsPages.Display;

/// <summary>
///     「设置 → 看板模式 → 自动显示」页：几时自己铺出来、多久轮播一屏作业、以及现在就开关。
///     <para>
///         与「显示与版式」页共用同一个 <see cref="DisplaySettingsPageViewModel" />（transient，
///         各自一份实例）：时段那几段是集合元素，改动要显式落盘，逻辑留在 VM 里。
///     </para>
///     <para>
///         指针事件挂在**页面**这一层：滑块内部的 Thumb / 轨道会吃掉一部分事件，
///         挂在滑块自己身上会出现「拖到一半不提交」。
///     </para>
/// </summary>
[PageInfo("settings.display.schedule", FluentIcons.CalendarFilled, "settings.display.group")]
public partial class DisplayScheduleSettingsPage : UserControl
{
    private DisplaySliderItem? _dragging;

    public DisplayScheduleSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<DisplaySettingsPageViewModel>();

        AddHandler(PointerPressedEvent, OnPagePointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPagePointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPagePointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        // 键盘微调（方向键）没有指针抬起那一拍，靠 ValueChanged 当场提交。
        AddHandler(RangeBase.ValueChangedEvent, OnSliderValueChanged,
            RoutingStrategies.Bubble, handledEventsToo: true);

        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) =>
        {
            EndDrag();
            ViewModel?.CommitAll();
            ViewModel?.Detach();
        };
    }

    private DisplaySettingsPageViewModel? ViewModel => DataContext as DisplaySettingsPageViewModel;

    /// <summary>时刻输入框失焦就提交（时段改动只有这一条落盘路径）。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitAll();

    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (FindItem(e.Source) is not { } item)
            return;

        EndDrag();
        _dragging = item;
        item.IsDragging = true;
    }

    private void OnPagePointerReleased(object? sender, PointerReleasedEventArgs e) => EndDrag();

    private void OnPagePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private void OnSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_dragging is null)
            ViewModel?.CommitAll();
    }

    private void EndDrag()
    {
        if (_dragging is null)
            return;

        _dragging.IsDragging = false;
        _dragging = null;
        ViewModel?.CommitAll();
    }

    private static DisplaySliderItem? FindItem(object? source) =>
        source is Visual visual
            ? visual.FindAncestorOfType<Slider>(includeSelf: true)?.DataContext as DisplaySliderItem
            : null;
}
