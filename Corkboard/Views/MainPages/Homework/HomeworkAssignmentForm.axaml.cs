using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRichEditor.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Controls;
using Corkboard.Core.Notes;
using Corkboard.Core.Board;
using Corkboard.ViewModels.MainPages.Homework;

namespace Corkboard.Views.MainPages.Homework;

/// <summary>
///     「布置作业」/「编辑作业」表单，由 <c>PageOverlayService</c> 放到壳的弹层里显示。
///     <para>
///         两种用途共用同一个控件与同一份 ViewModel：编辑走 <see cref="ForEdit" /> 建实例，
///         它会把字段区预填好、把确定按钮换成「保存」。
///     </para>
///     <para>
///         卡片整体居中：高度上限只占「看得见的宿主」的 <see cref="SheetHeightRatio" />
///         （壳内弹层按宿主窗口的内容区算，置底形态按屏幕工作区算），装不下的内容由卡片自己的
///         滚动条吃掉；算上限时**逐个量出底部按钮等行的实际高度**，所以按钮永远不会被顶出卡片。
///     </para>
///     <para>
///         作业内容<b>一个框就够了</b>：表单里直接就是富文本编辑区（所见即所得），没有「原文态 / 效果态」的切换，
///         写到什么样子板子上就是什么样子；空的时候上面盖一句提示。
///     </para>
///     <para>
///         格式工具栏常驻在编辑区正上方（库自带的富文本工具栏，<c>Target</c> 在构造函数里指向编辑控件）：
///         加粗、斜体、字号、颜色这些点一下就对<b>当前选中的文字</b>生效。卡片本身不再自绘任何格式控件
///         （工具栏与编辑区都由库提供），颜色入口的色板用「设置 → 作业板 → 快速颜色」里配的那几个，
///         由 <c>RichTextToolbarPalette</c> 在打开表单之前写进库的静态色板。
///     </para>
/// </summary>
public partial class HomeworkAssignmentForm : UserControl
{
    /// <summary>卡片高度上限占可用高度的比例：卡片居中显示，上下都得留出余量。</summary>
    private const double SheetHeightRatio = 0.7;

    /// <summary>卡片高度下限：可用高度很矮时也别缩成一条。</summary>
    private const double SheetMinHeight = 320;

    /// <summary>卡片到可用区域边缘留的余量（DIP）。宽度按它留，高度靠上面的比例留。</summary>
    private const double SheetMargin = 32;

    /// <summary>
    ///     指针捕获丢失事件的订阅策略：三个都写上。它是 Direct 事件（只有捕获元素那一跳），
    ///     但按气泡派发的实现也见过，写全两种都收得到；源就是捕获元素，路由只有一跳，不会重复触发。
    /// </summary>
    private const RoutingStrategies CaptureLostRoutes =
        RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel;


    /// <summary>作业内容的富文本编辑区：加粗、字号、颜色这些格式都套在它当前的选区上。</summary>
    private readonly RichTextBlock? _editor;

    /// <summary>内容区块，右下角手柄拖的是它。</summary>
    private readonly Grid? _contentArea;

    /// <summary>内容区的外壳：画边框与底色，也是「这一下不归卡片拖动」的落点之一。</summary>
    private readonly Border? _contentChrome;

    /// <summary>内容栈（除内容区以外的那些行都在它里面），算内容区还能长多高用。</summary>
    private readonly StackPanel? _sheetBody;

    /// <summary>右下角的缩放手柄：命中它时这一下归手柄自己，不能当成卡片拖动。</summary>
    private readonly Border? _resizeGrip;

    /// <summary>卡片本体，尺寸上限写在它身上。</summary>
    private readonly Border? _sheet;

    /// <summary>顶栏：卡片上唯一能按住拖走的那一行（见 <see cref="OnHeaderPressed" />）。</summary>
    private readonly Control? _sheetHeader;

    /// <summary>编辑区正上方那条格式工具栏（库自带）：目标编辑控件在构造函数里指过去。</summary>
    private readonly RichEditorToolbar? _toolbar;

    /// <summary>手写形态的画布：笔迹存在它自己身上，撤销 / 清除都是它公开的方法。</summary>
    private readonly HandwritingCanvas? _canvas;

    /// <summary>「撤销一笔」按钮：可用性跟着画布的 <c>CanUndo</c> 走（画布不认我们这边的绑定）。</summary>
    private readonly Button? _undoButton;

    /// <summary>表单 ViewModel：形态切换时靠它的通知补收尾动作（见 <see cref="OnViewModelPropertyChanged" />）。</summary>
    private readonly HomeworkAssignmentFormViewModel? _viewModel;

    /// <summary>XAML 里写的卡片宽度上限，构造期读一次当基准（见构造函数）。</summary>
    private readonly double _sheetMaxWidth;

    /// <summary>卡片拖动时叠在它身上的位移（布局保持居中不动，见 <see cref="OnHeaderPressed" />）。</summary>
    private readonly TranslateTransform _sheetOffset = new();

    /// <summary>卡片所在的顶层：它的尺寸一变，卡片上限与位移都要重算。</summary>
    private TopLevel? _topLevel;

    private bool _isResizingContent;
    private Point _resizeStartPointer;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    private bool _isDraggingSheet;

    /// <summary>拖动卡片的指针，收尾时靠它显式交还捕获。</summary>
    private IPointer? _dragPointer;

    /// <summary>独立窗口承载时被拖的那个窗口；壳内弹层这条路为 <c>null</c>。</summary>
    private Window? _dragWindow;

    private Point _dragStartPointer;
    private Vector _dragStartOffset;

    /// <summary>卡片不含位移时的位置（窗口坐标系）与可拖范围，按下时量一次。</summary>
    private Point _dragBasePosition;
    private Size _dragArea;

    private PixelPoint _dragStartWindowPosition;
    private PixelPoint _dragStartPointerScreen;

    public HomeworkAssignmentForm()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<HomeworkAssignmentFormViewModel>();
        _editor = this.FindControl<RichTextBlock>("ContentEditor");
        _contentArea = this.FindControl<Grid>("ContentArea");
        _contentChrome = this.FindControl<Border>("ContentBoxChrome");
        _sheetBody = this.FindControl<StackPanel>("SheetBody");
        _resizeGrip = this.FindControl<Border>("ResizeGrip");
        _sheet = this.FindControl<Border>("Sheet");
        _sheetHeader = this.FindControl<Control>("SheetHeader");
        _toolbar = this.FindControl<RichEditorToolbar>("EditorToolbar");
        _canvas = this.FindControl<HandwritingCanvas>("HandwritingSurface");
        _undoButton = this.FindControl<Button>("UndoStrokeButton");

        // 形态切换后要补动作（切回打字时把焦点交给编辑器、刷撤销按钮），所以订上 ViewModel 的通知。
        _viewModel = DataContext as HomeworkAssignmentFormViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 工具栏是编辑控件的兄弟而不是它的祖先（工具栏要显示在编辑框上面，但不是它的一部分），
        // XAML 里绑不过去，只能在代码里把目标指过去。
        if (_toolbar is not null && _editor is not null)
            _toolbar.Target = _editor;

        // 库的工具条完全不认主题（反编译出来四十来处写死的浅色，见 RichTextToolbarTheme），
        // 只能在每次布局时把那些颜色换成本应用的主题画刷：首帧之后控件才挂进窗口、主题资源才查得到，
        // 库重建工具条时（切语言 / 换 ToolbarLevel）也能跟着补一遍。换过的颜色不再匹配原表，
        // 所以重复调用是空操作，只有新控件会被换。
        if (_toolbar is not null)
            _toolbar.LayoutUpdated += OnToolbarLayoutUpdated;

        // 手写画布：一笔写完 / 橡皮擦完 / 撤销 / 清除都会发 Changed（画布在发事件之前就把 HasInk、CanUndo
        // 更新好了），这里只需把结果同步回 ViewModel——确定按钮的可用性与空画布提示都看那一份状态。
        if (_canvas is not null)
            _canvas.Changed += OnHandwritingChanged;

        // XAML 里的宽度上限只读一次当基准：上限要能跟着宿主一起变大变小，
        // 不能在「上一次算出来的上限」上再收一次——窗口先小后大就再也长不回去了。
        _sheetMaxWidth = _sheet?.MaxWidth ?? double.PositiveInfinity;

        if (_sheet is not null)
        {
            // 卡片拖动只叠位移、不动布局：位移挂在卡片自己身上。
            _sheet.RenderTransform = _sheetOffset;

            // 卡片尺寸一变（拉手柄、上限跟着窗口变）就重夹位移，别让它长到视野外抓不回来。
            _sheet.PropertyChanged += OnSheetPropertyChanged;
        }

        if (_sheetBody is not null)
            _sheetBody.PropertyChanged += OnSheetBodyPropertyChanged;

        // ⚠️ 拖动处理器挂在**表单自己**身上，跟指针捕获同一个元素：拖动期间事件只从捕获元素往上走，
        // 挂在顶栏上就一个都收不到（表现就是「按住拖不动」）。
        // 气泡路由 + 不接管已处理的事件，于是关闭钮自己的点击自然让出。
        AddHandler(PointerPressedEvent, OnHeaderPressed, RoutingStrategies.Bubble);
        AddHandler(PointerMovedEvent, OnHeaderMoved, RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, OnHeaderReleased, RoutingStrategies.Bubble);
        AddHandler(PointerCaptureLostEvent, OnHeaderCaptureLost, CaptureLostRoutes);

        // 弹层是后加进可视树的：焦点要等挂上之后再给，卡片的尺寸上限也要等能拿到宿主才算。
        AttachedToVisualTree += (_, _) =>
        {
            _topLevel = TopLevel.GetTopLevel(this);
            if (_topLevel is not null)
                _topLevel.PropertyChanged += OnTopLevelPropertyChanged;

            ApplySheetSizeLimits();

            Dispatcher.UIThread.Post(
                () =>
                {
                    // 首次布局之后，宿主尺寸与卡片各行的实际高度才量得出来：
                    // 这时再对齐一次，缩放手柄的上下限才不会一拖就跳。
                    ApplySheetSizeLimits();
                    FitContentAreaToLimit();
                    SyncSheetWidthToContent();
                    ClampSheetOffset();

                    // 工具条的颜色第一遍在首帧布局之后换（见 OnToolbarLayoutUpdated）：
                    // LayoutUpdated 只会为后续的重建兜底，这一发保证「打开就生效」。
                    ApplyToolbarFixes();

                    // 画布这时才量得出尺寸，撤销按钮的初始可用性也跟着再刷一次。
                    RefreshHandwritingState();
                    ScheduleInitialFocus();
                }, DispatcherPriority.Loaded);
        };

        // 表单即用即弃：离开可视树时收掉可能还留着的拖动捕获（见 EndSheetDrag），
        // 并摘掉挂在宿主、画布与 ViewModel 上的监听。
        Unloaded += (_, _) =>
        {
            EndSheetDrag();
            RemoveHandler(PointerPressedEvent, OnFirstPointerPressedForFocus);

            if (_toolbar is not null)
                _toolbar.LayoutUpdated -= OnToolbarLayoutUpdated;

            if (_canvas is not null)
                _canvas.Changed -= OnHandwritingChanged;

            if (_viewModel is not null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

            if (_topLevel is not null)
                _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;

            _topLevel = null;
        };
    }

    /// <summary>
    ///     库工具条的两处宿主侧补救：把里面写死的浅色换成本应用主题的画刷
    ///     （<see cref="RichTextToolbarTheme" />），并给它自带的两个颜色浮层补一个真正的取色器
    ///     （<see cref="RichTextCustomColor" />）。两件都是幂等的，可以安全地接在 <c>LayoutUpdated</c> 上，
    ///     给库重建工具条（切语言 / 换 ToolbarLevel）兜底。
    /// </summary>
    private void ApplyToolbarFixes()
    {
        if (_toolbar is null)
            return;

        RichTextToolbarTheme.Apply(_toolbar);
        RichTextCustomColor.Attach(_toolbar);
    }

    private void OnToolbarLayoutUpdated(object? sender, EventArgs e) => ApplyToolbarFixes();

    #region 手写形态

    /// <summary>
    ///     把画布的状态同步回表单：有没有笔迹（确定按钮与空画布提示都看它）、还能不能撤销。
    ///     <para>
    ///         画布在发 <c>Changed</c> 之前就更新好了 <c>HasInk</c> / <c>CanUndo</c>，所以这里读到的都是最新值；
    ///         用代码设 <c>Data</c> 载入笔迹时画布不发事件，那条路由构造函数与「挂上可视树之后」主动调一次。
    ///     </para>
    /// </summary>
    private void RefreshHandwritingState()
    {
        if (_canvas is null)
            return;

        if (_viewModel is not null)
        {
            _viewModel.HasInk = _canvas.HasInk;

            // ⚠️ 画布在内部是用 SetCurrentValue 写 Data 的，TwoWay 绑定不会把新值回传给源。
            // 不在这里显式同步，表单保存时 HandwritingData 还是打开表单时的旧值（通常是 null），
            // 表现就是「手写的内容关掉再打开就没了」。
            var data = _canvas.Data;
            if (!string.Equals(_viewModel.HandwritingData, data, StringComparison.Ordinal))
                _viewModel.HandwritingData = data;
        }

        if (_undoButton is not null)
            _undoButton.IsEnabled = _canvas.CanUndo;
    }

    private void OnHandwritingChanged(object? sender, EventArgs e) => RefreshHandwritingState();

    /// <summary>撤销一笔。只撤手写画布上的最后一笔，与富文本编辑器的撤销栈无关。</summary>
    private void OnHandwritingUndoClick(object? sender, RoutedEventArgs e)
    {
        _canvas?.Undo();
        RefreshHandwritingState();
    }

    /// <summary>清空画布（不是撤一步，是全部重来）。</summary>
    private void OnHandwritingClearClick(object? sender, RoutedEventArgs e)
    {
        _canvas?.Clear();
        RefreshHandwritingState();
    }

    /// <summary>
    ///     形态切换之后的收尾：刷撤销按钮，切回打字时把键盘焦点交给富文本编辑器。
    ///     <para>手写形态不抢焦点——画布点下去就是落笔，不需要键盘，弹输入法反而挡视线。</para>
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HomeworkAssignmentFormViewModel.IsTextMode))
            return;

        RefreshHandwritingState();

        // 切形态是点按钮触发的，这会儿焦点在按钮上：等这一轮输入处理完再挪，免得更后面的处理又把它抢回去。
        Dispatcher.UIThread.Post(FocusComposeSurface, DispatcherPriority.Input);
    }

    /// <summary>打字形态才把键盘焦点交给编辑区（打开表单、以及从手写切回打字时各调一次）。</summary>
    private void FocusComposeSurface()
    {
        if (_viewModel is { IsTextMode: true })
            _editor?.Focus();
    }

    /// <summary>
    ///     刚打开表单时的焦点：等<b>第一次按下</b>落定之后再给编辑区，别在挂进可视树的当场塞。
    ///     <para>
    ///         ⚠️ 这里踩过坑：原先在 <c>AttachedToVisualTree</c> 的 Loaded 派发里直接 <c>_editor.Focus()</c>，
    ///         结果是「打开表单后不先点一下输入框，底部的取消 / 保存按钮点不动，点了输入框之后才正常」。
    ///         弹层是<b>同步</b>塞进壳里的，而这一发派发排在 <c>PointerPressed</c> 之前跑：
    ///         它在用户按下的当口把键盘焦点从「什么都没聚焦」抢到编辑区，
    ///         一次按下引发的焦点转移会让这一轮指针输入作废——按住标题栏第一次也拖不动，
    ///         与用户报的是同一个现象。所以焦点一律推迟到「用户第一次真的点进表单」之后再给。
    ///     </para>
    ///     <para>点进来之后立刻退订：表单即用即弃，这份监听只为一发。</para>
    /// </summary>
    private void ScheduleInitialFocus()
    {
        if (_viewModel is not { IsTextMode: true })
            return;

        AddHandler(PointerPressedEvent, OnFirstPointerPressedForFocus, RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnFirstPointerPressedForFocus(object? sender, PointerPressedEventArgs e)
    {
        RemoveHandler(PointerPressedEvent, OnFirstPointerPressedForFocus);

        // 等这一轮输入处理完再挪焦点：在按下这一跳里抢焦点，同一个毛病会再来一遍。
        Dispatcher.UIThread.Post(FocusComposeSurface, DispatcherPriority.Input);
    }

    /// <summary>「新建分类」输入行里回车 = 确定、Esc = 收起；其余键照常交给输入框。</summary>
    private void OnSectionNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                if (_viewModel.ConfirmAddSectionCommand.CanExecute(null))
                    _viewModel.ConfirmAddSectionCommand.Execute(null);

                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.CancelAddSectionCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    #endregion

    private HomeworkAssignmentForm(Note note) : this()
    {
        _viewModel?.LoadForEdit(note);

        // 手写作业的笔迹是经双向绑定灌进画布的：用代码设 Data 时画布**不发** Changed，
        // 所以这里主动把画布算出来的「有没有笔迹」同步回表单一次。
        RefreshHandwritingState();
    }

    /// <summary>建一个用来编辑已有作业的表单。</summary>
    public static HomeworkAssignmentForm ForEdit(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return new HomeworkAssignmentForm(note);
    }

    /// <summary>
    ///     给卡片定尺寸上限。
    ///     <para>
    ///         基准是<b>看得见的那块地方</b>而不是整块屏幕：壳内弹层取宿主窗口的内容区，
    ///         于是卡片永远留在窗口里、居中显示，窗口多小都不会被裁掉一半；
    ///         高度只占可用高度的 <see cref="SheetHeightRatio" />，上下各留一成半，
    ///         不会一路长到贴边、更不会长成一条竖长的板子。
    ///     </para>
    /// </summary>
    private void ApplySheetSizeLimits()
    {
        if (_sheet is null)
            return;

        var (availableWidth, availableHeight) = ResolveAvailableArea();

        _sheet.MaxWidth = SheetSizeLimits.ResolveMaxWidth(availableWidth, _sheetMaxWidth, _sheet.MinWidth, SheetMargin);
        _sheet.MaxHeight = SheetSizeLimits.ResolveMaxHeight(availableHeight, SheetHeightRatio, SheetMinHeight);
    }

    /// <summary>
    ///     卡片能用多大：优先取「看得见的宿主」的内容区。
    ///     <para>
    ///         独立窗口（<see cref="PageOverlayWindow" />，「置底到桌面」时承载表单）是
    ///         <c>SizeToContent</c> 的：拿它自己的尺寸当基准会和卡片互相追着长
    ///         （卡片长大 → 窗口跟着长大 → 上限又抬高），所以那条路退回<b>屏幕工作区</b>——
    ///         屏幕是外部硬约束，不会绕圈。
    ///     </para>
    ///     <para>量不到（离屏测试里连屏幕都没有）时给一组保守默认值，绝不抛在弹层显示这一步。</para>
    /// </summary>
    private (double Width, double Height) ResolveAvailableArea()
    {
        var topLevel = TopLevel.GetTopLevel(this);

        // SizeToContent 的窗口跳过它自己的尺寸，见上面的说明。
        if (topLevel is not null && topLevel is not Window { SizeToContent: not SizeToContent.Manual })
        {
            var client = topLevel.ClientSize;
            if (client.Width > 0 && client.Height > 0)
                return (client.Width, client.Height);
        }

        if (topLevel is not null)
        {
            var screens = topLevel.Screens;
            var screen = screens?.ScreenFromTopLevel(topLevel) ?? screens?.Primary;
            if (screen is not null)
            {
                var scaling = screen.Scaling > 0 ? screen.Scaling : 1d;
                return (screen.WorkingArea.Width / scaling, screen.WorkingArea.Height / scaling);
            }
        }

        return (800d, 600d);
    }

    /// <summary>
    ///     把内容区的实际尺寸收进当前限额里。
    ///     <para>
    ///         默认尺寸是按大窗口定的，宿主很小时要当场收一下：否则第一次拖手柄会「一跳」到限额上，
    ///         而且卡片一开始就冒滚动条。类型切换多出几行字段、整篇字号变大撑高一行，也走这里再收一次。
    ///     </para>
    /// </summary>
    private void FitContentAreaToLimit()
    {
        if (_contentArea is not { } area)
            return;

        var (maxWidth, maxHeight) = ResolveContentSizeLimit();

        if (area.Width > maxWidth)
            area.Width = Math.Max(area.MinWidth, maxWidth);

        if (area.Height > maxHeight)
            area.Height = Math.Max(area.MinHeight, maxHeight);

        SyncSheetWidthToContent();
    }

    /// <summary>
    ///     内容栈的高度变了（多出字段行、某一行被撑高…）就把内容区再收进限额里。
    ///     <para>拖手柄的过程中不动它，否则会和用户的手对着干（见 <see cref="OnResizeGripMoved" />）。</para>
    /// </summary>
    private void OnSheetBodyPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty && !_isResizingContent)
            FitContentAreaToLimit();
    }

    /// <summary>宿主窗口尺寸一变：上限跟着变，内容区与位移也要重新对齐。</summary>
    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TopLevel.ClientSizeProperty)
            return;

        ApplySheetSizeLimits();
        FitContentAreaToLimit();
        ClampSheetOffset();
    }

    private void OnResizeGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_contentArea is null)
            return;

        _isResizingContent = true;
        _resizeStartPointer = e.GetPosition(this);
        _resizeStartWidth = _contentArea.Bounds.Width;
        _resizeStartHeight = _contentArea.Bounds.Height;

        if (sender is IInputElement grip)
            e.Pointer.Capture(grip);

        e.Handled = true;
    }

    /// <summary>拖动中：横向改宽度、纵向改高度，各自卡在自己的上下限里。</summary>
    private void OnResizeGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingContent || _contentArea is null)
            return;

        // 左键已经抬起来（在窗口外抬起、捕获被抢走时收不到 PointerReleased）就当场收尾，
        // 别让捕获留在手柄上——那会让整个表单点不动。
        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndResize();
            return;
        }

        var delta = e.GetPosition(this) - _resizeStartPointer;
        var (maxWidth, maxHeight) = ResolveContentSizeLimit();

        _contentArea.Width = Math.Clamp(_resizeStartWidth + delta.X, _contentArea.MinWidth, maxWidth);
        _contentArea.Height = Math.Clamp(_resizeStartHeight + delta.Y, _contentArea.MinHeight, maxHeight);

        // 卡片宽度跟着内容区走：内容变宽卡片就变宽，弹层是把卡片居中摆的，于是整张框始终在窗口中间。
        SyncSheetWidthToContent();
    }

    /// <summary>
    ///     让卡片宽度 = 内容区宽度 + 卡片内边距。
    ///     <para>
    ///         ⚠️ 卡片外面套着纵向 <c>ScrollViewer</c>，横向滚动被关掉时它会用视口宽度去量内容，
    ///         内容区再宽也撑不宽卡片——拖右下角手柄时卡片不跟着长，看起来就像「内容跑到框外面、框不居中」。
    ///         所以这里直接给卡片一个显式宽度（跟着内容区走），卡片由弹层居中摆放，拖大拖小都停在窗口中央。
    ///     </para>
    /// </summary>
    private void SyncSheetWidthToContent()
    {
        if (_sheet is null || _contentArea is null)
            return;

        var contentWidth = double.IsFinite(_contentArea.Width) && _contentArea.Width > 0
            ? _contentArea.Width
            : _contentArea.Bounds.Width;

        if (!double.IsFinite(contentWidth) || contentWidth <= 0)
            return;

        var padding = _sheet.Padding.Left + _sheet.Padding.Right
                      + _sheet.BorderThickness.Left + _sheet.BorderThickness.Right;

        var width = contentWidth + padding;
        if (double.IsFinite(_sheet.MaxWidth))
            width = Math.Min(width, _sheet.MaxWidth);
        if (double.IsFinite(_sheet.MinWidth))
            width = Math.Max(width, _sheet.MinWidth);

        if (double.IsFinite(_sheet.Width) && Math.Abs(_sheet.Width - width) <= 0.5)
            return;

        _sheet.Width = width;
        _sheet.InvalidateMeasure();
    }

    private void OnResizeGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndResize();
        e.Pointer.Capture(null);
    }

    /// <summary>捕获被别处抢走时收不到 <c>PointerReleased</c>，自己把状态收掉。</summary>
    private void OnResizeGripCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndResize();

    private void EndResize() => _isResizingContent = false;

    /// <summary>
    ///     内容区块能拉到多大。
    ///     <para>
    ///         宽度 = 卡片最大宽度减内边距（拉到卡片上限为止，卡片跟着变宽）。
    ///         高度 = 卡片最大高度扣掉「除内容区以外那些行」——**底部按钮行也是这里量出来的**，
    ///     所以内容区再怎么拉，按钮都留在卡片里。
    ///     </para>
    ///     <para>
    ///         那些行是**逐个量**的，不用「内容栈整高减内容区」倒推：卡片顶到上限、内部开始滚动时，
    ///         内容栈的高度会被裁到视口那么高，倒推出来的「其它行高度」会偏小，
    ///         于是内容区能一路拉大、把底部按钮顶出视野（这正是之前的毛病）。
    ///     </para>
    /// </summary>
    private (double MaxWidth, double MaxHeight) ResolveContentSizeLimit()
    {
        if (_sheet is null || _contentArea is not { } area || _sheetBody is null)
            return (double.PositiveInfinity, double.PositiveInfinity);

        var padding = _sheet.Padding.Left + _sheet.Padding.Right;
        var maxWidth = Math.Max(area.MinWidth, _sheet.MaxWidth - padding);

        // 除内容区以外的行（标题、类型、科目、字段、底部按钮…）各占多高，加上行距与卡片内边距。
        var chrome = _sheetBody.Children
            .Where(child => !ReferenceEquals(child, area))
            .Sum(child => child.Bounds.Height);
        chrome += _sheetBody.Spacing * Math.Max(0, _sheetBody.Children.Count - 1);
        chrome += _sheet.Padding.Top + _sheet.Padding.Bottom;

        var maxHeight = Math.Max(area.MinHeight, _sheet.MaxHeight - chrome);

        return (maxWidth, maxHeight);
    }

    #region 卡片拖动（只有顶栏能拖）

    /// <summary>
    ///     顶栏按下：接管指针，之后按位移拖动整张卡片。
    ///     <para>
    ///         <b>只有顶栏那一行是拖动面</b>——标题行的底色、行距、留白都能拖，但内容区、表单行、
    ///         底部按钮行都不行。曾经做过「整张卡片都能拖」，用户当场要求删掉（标题行与留白太容易误拖，
    ///         触屏更甚），所以这里改成按命中判定：只有落在 <see cref="SheetHeader" /> 里的按下才算拖动。
    ///     </para>
    ///     <para>
    ///         两种承载形态拖的不是同一个东西：
    ///         <list type="bullet">
    ///             <item>
    ///                 <b>壳内弹层</b>（默认，见 <c>PageOverlayService</c>）：挪卡片自己——给卡片叠一层位移，
    ///                 布局保持居中不动，所以「能挪多远」得自己夹（<see cref="SheetDragLimits" />）。
    ///             </item>
    ///             <item>
    ///                 <b>独立窗口</b>（<see cref="PageOverlayWindow" />，「置底到桌面」时用）：
    ///                 那个窗口是 <c>SizeToContent</c> 的、大小正好等于卡片，挪卡片等于把卡片推出窗口被裁掉。
    ///                 所以这时拖的是窗口本身，跟主窗口标题栏同一个做法（按 <c>Window.Position</c> 手动位移）。
    ///             </item>
    ///         </list>
    ///     </para>
    /// </summary>
    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        // 上一次拖动如果没收到 PointerReleased / PointerCaptureLost（指针在窗口外抬起、
        // 捕获被别处抢走等），捕获会留在表单上，表现就是「卡片再也拖不动」。
        // 新的按下是最好的收尾时机：先清干净，再按这次按下重新判定。
        EndSheetDrag();

        if (_sheet is null || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        // 触摸没有「左键」概念，与页面里其它手势判定保持一致。
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Pointer.Type != PointerType.Touch)
            return;

        // 命中顶栏以外的地方（内容区、下拉、按钮…）一律让给控件自己。
        if (!IsInSheetHeader(e.Source))
            return;

        _isDraggingSheet = true;
        _dragPointer = e.Pointer;
        _dragStartPointer = e.GetPosition(topLevel);
        _dragStartOffset = new Vector(_sheetOffset.X, _sheetOffset.Y);

        if (topLevel is PageOverlayWindow window)
        {
            _dragWindow = window;
            _dragStartWindowPosition = window.Position;
            _dragStartPointerScreen = GetPointerScreenPoint(e);
        }
        else
        {
            _dragBasePosition = ResolveSheetBasePosition();
            _dragArea = topLevel.ClientSize;
        }

        // 捕获挂在表单上，不挂顶栏里的子元素：表单的子树不会因为拖动被重建，命中面也一直有效，
        // 而拖动处理器就挂在同一个元素上（见构造函数），事件才收得到。
        // **收尾必须显式 Capture(null)**——指针在窗口外抬起收不到 PointerReleased，
        // 捕获留在表单上会让卡片里的输入框点不动（壳的整窗拖动踩过同一个坑）。
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>拖动中：壳内挪卡片（夹在宿主里），独立窗口挪窗口。</summary>
    private void OnHeaderMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingSheet)
            return;

        // 左键已经抬起来（指针在窗口外抬起、捕获被别处抢走时收不到 PointerReleased）就当场收尾。
        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndSheetDrag();
            return;
        }

        if (_dragWindow is { } window)
        {
            MoveWindowDrag(window, e);
            return;
        }

        if (_sheet is null || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        // ⚠️ 位移要在**同一个坐标系**里量：按下时量的是顶层坐标（见 OnHeaderPressed），
        // 这里也要用顶层坐标，而且必须换成「相对拖动起点」的增量——
        // 直接拿指针的屏幕坐标减起点只会得到 0（两个量都在顶层坐标系里，窗口还没动）。
        var delta = e.GetPosition(topLevel) - _dragStartPointer;
        var size = _sheet.Bounds.Size;

        _sheetOffset.X = SheetDragLimits.ClampOffset(
            _dragStartOffset.X + delta.X, _dragBasePosition.X, size.Width, _dragArea.Width);
        _sheetOffset.Y = SheetDragLimits.ClampOffset(
            _dragStartOffset.Y + delta.Y, _dragBasePosition.Y, size.Height, _dragArea.Height);

        e.Handled = true;
    }

    /// <summary>松手：收尾并把捕获显式还回去。</summary>
    private void OnHeaderReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingSheet)
            return;

        EndSheetDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>捕获被别处抢走时收不到 <c>PointerReleased</c>，自己把状态收掉。</summary>
    private void OnHeaderCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndSheetDrag();

    /// <summary>这一下是不是落在顶栏里（顶栏自己、标题、关闭钮都算；关闭钮另外由它自己接管点击）。</summary>
    private bool IsInSheetHeader(object? source)
    {
        if (_sheetHeader is null)
            return false;

        for (Visual? current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, _sheetHeader))
                return true;

            // 走到卡片自己就说明这一下在顶栏以外，不用再往上找。
            if (ReferenceEquals(current, _sheet))
                return false;
        }

        return false;
    }

    /// <summary>
    ///     卡片不含位移时的位置（窗口坐标系），算拖动范围用。
    ///     <para>
    ///         沿父链把各级的 <see cref="Visual.Bounds" /> 位置加起来：<c>Bounds</c> 是布局结果、
    ///         不含 <c>RenderTransform</c>，所以量出来的是<b>基准位置</b>，不会被正在拖的那层位移绕进去。
    ///     </para>
    /// </summary>
    private Point ResolveSheetBasePosition()
    {
        if (_sheet is null)
            return default;

        var x = _sheet.Bounds.X;
        var y = _sheet.Bounds.Y;

        for (var current = _sheet.GetVisualParent(); current is not null; current = current.GetVisualParent())
        {
            x += current.Bounds.X;
            y += current.Bounds.Y;
        }

        return new Point(x, y);
    }

    /// <summary>
    ///     拖动独立窗口：按 <c>Window.Position</c> 手动位移，跟主窗口标题栏同一套做法。
    ///     指针位置取<b>屏幕绝对坐标</b>——窗口跟着指针走，用相对位移会自我抵消。
    /// </summary>
    private void MoveWindowDrag(Window window, PointerEventArgs e)
    {
        var current = GetPointerScreenPoint(e);
        var target = new PixelPoint(
            _dragStartWindowPosition.X + (current.X - _dragStartPointerScreen.X),
            _dragStartWindowPosition.Y + (current.Y - _dragStartPointerScreen.Y));

        if (target != window.Position)
            window.Position = target;
    }

    /// <summary>指针的屏幕物理坐标：窗口左上角 + 指针相对窗口的偏移（按缩放折算）。</summary>
    private PixelPoint GetPointerScreenPoint(PointerEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var windowPosition = (topLevel as Window)?.Position ?? default;
        var scaling = topLevel?.RenderScaling ?? 1.0;
        var position = e.GetPosition(this);

        return new PixelPoint(
            windowPosition.X + (int)Math.Round(position.X * scaling),
            windowPosition.Y + (int)Math.Round(position.Y * scaling));
    }

    /// <summary>
    ///     把卡片位移重新夹回宿主里。
    ///     <para>
    ///         拖动时夹一次不够：卡片变大（拉手柄）、宿主变小（用户缩窗口）都会让原来的位移越界，
    ///         卡片有一半跑到视野外就再也抓不回来（它没有系统标题栏，也没有别的归位入口）。
    ///         所以卡片尺寸一变、宿主尺寸一变都重夹一次。
    ///     </para>
    ///     <para>置底形态下卡片就是那个独立窗口，位移恒为 0，这里自然什么都不做。</para>
    /// </summary>
    private void ClampSheetOffset()
    {
        if (_sheet is null || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        // 独立窗口那条路上卡片就是窗口本身，位移一直是 0，没有可夹的东西。
        if (topLevel is PageOverlayWindow)
            return;

        var host = topLevel.ClientSize;
        if (host.Width <= 0 || host.Height <= 0)
            return;

        var basePosition = ResolveSheetBasePosition();
        var size = _sheet.Bounds.Size;

        _sheetOffset.X = SheetDragLimits.ClampOffset(_sheetOffset.X, basePosition.X, size.Width, host.Width);
        _sheetOffset.Y = SheetDragLimits.ClampOffset(_sheetOffset.Y, basePosition.Y, size.Height, host.Height);
    }

    /// <summary>卡片尺寸一变就重夹位移：拉大之后它必须还在视野里。</summary>
    private void OnSheetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty)
            ClampSheetOffset();
    }

    /// <summary>
    ///     收尾：还回指针捕获、清掉拖动状态。
    ///     <para>捕获必须显式还：留着会让卡片里的输入框点不动、打不了字。</para>
    /// </summary>
    private void EndSheetDrag()
    {
        _dragPointer?.Capture(null);
        _dragPointer = null;
        _dragWindow = null;
        _isDraggingSheet = false;
    }

    #endregion

    /// <summary>顶栏右上那颗「关闭」：跟底部「取消」一个意思（不保存直接收起表单）。</summary>
    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.CancelCommand.CanExecute(null) == true)
            _viewModel.CancelCommand.Execute(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
