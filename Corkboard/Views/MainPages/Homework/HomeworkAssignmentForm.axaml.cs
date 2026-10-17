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

    /// <summary>卡片所在的顶层：它的尺寸一变，卡片上限就要重算。</summary>
    private TopLevel? _topLevel;

    private bool _isResizingContent;
    private Point _resizeStartPointer;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

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

        if (_sheetBody is not null)
            _sheetBody.PropertyChanged += OnSheetBodyPropertyChanged;

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

                    // 工具条的颜色第一遍在首帧布局之后换（见 OnToolbarLayoutUpdated）：
                    // LayoutUpdated 只会为后续的重建兜底，这一发保证「打开就生效」。
                    ApplyToolbarFixes();

                    // 画布这时才量得出尺寸，撤销按钮的初始可用性也跟着再刷一次。
                    RefreshHandwritingState();
                    FocusComposeSurface();
                }, DispatcherPriority.Loaded);
        };

        // 表单即用即弃：离开可视树时摘掉挂在宿主、画布与 ViewModel 上的监听。
        Unloaded += (_, _) =>
        {
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


    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
