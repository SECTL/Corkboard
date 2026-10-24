using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Corkboard.ViewModels.Export;

namespace Corkboard.Views.Export;

/// <summary>
///     「导出作业板」对话框（自绘弹层内容）：选图片还是表格（范围固定全部作业）。
///     <para>
///         只是弹层里的内容，不自己开窗口：由作业板页面交给 <c>PageOverlayService</c>
///         挂到壳的遮罩上（遮罩要盖住自绘标题栏，必须挂在壳那一层）。
///     </para>
///     <para>
///         用户点「下一步 / 复制图片 / 取消」后，对话框的 <see cref="Completion" /> 完成；
///         关闭遮罩由 ViewModel 负责（它拿得到 <c>PageOverlayService</c>）。
///     </para>
///     <para>
///         卡片底部按钮固定、内容区自己滚（勾选列表很长时不会把卡片顶出窗口），标题栏可以拖动整张卡片。
///     </para>
/// </summary>
public partial class ExportDialog : UserControl
{
    private Grid? _titleBar;
    private Border? _sheet;
    private Border? _scopeList;
    private ScrollViewer? _scroll;
    private TranslateTransform? _sheetTransform;

    private bool _isDragging;
    private Point _dragStartPointer;
    private Point _dragStartOffset;
    private Point _sheetOffset;

    /// <summary>弹层内内容由页面构造，ViewModel 用页面当前的分组顺序建勾选列表。</summary>
    public ExportDialog(ExportDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        _sheet = this.FindControl<Border>("Sheet");
        _scroll = this.FindControl<ScrollViewer>("DialogScroll");
        _scopeList = this.FindControl<Border>("ScopeList");
        _titleBar = this.FindControl<Grid>("DialogTitleBar");
        if (_titleBar is not null)
        {
            _titleBar.PointerPressed += OnTitleBarPointerPressed;
            _titleBar.PointerMoved += OnTitleBarPointerMoved;
            _titleBar.PointerReleased += OnTitleBarPointerReleased;
            _titleBar.PointerCaptureLost += OnTitleBarPointerCaptureLost;
        }
    }

    /// <summary>对话框的 ViewModel。</summary>
    public ExportDialogViewModel ViewModel { get; }

    /// <summary>等用户做选择；<c>null</c> 表示取消。</summary>
    public Task<ExportDialogResult?> Completion => ViewModel.Completion;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateScrollLimit();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateScrollLimit();
    }

    /// <summary>
    ///     卡片尺寸跟着宿主窗口走：宽度占窗口的六成左右（夹在 760~900 之间），
    ///     高度至少占窗口的七成（用户反馈卡片太矮、勾选列表挤成一小条），
    ///     内容区自己滚，不会把「下一步」顶出屏幕。
    /// </summary>
    private void UpdateScrollLimit()
    {
        if (_scroll is null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        var hostWidth = topLevel?.ClientSize.Width ?? 0;
        var hostHeight = topLevel?.ClientSize.Height ?? 0;

        if (hostWidth > 0 && _sheet is not null)
        {
            // 窗口特别窄时优先「放得下」：下限退到窗口的九成，不让卡片顶出屏幕。
            var floor = Math.Min(760, Math.Max(320, hostWidth * 0.9));
            _sheet.Width = Math.Clamp(hostWidth * 0.58, floor, 900);
        }

        if (hostHeight <= 0)
            return;

        if (_sheet is not null)
            _sheet.MinHeight = Math.Min(hostHeight * 0.72, hostHeight * 0.92);

        _scroll.MaxHeight = Math.Max(240, hostHeight * 0.9 - 120);
        if (_scopeList is not null)
            _scopeList.MaxHeight = Math.Clamp(hostHeight * 0.42, 220, 520);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_sheet is null || _titleBar is null)
            return;

        if (!e.GetCurrentPoint(_titleBar).Properties.IsLeftButtonPressed)
            return;

        _isDragging = true;
        _dragStartPointer = e.GetPosition(_sheet.Parent as Control ?? _sheet);
        _dragStartOffset = _sheetOffset;
        e.Pointer.Capture(_titleBar);
        e.Handled = true;
    }

    private void OnTitleBarPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _sheet?.Parent is not Control parent)
            return;

        var current = e.GetPosition(parent);
        var delta = current - _dragStartPointer;
        SetSheetOffset(ClampOffset(new Point(_dragStartOffset.X + delta.X, _dragStartOffset.Y + delta.Y)));
        e.Handled = true;
    }

    private void OnTitleBarPointerReleased(object? sender, PointerEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnTitleBarPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
    }

    /// <summary>卡片默认居中，所以偏移是相对居中位置的位移，夹在「还能露出来的范围」里。</summary>
    private Point ClampOffset(Point offset)
    {
        if (_sheet?.Parent is not Control parent)
            return offset;

        var maxX = Math.Max(0, (parent.Bounds.Width - _sheet.Bounds.Width) / 2);
        var maxY = Math.Max(0, (parent.Bounds.Height - _sheet.Bounds.Height) / 2);
        return new Point(Math.Clamp(offset.X, -maxX, maxX), Math.Clamp(offset.Y, -maxY, maxY));
    }

    private void SetSheetOffset(Point offset)
    {
        if (_sheet is null)
            return;

        _sheetOffset = offset;
        _sheetTransform ??= new TranslateTransform();
        _sheetTransform.X = offset.X;
        _sheetTransform.Y = offset.Y;
        if (!ReferenceEquals(_sheet.RenderTransform, _sheetTransform))
            _sheet.RenderTransform = _sheetTransform;
    }
}
