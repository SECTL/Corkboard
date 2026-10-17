using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
public class HandwritingCanvas : Control
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
    private bool _gestureChanged;
    private bool _syncingData;
    private bool _syncingAspect;
    private bool _coercingWidth;

    private HandwritingDocument _document = new();
    private HandwritingStroke? _activeStroke;
    private Point _lastPointerPosition;

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

    /// <summary>画布只要把笔迹画出来就行，不用画背景 / 网格。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return;

        HandwritingRenderer.Render(context, _document, new Rect(0, 0, width, height));
    }

    /// <summary>尺寸一变就按新宽高更新 aspect（点本来就是归一化的，不用动）。</summary>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        var width = e.NewSize.Width;
        var height = e.NewSize.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return;

        var aspect = HandwritingDocument.ClampAspect(height / width);
        if (Math.Abs(aspect - _document.Aspect) <= 1e-9)
            return;

        // 画布形状变了，已存笔迹的 aspect 也跟着变：预览按新比例画回来才不变形。
        _document.Aspect = aspect;
        SetAspectProperty(aspect);
        if (_document.HasInk)
            SyncDataFromDocument();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (!double.IsFinite(Bounds.Width) || Bounds.Width <= 0 || !double.IsFinite(Bounds.Height) || Bounds.Height <= 0)
            return;

        e.Pointer.Capture(this);
        _pointerDown = true;
        _gestureChanged = false;
        PushUndoSnapshot();

        var position = e.GetPosition(this);
        _lastPointerPosition = position;

        if (IsEraser)
            ApplyEraseAt(position);
        else
            BeginStroke(position);

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_pointerDown)
            return;

        var position = e.GetPosition(this);
        if (IsEraser)
            EraseAlong(_lastPointerPosition, position);
        else
            ExtendStroke(position);

        _lastPointerPosition = position;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_pointerDown)
            return;

        e.Pointer.Capture(null);
        EndGesture();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        // 指针在画布外抬起 / 被别的控件抢走捕获时也要收尾，否则这一笔会一直挂着。
        if (_pointerDown)
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
    private void ExtendStroke(Point position)
    {
        if (_activeStroke is null)
            return;

        var distance = Distance(_lastPointerPosition, position);
        if (distance < MinPointSpacing)
        {
            // 挪得太近：只落一个点（完全没动就不落，免得存一堆重复坐标）。
            if (distance > 0.01)
                _activeStroke.Points.Add(ToNormalized(position));

            InvalidateVisual();
            return;
        }

        var steps = (int)Math.Ceiling(distance / MinPointSpacing);
        for (var step = 1; step <= steps; step++)
        {
            var progress = (double)step / steps;
            var sample = new Point(
                _lastPointerPosition.X + (position.X - _lastPointerPosition.X) * progress,
                _lastPointerPosition.Y + (position.Y - _lastPointerPosition.Y) * progress);

            _activeStroke.Points.Add(ToNormalized(sample));
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
        var width = Bounds.Width;
        var height = Bounds.Height;
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
        _activeStroke = null;

        if (_gestureChanged)
        {
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

        SetAspectProperty(HandwritingDocument.ClampAspect(document.Aspect));

        RefreshHasInk();
        UpdateCanUndo();
        InvalidateVisual();
    }

    /// <summary>外部改 <see cref="Aspect" />：夹进合法区间并更新文档（有笔迹时重写 JSON）。</summary>
    private void OnAspectPropertyChanged(double aspect)
    {
        if (_syncingAspect)
            return;

        var clamped = HandwritingDocument.ClampAspect(aspect);
        _document.Aspect = clamped;

        if (Math.Abs(aspect - clamped) > 1e-9)
            SetAspectProperty(clamped);

        if (_document.HasInk)
            SyncDataFromDocument();
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

    private HandwritingPoint ToNormalized(Point position)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0)
            return new HandwritingPoint(0, 0);

        return HandwritingPoint.Create(position.X / width, position.Y / height);
    }

    private static double Distance(Point from, Point to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    #endregion
}
