using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Corkboard.Core.Handwriting;

namespace Corkboard.Core.Controls;

/// <summary>
///     可编辑的手写画布：按下 / 移动 / 抬起画线，橡皮按半径真的擦掉已有笔迹，支持撤销与清空。
///     <para>
///         笔迹存在归一化坐标里（见 <see cref="HandwritingDocument" />），<see cref="Data" /> 就是它的
///         JSON 文本，布置作业表单把它直接塞进 <c>Note.Handwriting</c>；<see cref="Aspect" /> 是最近一次
///         快照的高宽比，画布尺寸一变就按新宽高重算，点本身不用改（本来就是相对的）。
///     </para>
///     <para>
///         所有笔迹都画在这一层的 <see cref="Render" /> 里：不为每个点建控件、不为每一笔建控件，
///         笔画再多也只是每笔一条几何路径。
///     </para>
///     <para>
///         撤销以「一笔 / 一次擦除操作」为一步：按下时拍一张笔画列表的快照，抬起时若真的改了东西就留着，
///         没改就丢掉（比如在空白处按了一下橡皮）。
///     </para>
/// </summary>
public class HandwritingCanvas : Control, ICustomHitTest
{
    /// <summary>撤销最多记多少步：再多也没什么用，还占内存。</summary>
    private const int MaxUndoSteps = 100;

    /// <summary>两个采样点之间至少隔多少 DIP：手抖的重复点不落盘，线也不会一卡一卡。</summary>
    private const double MinPointSpacing = 2;

    /// <summary>当前笔色。</summary>
    public static readonly StyledProperty<Color> StrokeColorProperty =
        AvaloniaProperty.Register<HandwritingCanvas, Color>(nameof(StrokeColor), HandwritingStyle.DefaultColors[0]);

    /// <summary>当前笔粗（DIP）。赋值会被 <see cref="HandwritingStyle.ClampPenWidth" /> 夹。</summary>
    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<HandwritingCanvas, double>(nameof(StrokeWidth), HandwritingStyle.DefaultPenWidth);

    /// <summary>当前橡皮粗细（DIP）。赋值会被 <see cref="HandwritingStyle.ClampEraserWidth" /> 夹。</summary>
    public static readonly StyledProperty<double> EraserWidthProperty =
        AvaloniaProperty.Register<HandwritingCanvas, double>(nameof(EraserWidth), HandwritingStyle.DefaultEraserWidth);

    /// <summary>现在是在用橡皮还是用笔。</summary>
    public static readonly StyledProperty<bool> IsEraserProperty =
        AvaloniaProperty.Register<HandwritingCanvas, bool>(nameof(IsEraser));

    /// <summary>笔迹数据（JSON 文本）。get 返回当前笔迹，set 载入一份已有笔迹。</summary>
    public static readonly StyledProperty<string?> DataProperty =
        AvaloniaProperty.Register<HandwritingCanvas, string?>(nameof(Data));

    /// <summary>最近一次快照的高宽比（高 / 宽），默认按常见的横版便签比例。</summary>
    public static readonly StyledProperty<double> AspectProperty =
        AvaloniaProperty.Register<HandwritingCanvas, double>(nameof(Aspect), HandwritingDocument.DefaultAspect);

    /// <summary>还能不能撤销（「撤销」按钮绑这个）。</summary>
    public static readonly DirectProperty<HandwritingCanvas, bool> CanUndoProperty =
        AvaloniaProperty.RegisterDirect<HandwritingCanvas, bool>(nameof(CanUndo), canvas => canvas.CanUndo);

    /// <summary>画布上有没有能画出来的墨（表单靠这个判断「算不算留了笔迹」）。</summary>
    public static readonly DirectProperty<HandwritingCanvas, bool> HasInkProperty =
        AvaloniaProperty.RegisterDirect<HandwritingCanvas, bool>(nameof(HasInk), canvas => canvas.HasInk);

    /// <summary>画布上能画出来的笔画数（橡皮痕迹之类不可见的笔不算）。</summary>
    public static readonly DirectProperty<HandwritingCanvas, int> StrokeCountProperty =
        AvaloniaProperty.RegisterDirect<HandwritingCanvas, int>(nameof(StrokeCount), canvas => canvas.StrokeCount);

    private bool _canUndo;
    private bool _hasInk;
    private int _strokeCount;
    private bool _pointerDown;

    /// <summary>正在画这一笔的指针（鼠标 / 触摸 / 手写笔）：只认它自己的移动与抬起。</summary>
    private IPointer? _activePointer;
    private bool _gestureChanged;
    private bool _syncingData;
    private bool _syncingAspect;
    private bool _coercingWidth;

    private HandwritingDocument _document = new();
    private HandwritingStroke? _activeStroke;
    private Point _lastPointerPosition;

    /// <summary>
    ///     这页手写是按多宽（DIP）归一化的参考宽度。只在「第一次落笔」或「载入已有笔迹」时定，
    ///     之后画布被拉大拉小都不改——否则用户拖一下内容框，已经写好的字就会被拉伸 / 挪位置。
    /// </summary>
    private double _referenceWidth;

    /// <summary>参考高度（DIP）= 参考宽度 × 文档 aspect，y 坐标相对它归一化。</summary>
    private double _referenceHeight;

    /// <summary>上一次指针事件的时间戳（毫秒），用来算书写速度。</summary>
    private ulong _lastPointerTimestamp;

    /// <summary>当前笔锋倍率（平滑过的书写速度结果）。</summary>
    private double _widthScale = HandwritingPoint.DefaultWidthScale;

    /// <summary>视口左上角在「参考坐标」里的位置（DIP），用来平移画布写更多内容。</summary>
    private Point _viewOffset;

    /// <summary>视口缩放（1 = 跟手写坐标一样大）。</summary>
    private double _viewZoom = 1;

    /// <summary>可缩放范围：太小看不清、太大一屏放不下几个字。</summary>
    private const double MinViewZoom = 0.5;

    private const double MaxViewZoom = 4;

    /// <summary>当前按在画布上的触摸点（多指手势用；键盘/鼠标不会进这张表）。</summary>
    private readonly Dictionary<IPointer, Point> _touchPoints = [];

    /// <summary>双指手势进行中：此时不画线，只平移 / 缩放视口。</summary>
    private bool _touchGestureActive;

    /// <summary>
    ///     双指手势结束、还有手指没抬起来时置真：剩下那根手指不要再落笔
    ///     （否则缩放手势松手时会多出一小段杂线）。
    /// </summary>
    private bool _suppressTouchDraw;

    /// <summary>双指手势起点：锚点在文档坐标里的位置、起始缩放与两指间距。</summary>
    private Point _touchAnchorDocument;

    private double _touchStartZoom = 1;

    private double _touchStartDistance;

    /// <summary>正在用右键 / 中键拖动画布的指针。</summary>
    private IPointer? _panPointer;

    private Point _panStartPosition;

    private Point _panStartOffset;

    /// <summary>撤销快照：每一步是当时笔画列表的浅拷贝（旧笔对象不会被原地改，所以浅拷贝就够）。</summary>
    private readonly List<List<HandwritingStroke>> _undo = [];

    static HandwritingCanvas()
    {
        AffectsRender<HandwritingCanvas>(AspectProperty);

        DataProperty.Changed.AddClassHandler<HandwritingCanvas>((canvas, _) => canvas.OnDataPropertyChanged());
        AspectProperty.Changed.AddClassHandler<HandwritingCanvas>((canvas, args) => canvas.OnAspectPropertyChanged(args.GetNewValue<double>()));
        StrokeWidthProperty.Changed.AddClassHandler<HandwritingCanvas>((canvas, _) => canvas.CoerceStrokeWidth());
        EraserWidthProperty.Changed.AddClassHandler<HandwritingCanvas>((canvas, _) => canvas.CoerceEraserWidth());
    }

    public HandwritingCanvas()
    {
        // 画布不能画到外面去（点在边缘时圆头笔会超出半个笔宽）。
        ClipToBounds = true;
    }

    /// <summary>当前笔色。</summary>
    public Color StrokeColor
    {
        get => GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    /// <summary>当前笔粗（DIP）。</summary>
    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    /// <summary>当前橡皮粗细（DIP）。</summary>
    public double EraserWidth
    {
        get => GetValue(EraserWidthProperty);
        set => SetValue(EraserWidthProperty, value);
    }

    /// <summary>现在是在用橡皮还是用笔。</summary>
    public bool IsEraser
    {
        get => GetValue(IsEraserProperty);
        set => SetValue(IsEraserProperty, value);
    }

    /// <summary>笔迹数据（JSON 文本）。set 载入一份已有笔迹；空 / 坏数据按空画布处理。</summary>
    public string? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>最近一次快照的高宽比（高 / 宽）。</summary>
    public double Aspect
    {
        get => GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    /// <summary>还能不能撤销。</summary>
    public bool CanUndo
    {
        get => _canUndo;
        private set => SetAndRaise(CanUndoProperty, ref _canUndo, value);
    }

    /// <summary>画布上有没有能画出来的墨。</summary>
    public bool HasInk
    {
        get => _hasInk;
        private set => SetAndRaise(HasInkProperty, ref _hasInk, value);
    }

    /// <summary>画布上能画出来的笔画数。</summary>
    public int StrokeCount
    {
        get => _strokeCount;
        private set => SetAndRaise(StrokeCountProperty, ref _strokeCount, value);
    }

    /// <summary>笔迹变了（画完一笔、擦完一次、撤销、清除）之后触发。此时 <see cref="Data" /> 已是新的。</summary>
    public event EventHandler? Changed;

    /// <summary>撤销上一步（一笔 / 一次擦除）。画到一半时忽略。</summary>
    public void Undo()
    {
        if (_pointerDown || _undo.Count == 0)
            return;

        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _document.Strokes = snapshot;

        SyncDataFromDocument();
        RefreshHasInk();
        UpdateCanUndo();
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>清空整块画布（撤销历史一起清掉）。</summary>
    public void Clear()
    {
        if (_pointerDown || _document.Strokes.Count == 0)
            return;

        _undo.Clear();
        _document.Strokes = [];

        SyncDataFromDocument();
        RefreshHasInk();
        UpdateCanUndo();
        InvalidateVisual();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     整块画布都算命中：这是个没有背景的裸 <see cref="Control" />，交给默认命中测试时
    ///     空白处的按下会被父容器接走（表现就是「鼠标点上去画不出线」）。
    /// </summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    /// <summary>画布只要把笔迹画出来就行，不用画背景 / 网格。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return;

        EnsureReferenceSize();

        // 按「参考尺寸」画、左上角对齐：拖大拖小内容框时笔迹留在原处（用户明确要求），
        // 多出来的地方只是继续可写的空白，不会把已有的字拉伸。
        // 外面再套一层视口变换：右键/中键拖动平移、Ctrl+滚轮缩放，可以写到大画布的更多角落。
        var transform = Matrix.CreateScale(_viewZoom, _viewZoom)
                        * Matrix.CreateTranslation(-_viewOffset.X * _viewZoom, -_viewOffset.Y * _viewZoom);

        using (context.PushTransform(transform))
            HandwritingRenderer.Render(context, _document, new Rect(0, 0, _referenceWidth, _referenceHeight));
    }

    /// <summary>
    ///     尺寸变化：空画布跟着改参考尺寸（整块区域都能写）；已经有笔迹就<b>什么都不动</b>——
    ///     参考尺寸不变，笔迹的绝对位置与粗细就都不变（旧实现会改 aspect 并重写数据，表现是笔迹被拉走）。
    /// </summary>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        var width = e.NewSize.Width;
        var height = e.NewSize.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return;

        if (_document.HasInk)
        {
            InvalidateVisual();
            return;
        }

        _referenceWidth = HandwritingDocument.ClampReferenceWidth(width);
        _referenceHeight = _referenceWidth * HandwritingDocument.ClampAspect(height / width);
        _document.ReferenceWidth = _referenceWidth;
        _document.HasReferenceWidth = true;

        var aspect = HandwritingDocument.ClampAspect(height / width);
        _document.Aspect = aspect;
        SetAspectProperty(aspect);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // 右键 / 中键拖动画布（平移到别处写）；左键、触摸、手写笔画线。
        if (e.Pointer.Type == PointerType.Mouse)
        {
            var properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsRightButtonPressed || properties.IsMiddleButtonPressed)
            {
                if (!double.IsFinite(Bounds.Width) || Bounds.Width <= 0 || !double.IsFinite(Bounds.Height) || Bounds.Height <= 0)
                    return;

                _panPointer = e.Pointer;
                _panStartPosition = e.GetPosition(this);
                _panStartOffset = _viewOffset;

                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        // 触控：单指落笔（和以前一样），第二根手指落下时改成「平移 / 缩放视口」。
        if (e.Pointer.Type == PointerType.Touch)
        {
            if (!double.IsFinite(Bounds.Width) || Bounds.Width <= 0 || !double.IsFinite(Bounds.Height) || Bounds.Height <= 0)
                return;

            e.Pointer.Capture(this);
            _touchPoints[e.Pointer] = e.GetPosition(this);

            if (_touchPoints.Count >= 2)
            {
                // 已经落笔的那一段丢掉：用户要的是挪画布，不是画一截。
                CancelActiveTouchStroke();
                BeginTouchGesture();
            }

            e.Handled = true;

            if (_touchGestureActive || _suppressTouchDraw || _touchPoints.Count >= 2)
                return;
        }

        // 鼠标要按住左键；触摸与手写笔没有「左键」这个说法，按下就算。
        var isDrawPointer = e.Pointer.Type is PointerType.Touch or PointerType.Pen
                            || e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        if (!isDrawPointer)
            return;

        if (!double.IsFinite(Bounds.Width) || Bounds.Width <= 0 || !double.IsFinite(Bounds.Height) || Bounds.Height <= 0)
            return;

        EnsureReferenceSize();

        e.Pointer.Capture(this);
        _activePointer = e.Pointer;
        _pointerDown = true;
        _gestureChanged = false;
        PushUndoSnapshot();

        var position = e.GetPosition(this);
        _lastPointerPosition = position;
        _lastPointerTimestamp = e.Timestamp;
        _widthScale = HandwritingPoint.DefaultWidthScale;

        if (IsEraser)
            ApplyEraseAt(position);
        else
            BeginStroke(position);

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_panPointer is not null && ReferenceEquals(e.Pointer, _panPointer))
        {
            var panPosition = e.GetPosition(this);
            var dx = (panPosition.X - _panStartPosition.X) / _viewZoom;
            var dy = (panPosition.Y - _panStartPosition.Y) / _viewZoom;

            _viewOffset = new Point(_panStartOffset.X - dx, _panStartOffset.Y - dy);
            ClampViewOffset();
            InvalidateVisual();

            e.Handled = true;
            return;
        }

        if (e.Pointer.Type == PointerType.Touch && _touchPoints.ContainsKey(e.Pointer))
        {
            _touchPoints[e.Pointer] = e.GetPosition(this);

            // 双指手势：只挪 / 缩视口，不落笔。
            if (_touchGestureActive || _touchPoints.Count >= 2)
            {
                if (_touchGestureActive)
                    UpdateTouchGesture();

                e.Handled = true;
                return;
            }

            // ⚠️ 单指必须继续往下走落笔分支。这里以前直接 return，手指拖动只落得下按下的那一个点
            //    （用户反馈「手指按到屏幕上之后拖动，其实只有一个点」）。
        }

        if (!_pointerDown || !ReferenceEquals(e.Pointer, _activePointer))
            return;

        var position = e.GetPosition(this);
        if (IsEraser)
        {
            EraseAlong(_lastPointerPosition, position);
        }
        else
        {
            // 笔锋：按「这段路走了多快」定粗细，慢写粗、快写细，再用指数平滑去掉抖动。
            _widthScale = ResolveSmoothedWidthScale(_lastPointerPosition, position, e.Timestamp);
            ExtendStroke(position, _widthScale);
        }

        _lastPointerPosition = position;
        _lastPointerTimestamp = e.Timestamp;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_panPointer is not null && ReferenceEquals(e.Pointer, _panPointer))
        {
            EndPan();
            e.Handled = true;
            return;
        }

        if (e.Pointer.Type == PointerType.Touch)
        {
            _touchPoints.Remove(e.Pointer);
            e.Pointer.Capture(null);

            if (_touchPoints.Count < 2)
                _touchGestureActive = false;

            if (_touchPoints.Count == 0)
                _suppressTouchDraw = false;

            // ⚠️ 单指画完抬起来必须收尾：不收尾 _pointerDown 会一直挂着，
            //    「撤销一笔」「清除」都因为「画到一半」被忽略（用户反馈点了没反应），
            //    这一笔也永远走不到 SyncDataFromDocument，表单保存时笔迹是空的。
            if (_pointerDown && ReferenceEquals(e.Pointer, _activePointer))
                EndGesture();

            e.Handled = true;
            return;
        }

        if (!_pointerDown || !ReferenceEquals(e.Pointer, _activePointer))
            return;

        e.Pointer.Capture(null);
        EndGesture();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (e.Pointer.Type == PointerType.Touch)
        {
            _touchPoints.Remove(e.Pointer);

            if (_touchPoints.Count < 2)
                _touchGestureActive = false;

            if (_touchPoints.Count == 0)
                _suppressTouchDraw = false;

            // 和抬起一样：捕获在画布外丢掉时也要把这一笔收掉，否则 _pointerDown 会一直挂住。
            if (_pointerDown && ReferenceEquals(e.Pointer, _activePointer))
                EndGesture();

            return;
        }

        // 指针在画布外抬起 / 被别的控件抢走捕获时也要收尾，否则这一笔会一直挂着。
        if (_pointerDown && ReferenceEquals(e.Pointer, _activePointer))
            EndGesture();
    }

    #region 画线

    /// <summary>按下开一笔。</summary>
    private void BeginStroke(Point position)
    {
        var stroke = new HandwritingStroke
        {
            Color = StrokeColor,
            Width = HandwritingStyle.ClampPenWidth(StrokeWidth),
            Points = [ToNormalized(position)]
        };

        _document.Strokes.Add(stroke);
        _activeStroke = stroke;
        _gestureChanged = true;

        RefreshHasInk();
        InvalidateVisual();
    }

    /// <summary>移动时往当前这一笔上加点；跨度大就按固定间距插值，线不会变成一段段折线。</summary>
    private void ExtendStroke(Point position, double widthScale)
    {
        if (_activeStroke is null)
            return;

        var distance = Distance(_lastPointerPosition, position);
        if (distance < MinPointSpacing)
        {
            // 挪得太近：只落一个点（完全没动就不落，免得存一堆重复坐标）。
            if (distance > 0.01)
                _activeStroke.Points.Add(ToNormalized(position, widthScale));

            InvalidateVisual();
            return;
        }

        // 插值出来的点，笔锋倍率也一起从上一个点线性过渡到这一下算出来的倍率。
        var previousScale = _activeStroke.Points.Count > 0
            ? _activeStroke.Points[^1].WidthScale
            : HandwritingPoint.DefaultWidthScale;

        var steps = (int)Math.Ceiling(distance / MinPointSpacing);
        for (var step = 1; step <= steps; step++)
        {
            var progress = (double)step / steps;
            var sample = new Point(
                _lastPointerPosition.X + (position.X - _lastPointerPosition.X) * progress,
                _lastPointerPosition.Y + (position.Y - _lastPointerPosition.Y) * progress);

            var scale = previousScale + (widthScale - previousScale) * progress;
            _activeStroke.Points.Add(ToNormalized(sample, scale));
        }

        InvalidateVisual();
    }

    #endregion

    #region 橡皮

    /// <summary>从上一个位置擦到当前位置：中间按 1/4 橡皮直径采样，快速拖动也不会漏掉一段。</summary>
    private void EraseAlong(Point from, Point to)
    {
        var spacing = Math.Max(1, HandwritingStyle.ClampEraserWidth(EraserWidth) / 4);
        var distance = Distance(from, to);

        if (distance <= 0.01)
        {
            ApplyEraseAt(to);
            return;
        }

        var steps = Math.Max(1, (int)Math.Ceiling(distance / spacing));
        for (var step = 1; step <= steps; step++)
        {
            var progress = (double)step / steps;
            ApplyEraseAt(new Point(
                from.X + (to.X - from.X) * progress,
                from.Y + (to.Y - from.Y) * progress));
        }
    }

    /// <summary>在某个点上擦一下：命中就替换笔画列表（橡皮不存成看不见的笔画）。</summary>
    private void ApplyEraseAt(Point position)
    {
        EnsureReferenceSize();

        var width = _referenceWidth;
        var height = _referenceHeight;
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0)
            return;

        var eraserWidth = HandwritingStyle.ClampEraserWidth(EraserWidth);
        var updated = HandwritingEraser.Erase(
            _document.Strokes,
            position.X / width,
            position.Y / height,
            eraserWidth / 2,
            width,
            height,
            out var changed);

        if (!changed)
            return;

        _document.Strokes = updated;
        _gestureChanged = true;

        RefreshHasInk();
        InvalidateVisual();
    }

    #endregion

    #region 手势收尾与撤销

    /// <summary>抬起 / 失去捕获时收尾：真的改了东西才保留撤销快照并通知外部。</summary>
    private void EndGesture()
    {
        if (!_pointerDown)
            return;

        _pointerDown = false;
        _activePointer = null;
        _activeStroke = null;

        if (_gestureChanged)
        {
            // 落笔可能超出参考区域（用户把内容框拖大了又往右边写）：把参考宽度撑大、
            // 所有归一化坐标按同样比例收回来，笔迹的**绝对位置不变**，但预览不会裁掉外面的部分。
            NormalizeExtents();
            SyncDataFromDocument();
            RefreshHasInk();
            UpdateCanUndo();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // 按下就抬（比如在空白处擦了一下）：这张快照没用，撤掉。
            DiscardLastUndoSnapshot();
            UpdateCanUndo();
        }

        _gestureChanged = false;
    }

    private void PushUndoSnapshot()
    {
        _undo.Add(new List<HandwritingStroke>(_document.Strokes));

        if (_undo.Count > MaxUndoSteps)
            _undo.RemoveAt(0);

        UpdateCanUndo();
    }

    private void DiscardLastUndoSnapshot()
    {
        if (_undo.Count > 0)
            _undo.RemoveAt(_undo.Count - 1);
    }

    private void UpdateCanUndo()
    {
        CanUndo = _undo.Count > 0;
    }

    #endregion

    #region 数据与属性同步

    /// <summary>外部给 <see cref="Data" /> 赋值：解析成文档；坏数据清成空画布（不抛异常）。</summary>
    private void OnDataPropertyChanged()
    {
        // 自己往 Data 里写（画完一笔同步 JSON）时不要再反过来重载文档，否则会把手势中的状态冲掉。
        if (_syncingData)
            return;

        var json = Data;
        if (!HandwritingCodec.TryDecode(json, out var document))
        {
            document = new HandwritingDocument();

            // 坏数据不留在控件里，免得表单下次保存又把这段解析不了的串写回磁盘。
            if (!string.IsNullOrEmpty(json))
                SetDataValue(null);
        }

        _document = document;
        _undo.Clear();

        // 载入的新文档要按它自己的参考宽度画：数据里没写（老格式）就用当前控件宽度补一个。
        _referenceWidth = 0;
        _referenceHeight = 0;

        SetAspectProperty(HandwritingDocument.ClampAspect(document.Aspect));

        RefreshHasInk();
        UpdateCanUndo();
        InvalidateVisual();
    }

    /// <summary>外部改 <see cref="Aspect" />：夹进合法区间并更新文档（不重写数据，数据只在手势结束时落）。</summary>
    private void OnAspectPropertyChanged(double aspect)
    {
        if (_syncingAspect)
            return;

        var clamped = HandwritingDocument.ClampAspect(aspect);
        _document.Aspect = clamped;

        if (Math.Abs(aspect - clamped) > 1e-9)
            SetAspectProperty(clamped);

        if (_document.HasInk)
        {
            // aspect 变了只影响预览怎么排；参考尺寸保持不变，笔迹的绝对位置不动。
            _referenceHeight = _referenceWidth * clamped;
            InvalidateVisual();
        }
    }

    /// <summary>把 aspect 写回属性（不触发「外部改 aspect」那条分支，避免自己转圈）。</summary>
    private void SetAspectProperty(double aspect)
    {
        if (Math.Abs(Aspect - aspect) <= 1e-9)
            return;

        _syncingAspect = true;
        try
        {
            SetCurrentValue(AspectProperty, aspect);
        }
        finally
        {
            _syncingAspect = false;
        }
    }

    /// <summary>把文档编码回 <see cref="Data" />；没有墨时给 null（表单据此判断有没有手写）。</summary>
    private void SyncDataFromDocument()
    {
        SetDataValue(_document.HasInk ? HandwritingCodec.Encode(_document) : null);
    }

    private void SetDataValue(string? json)
    {
        if (string.Equals(Data, json, StringComparison.Ordinal))
            return;

        _syncingData = true;
        try
        {
            SetCurrentValue(DataProperty, json);
        }
        finally
        {
            _syncingData = false;
        }
    }

    private void RefreshHasInk()
    {
        HasInk = _document.HasInk;
        StrokeCount = _document.VisibleStrokeCount;
    }

    /// <summary>笔宽越界时夹回合法区间（绑定传进来的 0 或 999 不该落进笔迹里）。</summary>
    private void CoerceStrokeWidth()
    {
        if (_coercingWidth)
            return;

        var clamped = HandwritingStyle.ClampPenWidth(StrokeWidth);
        if (Math.Abs(clamped - StrokeWidth) <= 1e-9)
            return;

        _coercingWidth = true;
        try
        {
            SetCurrentValue(StrokeWidthProperty, clamped);
        }
        finally
        {
            _coercingWidth = false;
        }
    }

    /// <summary>橡皮粗细越界时夹回合法区间。</summary>
    private void CoerceEraserWidth()
    {
        var clamped = HandwritingStyle.ClampEraserWidth(EraserWidth);
        if (Math.Abs(clamped - EraserWidth) <= 1e-9)
            return;

        SetCurrentValue(EraserWidthProperty, clamped);
    }

    /// <summary>Ctrl + 滚轮缩放画布（以指针位置为锚点），这样一屏装不下时能放大写细节。</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0)
            return;

        var position = e.GetPosition(this);
        var anchor = ToDocument(position);

        var zoom = Math.Clamp(_viewZoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), MinViewZoom, MaxViewZoom);
        if (Math.Abs(zoom - _viewZoom) <= 1e-9)
            return;

        _viewZoom = zoom;
        _viewOffset = new Point(anchor.X - position.X / _viewZoom, anchor.Y - position.Y / _viewZoom);
        ClampViewOffset();
        InvalidateVisual();

        e.Handled = true;
    }

    /// <summary>双指手势起点：记下锚点（文档坐标）、起始缩放与两指间距。</summary>
    private void BeginTouchGesture()
    {
        _touchGestureActive = true;
        _suppressTouchDraw = true;

        var centroid = TouchCentroid();
        _touchAnchorDocument = ToDocument(centroid);
        _touchStartZoom = _viewZoom;
        _touchStartDistance = TouchSpread();
    }

    /// <summary>
    ///     双指移动：两指中点的位移 = 平移，两指间距的变化 = 缩放（以起点锚点为准）。
    ///     平移和缩放用同一个锚点算式，拖动时「手指下的那一点」始终跟着手指走，手感不会漂。
    /// </summary>
    private void UpdateTouchGesture()
    {
        var centroid = TouchCentroid();
        var spread = TouchSpread();

        var zoom = _touchStartZoom;
        if (_touchStartDistance > 1 && spread > 1)
            zoom = Math.Clamp(_touchStartZoom * (spread / _touchStartDistance), MinViewZoom, MaxViewZoom);

        _viewZoom = zoom;
        _viewOffset = new Point(
            _touchAnchorDocument.X - centroid.X / zoom,
            _touchAnchorDocument.Y - centroid.Y / zoom);

        ClampViewOffset();
        InvalidateVisual();
    }

    /// <summary>第二根手指落下时把正在画的那一笔 / 正在擦的那一段整个撤掉。</summary>
    private void CancelActiveTouchStroke()
    {
        if (!_pointerDown)
            return;

        if (_undo.Count > 0)
        {
            _document.Strokes = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
        }

        _pointerDown = false;
        _activePointer = null;

        RefreshHasInk();
        UpdateCanUndo();
        InvalidateVisual();
    }

    private Point TouchCentroid()
    {
        var sumX = 0.0;
        var sumY = 0.0;
        foreach (var position in _touchPoints.Values)
        {
            sumX += position.X;
            sumY += position.Y;
        }

        var count = Math.Max(1, _touchPoints.Count);
        return new Point(sumX / count, sumY / count);
    }

    /// <summary>两指之间的最大距离（超过两指时取最远的一对，够用就行）。</summary>
    private double TouchSpread()
    {
        var max = 0.0;
        foreach (var first in _touchPoints.Values)
        {
            foreach (var second in _touchPoints.Values)
            {
                var dx = second.X - first.X;
                var dy = second.Y - first.Y;
                max = Math.Max(max, Math.Sqrt(dx * dx + dy * dy));
            }
        }

        return max;
    }

    /// <summary>屏幕坐标 → 参考坐标（视口的反变换）。</summary>
    private Point ToDocument(Point position)
    {
        return new Point(position.X / _viewZoom + _viewOffset.X, position.Y / _viewZoom + _viewOffset.Y);
    }

    private void EndPan()
    {
        _panPointer?.Capture(null);
        _panPointer = null;
    }

    /// <summary>别让视口跑到笔迹范围之外：左上角最多退到原点，右下角最多越过参考区域一屏。</summary>
    private void ClampViewOffset()
    {
        if (!double.IsFinite(Bounds.Width) || !double.IsFinite(Bounds.Height) || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var visibleWidth = Bounds.Width / _viewZoom;
        var visibleHeight = Bounds.Height / _viewZoom;
        var maxX = Math.Max(0, _referenceWidth - visibleWidth);
        var maxY = Math.Max(0, _referenceHeight - visibleHeight);

        _viewOffset = new Point(
            Math.Clamp(_viewOffset.X, -visibleWidth * 0.25, maxX + visibleWidth * 0.25),
            Math.Clamp(_viewOffset.Y, -visibleHeight * 0.25, maxY + visibleHeight * 0.25));
    }

    private HandwritingPoint ToNormalized(Point position)
    {
        return ToNormalized(position, HandwritingPoint.DefaultWidthScale);
    }

    private HandwritingPoint ToNormalized(Point position, double widthScale)
    {
        EnsureReferenceSize();

        var width = _referenceWidth;
        var height = _referenceHeight;
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0)
            return new HandwritingPoint(0, 0);

        var documentPosition = ToDocument(position);
        return HandwritingPoint.Create(documentPosition.X / width, documentPosition.Y / height, widthScale);
    }

    /// <summary>
    ///     保证参考尺寸已经定下来：空画布用当前控件尺寸，载入的文档用它自己记着的参考宽度
    ///     （老数据没有就按当前控件宽度补，并写回文档）。
    /// </summary>
    private void EnsureReferenceSize()
    {
        if (double.IsFinite(_referenceWidth) && _referenceWidth > 0
            && double.IsFinite(_referenceHeight) && _referenceHeight > 0)
        {
            return;
        }

        var boundsWidth = Bounds.Width;
        var boundsHeight = Bounds.Height;
        if (!double.IsFinite(boundsWidth) || boundsWidth <= 0 || !double.IsFinite(boundsHeight) || boundsHeight <= 0)
            return;

        if (_document.HasInk)
        {
            _referenceWidth = HandwritingDocument.ClampReferenceWidth(
                _document.HasReferenceWidth ? _document.ReferenceWidth : boundsWidth);
        }
        else
        {
            _referenceWidth = HandwritingDocument.ClampReferenceWidth(boundsWidth);
            _document.Aspect = HandwritingDocument.ClampAspect(boundsHeight / boundsWidth);
        }

        _referenceHeight = Math.Max(1, _referenceWidth * _document.Aspect);
        _document.ReferenceWidth = _referenceWidth;
        _document.HasReferenceWidth = true;
    }

    /// <summary>
    ///     把超出参考区域的笔迹收回来：参考宽度按「最远那一点」放大 s 倍，所有归一化坐标同时除以 s。
    ///     <c>坐标 × 参考宽度</c> 这个绝对位置保持不变，所以笔迹不会跳；重新归一化后预览也不会裁边。
    /// </summary>
    private void NormalizeExtents()
    {
        EnsureReferenceSize();

        if (!_document.HasInk || _referenceWidth <= 0)
            return;

        var maxX = 1.0;
        var maxY = 1.0;
        foreach (var stroke in _document.Strokes)
        {
            if (!stroke.IsVisible)
                continue;

            foreach (var point in stroke.Points)
            {
                if (point.X > maxX)
                    maxX = point.X;
                if (point.Y > maxY)
                    maxY = point.Y;
            }
        }

        var growth = Math.Max(maxX, maxY);
        if (!double.IsFinite(growth) || growth <= 1.0001)
            return;

        var targetWidth = HandwritingDocument.ClampReferenceWidth(_referenceWidth * growth);
        var factor = targetWidth / _referenceWidth;
        if (!double.IsFinite(factor) || factor <= 1)
            return;

        foreach (var stroke in _document.Strokes)
        {
            for (var index = 0; index < stroke.Points.Count; index++)
            {
                var point = stroke.Points[index];
                stroke.Points[index] = HandwritingPoint.Create(point.X / factor, point.Y / factor, point.WidthScale);
            }
        }

        _referenceWidth = targetWidth;
        _referenceHeight = targetWidth * _document.Aspect;
        _document.ReferenceWidth = _referenceWidth;
        _document.HasReferenceWidth = true;
    }

    /// <summary>
    ///     按书写速度算笔锋倍率，再做平滑与限幅（慢写略粗、快写略细）：
    ///     速度单位是 DIP/毫秒，正常人写字大概在 0.2–1.2 之间。
    /// </summary>
    private double ResolveSmoothedWidthScale(Point from, Point to, ulong timestamp)
    {
        var target = HandwritingPoint.DefaultWidthScale;

        var elapsed = timestamp >= _lastPointerTimestamp ? timestamp - _lastPointerTimestamp : 0;
        if (elapsed > 0)
        {
            var speed = Distance(from, to) / elapsed;

            // 速度 → 倍率：慢写 1.25 倍、快写 0.75 倍。两端都夹住——快甩时不该细成头发丝，
            // 慢写时也不该糊成一块（用户反馈过「速度太快会被拉得特别细」）。
            const double slowSpeed = 0.2;
            const double fastSpeed = 1.2;
            var progress = Math.Clamp((speed - slowSpeed) / (fastSpeed - slowSpeed), 0, 1);
            target = 1.25 - progress * 0.5;
        }

        // 平滑 + 自动矫正：一次极端速度不该把后面几笔全带粗/带细，每点最多变 0.12；
        // 结果再夹进 0.72–1.3，保证笔迹始终看得清。
        var smoothed = _widthScale * 0.65 + target * 0.35;
        _widthScale = Math.Clamp(smoothed, _widthScale - 0.12, _widthScale + 0.12);
        _widthScale = Math.Clamp(_widthScale, 0.72, 1.3);
        return _widthScale;
    }

    private static double Distance(Point from, Point to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    #endregion
}
