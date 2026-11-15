using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam.Layout;
using Corkboard.Core.Models.SubConfigs.Exam;

namespace Corkboard.Core.Controls.ExamBoard;

/// <summary>
///     自定义布局面板：按百分比矩形摆放五个看板区块。
///     <para>
///         每个子控件挂 <see cref="ComponentProperty" /> 声明自己是谁，面板从 <see cref="LayoutProperty" />
///         取该区块的百分比矩形与样式。百分比以面板自身客户区为基准，因此窗口换分辨率时版式等比缩放。
///     </para>
///     <para>
///         布局编辑窗点击画布时，面板把「点中了哪个区块」抛给宿主（<see cref="ComponentPressed" />），
///         点空白处抛 <see langword="null" />。放映窗没有订阅者，普通点击不受影响。
///     </para>
/// </summary>
public class ExamBoardLayoutPanel : Panel
{
    public static readonly AttachedProperty<ExamBoardLayoutComponentId> ComponentProperty =
        AvaloniaProperty.RegisterAttached<ExamBoardLayoutPanel, Control, ExamBoardLayoutComponentId>(
            "Component");

    public static readonly StyledProperty<ExamBoardStatusLayout?> LayoutProperty =
        AvaloniaProperty.Register<ExamBoardLayoutPanel, ExamBoardStatusLayout?>(nameof(Layout));

    static ExamBoardLayoutPanel()
    {
        ComponentProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (control.Parent is Control parent)
            {
                parent.InvalidateMeasure();
            }
        });
        LayoutProperty.Changed.AddClassHandler<ExamBoardLayoutPanel>((panel, _) => panel.AttachLayout());
        AffectsParentMeasure<ExamBoardLayoutPanel>(ComponentProperty);
    }

    public ExamBoardLayoutPanel()
    {
        // 退出区块自己的 Border 会处理指针事件，这里仍要拿到（编辑窗需要能点中它）。
        AddHandler(PointerPressedEvent, OnPanelPointerPressed, RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    /// <summary>画布上某个区块被点击；点空白处参数为 <see langword="null" />。</summary>
    public event EventHandler<ExamBoardLayoutComponentId?>? ComponentPressed;

    public ExamBoardStatusLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public static ExamBoardLayoutComponentId GetComponent(Control control) =>
        control.GetValue(ComponentProperty);

    public static void SetComponent(Control control, ExamBoardLayoutComponentId value) =>
        control.SetValue(ComponentProperty, value);

    private void OnPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        for (var visual = e.Source as Visual; visual is not null && visual != this;
             visual = visual.GetVisualParent())
        {
            // ⚠️ 只认面板直接托管的那个包装 ContentControl：UserControl 自己也是 ContentControl，
            // 从被点中的区块往上第一个命中的往往是区块本身，它的 Component 附加属性没设过、
            // 读出来是默认的 Title——表现就是「点哪个区块，下拉框都跳回标题」。
            if (visual is ContentControl content && Children.Contains(content))
            {
                ComponentPressed?.Invoke(this, GetComponent(content));
                return;
            }
        }

        ComponentPressed?.Invoke(this, null);
    }

    private ExamBoardStatusLayout? _attached;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachLayout();
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachLayout()
    {
        DetachLayout();
        _attached = Layout;
        if (_attached is not null)
        {
            _attached.PropertyChanged += OnLayoutPropertyChanged;
            foreach (var component in Enum.GetValues<ExamBoardLayoutComponentId>())
            {
                _attached.For(component).PropertyChanged += OnComponentPropertyChanged;
            }
        }

        InvalidateMeasure();
    }

    private void DetachLayout()
    {
        if (_attached is null)
        {
            return;
        }

        _attached.PropertyChanged -= OnLayoutPropertyChanged;
        foreach (var component in Enum.GetValues<ExamBoardLayoutComponentId>())
        {
            _attached.For(component).PropertyChanged -= OnComponentPropertyChanged;
        }

        _attached = null;
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e) => AttachLayout();

    private void OnComponentPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = default(Size);
        foreach (var child in Children)
        {
            var component = GetComponent(child);
            var layout = Layout?.For(component);
            if (layout is null)
            {
                child.Measure(default);
                continue;
            }

            ApplyStyle(child, layout);
            var rect = ResolveRect(layout, availableSize);
            child.Measure(rect.Size);
            desired = new Size(Math.Max(desired.Width, child.DesiredSize.Width),
                Math.Max(desired.Height, child.DesiredSize.Height));
        }

        return desired;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var component = GetComponent(child);
            var layout = Layout?.For(component);
            if (layout is null)
            {
                child.Arrange(default);
                continue;
            }

            child.Arrange(ResolveRect(layout, finalSize));
        }

        return finalSize;
    }

    private static Rect ResolveRect(ExamBoardComponentLayout layout, Size size) =>
        new(size.Width * layout.X / 100d, size.Height * layout.Y / 100d,
            size.Width * layout.Width / 100d, size.Height * layout.Height / 100d);

    private static void ApplyStyle(Control child, ExamBoardComponentLayout layout)
    {
        child.Opacity = layout.Opacity;

        if (child is ContentControl content)
        {
            var (horizontal, vertical) = ToAlignment(layout.ContentAlignment);
            if (content.HorizontalContentAlignment != horizontal)
            {
                content.HorizontalContentAlignment = horizontal;
            }

            if (content.VerticalContentAlignment != vertical)
            {
                content.VerticalContentAlignment = vertical;
            }
        }

        var scale = layout.Scale;
        if (Math.Abs(scale - 1) < 0.001)
        {
            if (child.RenderTransform is ScaleTransform)
            {
                child.RenderTransform = null;
            }

            return;
        }

        if (child.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform(scale, scale);
            child.RenderTransform = transform;
        }
        else
        {
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }

        // 缩放锚点朝屏幕内侧：左上角的区块往右下长、右下角的往左上长。
        // 锚在中心的话，贴着边缘的时钟/标题放大时会有一半跑到屏幕外（用户反馈过）。
        child.RenderTransformOrigin = new RelativePoint(
            layout.X <= 50 ? 0 : 1,
            layout.Y <= 50 ? 0 : 1,
            RelativeUnit.Relative);
    }

    private static (HorizontalAlignment Horizontal, VerticalAlignment Vertical) ToAlignment(
        ExamBoardContentAlignment alignment) => alignment switch
    {
        ExamBoardContentAlignment.TopLeft => (HorizontalAlignment.Left, VerticalAlignment.Top),
        ExamBoardContentAlignment.TopCenter => (HorizontalAlignment.Center, VerticalAlignment.Top),
        ExamBoardContentAlignment.TopRight => (HorizontalAlignment.Right, VerticalAlignment.Top),
        ExamBoardContentAlignment.MiddleLeft => (HorizontalAlignment.Left, VerticalAlignment.Center),
        ExamBoardContentAlignment.MiddleRight => (HorizontalAlignment.Right, VerticalAlignment.Center),
        ExamBoardContentAlignment.BottomLeft => (HorizontalAlignment.Left, VerticalAlignment.Bottom),
        ExamBoardContentAlignment.BottomCenter => (HorizontalAlignment.Center, VerticalAlignment.Bottom),
        ExamBoardContentAlignment.BottomRight => (HorizontalAlignment.Right, VerticalAlignment.Bottom),
        _ => (HorizontalAlignment.Center, VerticalAlignment.Center)
    };
}