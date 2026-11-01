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
///     「看板模式」设置页：全屏里显示什么、怎么排、字多大，以及「到点自己进出」与链接入口。
///     <para>
///         这一页是**顶层项、不归组**（<see cref="PageInfoAttribute" /> 上不写 groupId）：
///         看板模式是一块独立的全屏入口，跟作业板、考试看板都不是子项关系。
///     </para>
///     <para>
///         字号滑块拖动时弹预览浮窗（<c>DisplaySliderItem.IsDragging</c>），松手才写配置。
///         指针事件挂在**页面**这一层：滑块内部的 Thumb / 轨道会吃掉一部分事件，
///         挂在滑块自己身上会出现「拖到一半浮窗不关、值也没提交」。
///     </para>
/// </summary>
[PageInfo("settings.display", FluentIcons.ClockFilled, "settings.display.group")]
public partial class DisplaySettingsPage : UserControl
{
    private DisplaySliderItem? _dragging;

    public DisplaySettingsPage()
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

        // ViewModel 是 transient、配置 handler 是单例：订阅必须在离开可视树时断开。
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) =>
        {
            EndDrag();
            ViewModel?.CommitAll();
            ViewModel?.Detach();
        };
    }

    private DisplaySettingsPageViewModel? ViewModel => DataContext as DisplaySettingsPageViewModel;

    /// <summary>字号 / 光标 / 时刻输入框失焦就提交。</summary>
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
        // 拖着的时候不提交（等松手），键盘 / 点击轨道这种「一下一下」的改动当场落盘。
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
