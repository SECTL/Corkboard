using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Controls;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Services.Board;
using Corkboard.ViewModels.MainPages;

namespace Corkboard.Views.MainPages;

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
///         格式（颜色 / 字号 / Markdown 记号）不在卡片上常驻一排控件，而是选中文字之后
///         在选段上方弹一小条浮窗（<c>FormatBar</c>，Office 那种手感）；浮窗开着的时候，
///         这次操作作用于**记下来的那段选区**——点浮窗会让原文框失焦，直接读它当前选区会读到空的。
///     </para>
/// </summary>
public partial class BoardAssignmentForm : UserControl
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

    /// <summary>作业内容的 Markdown 原文输入框。焦点与套格式要用的选区都从它这里取。</summary>
    private readonly TextBox? _contentBox;

    /// <summary>内容区块（原文 + 预览），右下角手柄拖的是它。</summary>
    private readonly Grid? _contentArea;

    /// <summary>内容栈（除内容区以外的那些行都在它里面），算内容区还能长多高用。</summary>
    private readonly StackPanel? _sheetBody;

    /// <summary>右下角的缩放手柄：命中它时这一下归手柄自己，不能当成卡片拖动。</summary>
    private readonly Border? _resizeGrip;

    /// <summary>卡片本体，尺寸上限写在它身上。</summary>
    private readonly Border? _sheet;

    /// <summary>格式浮窗本体与它的定位锚点。</summary>
    private readonly Popup? _formatBar;
    private readonly Border? _formatAnchor;

    /// <summary>浮窗上的「字号」下拉：显示选段的当前字号，选一个就套上去。</summary>
    private readonly ComboBox? _sizeBox;

    /// <summary>锚点所在的层，浮窗坐标按它算。</summary>
    private readonly Canvas? _formatLayer;

    /// <summary>XAML 里写的卡片宽度上限，构造期读一次当基准（见构造函数）。</summary>
    private readonly double _sheetMaxWidth;

    /// <summary>卡片拖动时叠在它身上的位移（布局保持居中不动，见 <see cref="OnSheetPressed" />）。</summary>
    private readonly TranslateTransform _sheetOffset = new();

    /// <summary>卡片所在的顶层：它的尺寸一变，卡片上限与位移都要重算。</summary>
    private TopLevel? _topLevel;

    /// <summary>
    ///     格式浮窗开着时记下的选段（原文框的 SelectionStart/SelectionEnd）。
    ///     点浮窗上的控件会让原文框失焦，之后再读它的选区可能已经被清掉，所以要在弹出来的那一刻记牢。
    /// </summary>
    private bool _hasFormatSelection;
    private int _formatSelectionStart;
    private int _formatSelectionEnd;

    /// <summary>浮窗里正在开着子弹层（颜色取色器 / 字号下拉）的个数：那时不能把浮窗收掉。</summary>
    private int _openChildPopups;

    /// <summary>正在把「选段当前字号」写进下拉：这时的选择变化不是用户操作，别当成套字号。</summary>
    private bool _suppressSizeApply;

    /// <summary>正在由浮窗改文本（套 Markdown）：这时的文本变化不算「用户自己打字」，别收浮窗。</summary>
    private bool _isApplyingFormat;

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

    public BoardAssignmentForm()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<BoardAssignmentFormViewModel>();
        _contentBox = this.FindControl<TextBox>("ContentBox");
        _contentArea = this.FindControl<Grid>("ContentArea");
        _sheetBody = this.FindControl<StackPanel>("SheetBody");
        _resizeGrip = this.FindControl<Border>("ResizeGrip");
        _sheet = this.FindControl<Border>("Sheet");
        _formatBar = this.FindControl<Popup>("FormatBar");
        _formatAnchor = this.FindControl<Border>("FormatAnchor");
        _formatLayer = this.FindControl<Canvas>("FormatLayer");
        _sizeBox = this.FindControl<ComboBox>("SelectionSizeBox");

        // XAML 里的宽度上限只读一次当基准：上限要能跟着宿主一起变大变小，
        // 不能在「上一次算出来的上限」上再收一次——窗口先小后大就再也长不回去了。
        _sheetMaxWidth = _sheet?.MaxWidth ?? double.PositiveInfinity;

        // Avalonia 12 的 TextBox 没有 SelectionChanged 事件，选区是 StyledProperty，
        // 只能从属性变化里盯（见 OnContentBoxPropertyChanged）。
        if (_contentBox is not null)
        {
            _contentBox.PropertyChanged += OnContentBoxPropertyChanged;

            // 在原文框里松开指针＝一次选段结束：这时才弹格式浮窗（见 OnContentBoxPointerReleased）。
            _contentBox.AddHandler(PointerReleasedEvent, OnContentBoxPointerReleased, RoutingStrategies.Bubble);
        }

        if (_sheet is not null)
        {
            // 卡片拖动只叠位移、不动布局：位移挂在卡片自己身上。
            _sheet.RenderTransform = _sheetOffset;

            // 卡片尺寸一变（拉手柄、上限跟着窗口变）就重夹位移，别让它长到视野外抓不回来。
            _sheet.PropertyChanged += OnSheetPropertyChanged;
        }

        if (_sheetBody is not null)
            _sheetBody.PropertyChanged += OnSheetBodyPropertyChanged;

        if (_formatBar is not null)
        {
            // 浮窗挂在锚点上，由锚点决定它出现在哪儿（Placement=Top → 压在选段上方）。
            if (_formatAnchor is not null)
                _formatBar.PlacementTarget = _formatAnchor;

            // 浮窗内容是独立弹层，不保证继承到表单的 DataContext，显式给一份（按钮上的绑定全靠它）。
            if (_formatBar.Child is Control barContent)
                barContent.DataContext = DataContext;

            _formatBar.Closed += (_, _) => _hasFormatSelection = false;
        }

        // ⚠️ 拖动处理器挂在**表单自己**身上，跟指针捕获同一个元素：拖动期间事件只从捕获元素往上走，
        // 挂在卡片里的子元素上就一个都收不到（表现就是「按住拖不动」）。
        // 气泡路由 + 不接管已处理的事件，于是输入控件自己的手势（选字、拉开下拉、拖手柄）自然让出。
        AddHandler(PointerPressedEvent, OnSheetPressed, RoutingStrategies.Bubble);
        AddHandler(PointerMovedEvent, OnSheetMoved, RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, OnSheetReleased, RoutingStrategies.Bubble);
        AddHandler(PointerCaptureLostEvent, OnSheetCaptureLost, CaptureLostRoutes);

        // 弹层是后加进可视树的：焦点要等挂上之后再给，卡片的尺寸上限也要等能拿到宿主才算。
        AttachedToVisualTree += (_, _) =>
        {
            _topLevel = TopLevel.GetTopLevel(this);
            if (_topLevel is not null)
            {
                _topLevel.PropertyChanged += OnTopLevelPropertyChanged;

                // 在别处按下（卡片上、遮罩上）就把浮窗收掉；子弹层与浮窗自己没有背景，
                // 靠这两条判断让出去。用 Tunnel + handledEventsToo 才能连输入控件吃掉的按下也看见。
                _topLevel.AddHandler(PointerPressedEvent, OnTopLevelPointerPressed,
                    RoutingStrategies.Tunnel, handledEventsToo: true);
                _topLevel.AddHandler(KeyDownEvent, OnTopLevelKeyDown,
                    RoutingStrategies.Tunnel, handledEventsToo: true);
            }

            ApplySheetSizeLimits();

            Dispatcher.UIThread.Post(
                () =>
                {
                    // 首次布局之后，宿主尺寸与卡片各行的实际高度才量得出来：
                    // 这时再对齐一次，缩放手柄的上下限才不会一拖就跳。
                    ApplySheetSizeLimits();
                    FitContentAreaToLimit();
                    ClampSheetOffset();
                    _contentBox?.Focus();
                    SyncSelection();
                }, DispatcherPriority.Loaded);
        };

        // 表单即用即弃：离开可视树时收掉可能还留着的拖动捕获与浮窗（见 EndSheetDrag），
        // 并摘掉挂在宿主上的监听。
        Unloaded += (_, _) =>
        {
            EndSheetDrag();
            CloseFormatBar();

            if (_topLevel is not null)
            {
                _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
                _topLevel.RemoveHandler(PointerPressedEvent, OnTopLevelPointerPressed);
                _topLevel.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
            }

            _topLevel = null;
        };
    }

    private BoardAssignmentForm(BoardNote note) : this()
    {
        (DataContext as BoardAssignmentFormViewModel)?.LoadForEdit(note);
    }

    /// <summary>建一个用来编辑已有作业的表单。</summary>
    public static BoardAssignmentForm ForEdit(BoardNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return new BoardAssignmentForm(note);
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

    /// <summary>在别处按下就收掉格式浮窗；浮窗里的控件与它拉起的子弹层不算「别处」。</summary>
    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsFormatBarOpen || _openChildPopups > 0 || IsWithinFormatBar(e.Source))
            return;

        CloseFormatBar();
    }

    /// <summary>Esc 也能收掉格式浮窗。</summary>
    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsFormatBarOpen)
            CloseFormatBar();
    }

    /// <summary>把输入框当前的选区递给 ViewModel——套格式得知道改的是哪一段。</summary>
    private void SyncSelection()
    {
        if (_contentBox is { } box && DataContext is BoardAssignmentFormViewModel viewModel)
            viewModel.UpdateSelection(box.SelectionStart, box.SelectionEnd);
    }

    /// <summary>
    ///     选区的变化要从属性里盯（Avalonia 12 的 TextBox 没有 SelectionChanged 事件）；
    ///     文本变了（用户打字）就说明这一次格式操作已经结束，把浮窗收掉。
    /// </summary>
    private void OnContentBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
        {
            SyncSelection();
            return;
        }

        if (e.Property == TextBox.TextProperty && !_isApplyingFormat)
            CloseFormatBar();
    }

    /// <summary>在原文框里松开指针＝一次选段结束：把格式浮窗弹到选段上方。</summary>
    private void OnContentBoxPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        OpenFormatBar(e);
    }

    /// <summary>
    ///     选段字号。下拉里显示的是选段**当前**的字号（见 <see cref="SyncFormatBarValues" />），
    ///     换成另一档就套到选段上。
    /// </summary>
    private void OnSelectionSizeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSizeApply || sender is not ComboBox combo)
            return;

        if (combo.SelectedItem is BoardFontSizeOption option
            && DataContext is BoardAssignmentFormViewModel viewModel)
        {
            PushFormatSelection();
            viewModel.ApplyFontSizeToSelection(option.Size);
        }
    }

    /// <summary>
    ///     把浮窗上的「字号」显示成选段当前正在生效的字号。
    ///     <para>
    ///         不显示的话用户得先猜「现在是多少号」，而且这里原来当菜单用、选完就复位成空，
    ///     选中文字后下拉永远是空的。
    ///     </para>
    /// </summary>
    private void SyncFormatBarValues()
    {
        if (_sizeBox is null || DataContext is not BoardAssignmentFormViewModel viewModel)
            return;

        var size = viewModel.ResolveSelectionFontSize();
        var current = viewModel.SizeOptions.FirstOrDefault(
            option => option.Size is { } candidate && Math.Abs(candidate - size) < 0.01);

        _suppressSizeApply = true;
        try
        {
            _sizeBox.SelectedItem = current;
        }
        finally
        {
            _suppressSizeApply = false;
        }
    }

    #region 格式浮窗

    /// <summary>浮窗是否正开着。</summary>
    private bool IsFormatBarOpen => _formatBar is { IsOpen: true };

    /// <summary>
    ///     弹出格式浮窗：把锚点挪到「松开指针的地方」（浮窗按 Placement=Top 压在选段上方），
    ///     并把这一次要操作的选段记牢——后面点浮窗上的控件时原文框已经失焦，不能再指望它的选区。
    /// </summary>
    private void OpenFormatBar(PointerEventArgs e)
    {
        if (_contentBox is not { } box || _formatBar is null || _formatAnchor is null || _formatLayer is null)
            return;

        var start = Math.Min(box.SelectionStart, box.SelectionEnd);
        var end = Math.Max(box.SelectionStart, box.SelectionEnd);

        // 没选中就不弹：浮窗是「选中文字之后」才出现的东西，光标停着不该冒出来。
        if (end <= start)
        {
            CloseFormatBar();
            return;
        }

        _hasFormatSelection = true;
        _formatSelectionStart = start;
        _formatSelectionEnd = end;
        (DataContext as BoardAssignmentFormViewModel)?.UpdateSelection(start, end);

        var point = e.GetPosition(_formatLayer);
        Canvas.SetLeft(_formatAnchor, point.X);
        Canvas.SetTop(_formatAnchor, point.Y);

        // 锚点刚挪过位：先让布局跟上来，否则浮窗会按锚点的旧位置定位（看起来就是「弹到卡片角上」）。
        _formatAnchor.UpdateLayout();

        // 浮窗上的「字号」跟着新选段走：不管浮窗是不是已经开着，这次都按新选段刷新。
        SyncFormatBarValues();

        // 已经开着就只换位置与选段，不重开：重开会把正在操作的下拉收起来。
        if (_formatBar.IsOpen)
            return;

        _formatBar.IsOpen = true;
    }

    private void CloseFormatBar()
    {
        _hasFormatSelection = false;

        if (_formatBar is { IsOpen: true })
            _formatBar.IsOpen = false;
    }

    /// <summary>
    ///     把记下的选段塞回 ViewModel。**每次套格式之前都要先调它**：
    ///     点浮窗会让原文框失焦，直接读它当前的选区会拿到空选区（那就变成「改光标所在的那一段」了，
    ///     用户看到的现象是「选了一小段却改了整行」）。
    /// </summary>
    private void PushFormatSelection()
    {
        if (!_hasFormatSelection)
            return;

        (DataContext as BoardAssignmentFormViewModel)?.UpdateSelection(_formatSelectionStart, _formatSelectionEnd);
    }

    /// <summary>「+」按钮：先把选段塞回去，之后取色器每改一次色都套在这段上。</summary>
    private void OnSelectionColorClicked(object? sender, RoutedEventArgs e) => PushFormatSelection();

    /// <summary>
    ///     点预设色块：一次点击就把这个颜色套到选段上（不用先开取色器、再选色，那是两次点击）。
    ///     颜色取自色块行的 DataContext（色板是可配置的，色块由 ItemsControl 生成，
    ///     拿 DataContext 比顺着 Border.Background 去猜更稳）。
    /// </summary>
    private void OnPaletteColorClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Color color })
            return;

        PushFormatSelection();

        // 走草稿属性：套色与「自定义颜色」共用同一条路，取色器下次打开也停在这个色上。
        if (DataContext is BoardAssignmentFormViewModel viewModel)
            viewModel.SelectionColorDraft = color;
    }

    private void OnMarkdownBoldClicked(object? sender, RoutedEventArgs e) => ApplyMarkdown(BoardMarkdownFormat.Bold);

    private void OnMarkdownItalicClicked(object? sender, RoutedEventArgs e) => ApplyMarkdown(BoardMarkdownFormat.Italic);

    /// <summary>清掉选段的颜色与字号标注（Markdown 记号是原文，得自己删）。</summary>
    private void OnClearSelectionFormatClicked(object? sender, RoutedEventArgs e)
    {
        PushFormatSelection();
        (DataContext as BoardAssignmentFormViewModel)?.ClearSelectionFormat();
        SyncFormatBarValues();
    }

    /// <summary>
    ///     套一个 Markdown 记号，并把原文框的选区摆到记号之外那一段（用户能接着点下一个格式）。
    /// </summary>
    private void ApplyMarkdown(BoardMarkdownFormat format)
    {
        if (DataContext is not BoardAssignmentFormViewModel viewModel)
            return;

        PushFormatSelection();

        _isApplyingFormat = true;
        try
        {
            if (viewModel.ApplyMarkdown(format) is not { } span)
                return;

            _hasFormatSelection = true;
            _formatSelectionStart = span.Start;
            _formatSelectionEnd = span.Start + span.Length;

            if (_contentBox is { } box)
            {
                // 焦点还给原文框：套完记号接着打字仍然落在正确的位置上。
                box.Focus();
                box.SelectionStart = span.Start;
                box.SelectionEnd = span.Start + span.Length;
            }

            // 记号是插在原文里的，选段挪了：字号显示也跟着新选段刷新。
            SyncFormatBarValues();
        }
        finally
        {
            _isApplyingFormat = false;
        }
    }

    /// <summary>浮窗里的下拉拉开/收起：这时按别处不能把浮窗（连同下拉）收掉。</summary>
    private void OnFormatComboDropDownOpened(object? sender, EventArgs e) => _openChildPopups++;

    private void OnFormatComboDropDownClosed(object? sender, EventArgs e) => _openChildPopups = Math.Max(0, _openChildPopups - 1);

    private void OnSelectionColorFlyoutOpened(object? sender, EventArgs e) => _openChildPopups++;

    private void OnSelectionColorFlyoutClosed(object? sender, EventArgs e) => _openChildPopups = Math.Max(0, _openChildPopups - 1);

    /// <summary>
    ///     按下是不是落在格式浮窗里。浮窗可能被宿主放进<b>独立弹层窗口</b>，也可能是同窗口的浮层，
    ///     所以按「是不是浮窗内容那棵树的后代」判断，而不是按坐标猜。
    /// </summary>
    private bool IsWithinFormatBar(object? source)
    {
        var content = _formatBar?.Child as Visual;

        for (Visual? current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, content))
                return true;
        }

        return false;
    }

    #endregion

    /// <summary>右下角手柄按下：记下起点与当前尺寸，并把指针接管过来，拖出卡片外也跟得住。</summary>
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

    #region 卡片拖动

    /// <summary>
    ///     卡片按下：接管指针，之后按位移拖动整张卡片。
    ///     <para>
    ///         <b>整张卡片都是拖动面</b>——按住卡片上任何非输入控件的地方（标题、行距、留白、
    ///         预览区）都能拖走，所以在哪拖都行、也不需要一个「抓手」图标来指路。
    ///         输入控件与右下角手柄命中时让出（见 <see cref="IsInteractiveSource" />）。
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
    private void OnSheetPressed(object? sender, PointerPressedEventArgs e)
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

        // 命中输入控件、缩放手柄或格式浮窗时这一下归它们：不然选不中字、下拉打不开、手柄也拖不动。
        if (IsInteractiveSource(e.Source) || IsWithinFormatBar(e.Source))
            return;

        _isDraggingSheet = true;
        _dragPointer = e.Pointer;
        _dragStartPointer = e.GetPosition(this);
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

        // 捕获挂在表单上，不挂卡片里的子元素：表单的子树不会因为拖动被重建，命中面也一直有效，
        // 而拖动处理器就挂在同一个元素上（见构造函数），事件才收得到。
        // **收尾必须显式 Capture(null)**——指针在窗口外抬起收不到 PointerReleased，
        // 捕获留在表单上会让卡片里的输入框点不动（壳的整窗拖动踩过同一个坑）。
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>拖动中：壳内挪卡片（夹在宿主里），独立窗口挪窗口。</summary>
    private void OnSheetMoved(object? sender, PointerEventArgs e)
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

        if (_sheet is null)
            return;

        var delta = e.GetPosition(this) - _dragStartPointer;
        var size = _sheet.Bounds.Size;

        _sheetOffset.X = SheetDragLimits.ClampOffset(
            _dragStartOffset.X + delta.X, _dragBasePosition.X, size.Width, _dragArea.Width);
        _sheetOffset.Y = SheetDragLimits.ClampOffset(
            _dragStartOffset.Y + delta.Y, _dragBasePosition.Y, size.Height, _dragArea.Height);
    }

    /// <summary>松手：收尾并把捕获显式还回去。</summary>
    private void OnSheetReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndSheetDrag();
        e.Pointer.Capture(null);
    }

    /// <summary>捕获被别处抢走时收不到 <c>PointerReleased</c>，自己把状态收掉。</summary>
    private void OnSheetCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndSheetDrag();

    /// <summary>
    ///     命中卡片里的输入控件（或右下角缩放手柄）时，这一下不归卡片拖动。
    ///     <para>
    ///         漏掉任何一类，那个控件就会变成「点不动」——跟壳的 <c>MainView.IsInteractiveSource</c>
    ///         是同一张排除表的两半，往表单里加控件时要一起看。
    ///     </para>
    /// </summary>
    private bool IsInteractiveSource(object? source)
    {
        for (Visual? current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            // 走到卡片自己就说明命中的是卡片本身或它以外的结构，不是输入控件。
            if (ReferenceEquals(current, _sheet))
                return false;

            if (ReferenceEquals(current, _resizeGrip)
                || current is Button or ToggleButton or TextBox or ComboBox or NumericUpDown or RangeBase)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     卡片不含位移时的位置（窗口坐标系），算拖动范围用。
    ///     <para>
    ///         沿父链把各级的 <see cref="Visual.Bounds" /> 位置加起来：<c>Bounds</c> 是布局结果、
    ///     不含 <c>RenderTransform</c>，所以量出来的是<b>基准位置</b>，不会被正在拖的那层位移绕进去。
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
