using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Corkboard.Views.Actions;

/// <summary>
///     规则编辑器卡片。只有顶栏是拖动面（整卡拖动会与输入框、下拉、滚动条抢指针）；
///     位移用 <c>RenderTransform</c>，坐标一律取顶层窗口坐标系——取相对坐标会自我抵消、拖不动。
/// </summary>
public partial class ActionEditorView : UserControl
{
    private bool _dragging;
    private Point _pressPosition;
    private Point _pressOffset;
    private readonly TranslateTransform _transform;

    public ActionEditorView()
    {
        InitializeComponent();

        _transform = (TranslateTransform)Card.RenderTransform!;

        // 拖动期间事件只从捕获元素往上走，所以移动/抬起挂在卡片自己身上。
        AddHandler(PointerMovedEvent, OnPointerMoved,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost,
            RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;

        _dragging = true;
        _pressPosition = e.GetPosition(topLevel);
        _pressOffset = new Point(_transform.X, _transform.Y);

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;

        var position = e.GetPosition(topLevel);
        _transform.X = _pressOffset.X + (position.X - _pressPosition.X);
        _transform.Y = _pressOffset.Y + (position.Y - _pressPosition.Y);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => EndDrag(e.Pointer);

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag(null);

    private void EndDrag(IPointer? pointer)
    {
        if (!_dragging) return;

        _dragging = false;

        // 指针在窗口外抬起时收不到 Released，捕获不显式放掉会让整窗点不动。
        pointer?.Capture(null);
    }
}
