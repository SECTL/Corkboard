using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Exam;

namespace Corkboard.Views.SettingsPages.Exam;

/// <summary>
///     「看板字号」设置页：全屏考试看板上每一处文字的字号，外加「恢复默认字号」。
///     <para>
///         页面本身很薄——每一项字号都是一条由 ViewModel 的 <c>FontSizeGroups</c> 驱动的行，
///         加一项新文字不用改这里。字号用滑块调（**没有精确输入框**），拖动期间浮窗里给出
///         「按当前字号渲染的预览 + 当前数值」。
///     </para>
///     <para>
///         ⚠️ 拖动状态由本文件置位：滑块的指针事件有一部分被 Thumb / 轨道自己吃掉，
///         所以处理器挂在**页面**这一层、三种路由全写、<c>handledEventsToo</c> 为真
///         （逐个滑块去挂要跟着模板复用走，容易漏挂或多挂）。
///     </para>
/// </summary>
[PageInfo("settings.exam.font", FluentIcons.TextFontFilled, "settings.exam.group")]
public partial class ExamFontSettingsPage : UserControl
{
    /// <summary>正在被按住拖的那一项（松手要把它收回去并落盘）。</summary>
    private ExamFontSizeItem? _dragging;

    public ExamFontSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamFontSettingsPageViewModel>();

        AddHandler(PointerPressedEvent, OnPagePointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPagePointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPagePointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        // 键盘调（方向键 / PageUp）不经过指针：只要不是在拖，值一变就落盘。
        AddHandler(RangeBase.ValueChangedEvent, OnSliderValueChanged,
            RoutingStrategies.Bubble, handledEventsToo: true);

        // ViewModel 是 transient、配置是单例：订阅必须在离开可视树时断开。
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) =>
        {
            EndDrag();
            ViewModel?.CommitAll();
            ViewModel?.Detach();
        };
    }

    private ExamFontSettingsPageViewModel? ViewModel => DataContext as ExamFontSettingsPageViewModel;

    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (FindItem(e.Source) is not { } item)
            return;

        // 换一根滑块：先把上一根收回去，免得浮窗留在上一次那一行。
        EndDrag();
        _dragging = item;
        item.IsDragging = true;
    }

    private void OnPagePointerReleased(object? sender, PointerReleasedEventArgs e) => EndDrag();

    private void OnPagePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    /// <summary>值变了：拖动期间只改草稿（松手才落盘），其余情况（键盘微调）当场提交。</summary>
    private void OnSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_dragging is null)
            ViewModel?.CommitAll();
    }

    /// <summary>收起浮窗并把还压在草稿里的值写进配置。</summary>
    private void EndDrag()
    {
        if (_dragging is null)
            return;

        _dragging.IsDragging = false;
        _dragging = null;
        ViewModel?.CommitAll();
    }

    /// <summary>从指针落在哪个元素上推出「这是哪一项的滑块」（落在 Thumb / 轨道上都算）。</summary>
    private static ExamFontSizeItem? FindItem(object? source)
    {
        if (source is not Visual visual)
            return null;

        return visual.FindAncestorOfType<Slider>(includeSelf: true)?.DataContext as ExamFontSizeItem;
    }
}
