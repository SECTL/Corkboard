using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Controls;
using Corkboard.Core.Icons;
using Corkboard.Services.Ui;
using Corkboard.ViewModels.MainPages;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.MainPages;

/// <summary>
///     作业板主页面。<see cref="PageInfo" /> 的值同时是导航项 Id 与键控 DI 的 key，
///     改 Id 必须同步改 <c>AppConsts.DefaultMainPageId</c>。
///     <para>
///         「布置作业」的入口与时空回放的控制条都在主界面标题栏（<c>MainView</c>）；
///         页面这里管显示、编辑、删除，以及科目区块的拖动排序。
///     </para>
/// </summary>
[PageInfo("main.board", FluentIcons.BoardFilled)]
public partial class BoardPage : UserControl
{
    /// <summary>拖动阈值（DIP）：按下后挪动超过它才算「要换位置」，免得点一下标题就改排序。</summary>
    private const double ReorderTolerance = 4;

    /// <summary>
    ///     单列排布的宽度上限（DIP）：约 40 个中文字一行，再宽就得左右转头读了。
    ///     超出的宽度由 <c>HorizontalAlignment="Center"</c> 留在两边，列本身不动。
    /// </summary>
    private const double SingleColumnMaxWidth = 720;

    /// <summary>落位动画时长：短到不拖手、长到看得出「谁给谁让了位」。</summary>
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(160);

    /// <summary>落位动画的帧间隔（约 60fps）。</summary>
    private static readonly TimeSpan SlideFrameInterval = TimeSpan.FromMilliseconds(16);

    private readonly PageOverlayService _overlay;

    /// <summary>单列排布的那份列表：宽度是代码按可用宽度算出来的（见 <see cref="SingleColumnMaxWidth" />）。</summary>
    private readonly ItemsControl? _singleColumnList;

    /// <summary>正在滑回原位的区块（见 <see cref="SlideFrame" />）。</summary>
    private readonly List<SlideEntry> _slides = [];

    private DispatcherTimer? _slideTimer;

    /// <summary>被按住的那一块（落盘要用它的 Key），松手后置空。</summary>
    private BoardSubjectItem? _reorderItem;

    /// <summary>被按住那一块的外框，用来加 .dragging 样式。</summary>
    private Border? _reorderNote;

    /// <summary>被按住那一块所在的面板，算目标位置用；容器会被重建，面板不会。</summary>
    private Panel? _reorderPanel;

    /// <summary>这次拖动的指针，收尾时靠它显式交还捕获。</summary>
    private IPointer? _reorderPointer;

    private Point _pressOrigin;
    private bool _isReordering;

    public BoardPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<BoardPageViewModel>();
        _overlay = IAppHost.GetService<PageOverlayService>();
        _singleColumnList = this.FindControl<ItemsControl>("SingleColumnList");

        // 页面是 transient 的，离开可视树时必须断开对单例服务和配置的订阅；
        // 拖动中留下的指针捕获、落位动画的计时器也要在这里收掉（见 EndReorder / StopSlides）。
        Unloaded += (_, _) =>
        {
            EndReorder(commit: false);
            StopSlides();
            ViewModel?.Detach();
        };
    }

    private BoardPageViewModel? ViewModel => DataContext as BoardPageViewModel;

    /// <summary>
    ///     编辑入口：复用「布置作业」那份表单，打开时把这条作业填进去，确定即写回。
    ///     <para>弹层挂在壳那一层，所以这里只管把控件交给 <see cref="PageOverlayService" />。</para>
    /// </summary>
    private void OnEditNoteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: BoardNoteItem item })
            return;

        // 回放期间不给改历史快照（按钮本身已隐藏，这里是兜底）。
        if (ViewModel is { IsReplaying: true })
            return;

        _overlay.Show(BoardAssignmentForm.ForEdit(item.Note));
    }

    /// <summary>
    ///     删除入口。确认对话框是视图层的事，所以「删除前确认」开关在这里读，
    ///     开关关掉时直接删，不打扰用户。
    /// </summary>
    private async void OnDeleteNoteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: BoardNoteItem item } || ViewModel is not { } viewModel)
            return;

        if (viewModel.ConfirmBeforeDelete && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            var dialog = new FAContentDialog
            {
                Title = CR.Board_DeleteConfirmTitle,
                Content = CR.Board_DeleteConfirmMessage,
                PrimaryButtonText = CR.Board_DeleteNote,
                CloseButtonText = CR.Common_Cancel
            };

            if (await dialog.ShowAsync(topLevel) != FAContentDialogResult.Primary)
                return;
        }

        viewModel.DeleteNoteCommand.Execute(item);
    }

    #region 排布

    /// <summary>
    ///     内容区宽度变了：重算单列的限宽。
    ///     <para>
    ///         这里给的是<b>显式宽度</b>而不是 MaxWidth：只限上限时 ItemsControl 会缩到内容宽度，
    ///         内容窄的科目整列就往中间挤、内容宽的又铺满，宽度来回跳；写死成
    ///         「可用宽度与上限取小」之后，它的宽度只跟窗口走。
    ///     </para>
    ///     <para>
    ///         赋宽度这件事本身会再触发一次 <c>SizeChanged</c>（宽度变了），但第二次算出来的值
    ///     与当前宽度相同，不会再变，所以不会来回抖。
    ///     </para>
    /// </summary>
    private void OnBoardContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_singleColumnList is not null)
            _singleColumnList.Width = Math.Min(e.NewSize.Width, SingleColumnMaxWidth);
    }

    #endregion

    #region 科目区块拖动排序

    /// <summary>
    ///     区块标题行按下：立刻接管指针，挪动超过阈值就开始换位置。
    ///     <para>
    ///         刻意**不等长按**：手柄本身就是「抓这里拖」的意思，而且 500ms 长按正好和壳的
    ///     整窗长按拖动撞车（两边判定条件一样，同一次按住会被判成两件事）。
    ///     标题行整条都标了 <see cref="WindowDragGesture.SuppressProperty" />，壳不会来抢。
    ///     </para>
    ///     <para>
    ///         指针捕获挂**页面**上，不挂手柄：区块一换位置，ItemsControl 就可能把容器连同
    ///     手柄一起重建，捕获挂在那上面等于挂在一个离开可视树的元素上——之后收不到
    ///     <c>PointerReleased</c>，捕获留在页面上，整个界面就点不动了。
    ///     </para>
    /// </summary>
    private void OnReorderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isReordering || _reorderItem is not null)
            return;

        if (sender is not Control header || header.DataContext is not BoardSubjectItem item)
            return;

        if (ViewModel is not { IsReplaying: false } || !item.CanReorder)
            return;

        if (!e.GetCurrentPoint(header).Properties.IsLeftButtonPressed && e.Pointer.Type != PointerType.Touch)
            return;

        _reorderItem = item;
        _reorderNote = FindNoteBorder(header);
        _reorderPanel = FindItemsPanel();
        _reorderPointer = e.Pointer;
        _pressOrigin = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    /// <summary>
    ///     拖动中：先过阈值判定，再把区块挪到指针底下那一格。
    ///     <para>
    ///         捕获也可能会被别的窗口抢走（原生拖动、别的弹窗），那种情况下指针事件会停，
    ///     所以顺手查一次左键还在不在——不在就当场收尾，别把拖动状态留在界面上。
    ///     </para>
    /// </summary>
    private void OnReorderMoved(object? sender, PointerEventArgs e)
    {
        if (_reorderItem is null)
            return;

        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndReorder(commit: _isReordering);
            return;
        }

        if (!_isReordering)
        {
            var delta = e.GetPosition(this) - _pressOrigin;
            if (Math.Abs(delta.X) <= ReorderTolerance && Math.Abs(delta.Y) <= ReorderTolerance)
                return;

            _isReordering = true;
            _reorderNote?.Classes.Add("dragging");
        }

        MoveToPointer(e);
    }

    /// <summary>松手：拖动真的发生过就把顺序落盘，只是点了一下就什么都不做。</summary>
    private void OnReorderReleased(object? sender, PointerReleasedEventArgs e) => EndReorder(_isReordering);

    /// <summary>
    ///     捕获被别处抢走（例如另一个控件调用 <c>Capture</c>）：
    ///     这时收不到 <c>PointerReleased</c>，得自己收尾。
    /// </summary>
    private void OnReorderCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndReorder(_isReordering);

    /// <summary>把被按住的那一块挪到指针底下那一格。</summary>
    private void MoveToPointer(PointerEventArgs e)
    {
        if (_reorderItem is null || ViewModel is not { } viewModel)
            return;

        var currentIndex = viewModel.SubjectGroups.IndexOf(_reorderItem);
        if (currentIndex < 0)
            return;

        var targetIndex = ResolveTargetIndex(e, currentIndex);
        if (targetIndex >= 0)
            MoveWithAnimation(currentIndex, targetIndex);
    }

    /// <summary>
    ///     挪动区块，并让被让位的区块滑过去。
    ///     <para>
    ///         FLIP：先把每块的旧位置量下来，挪完等这次布局跑完，再把「旧位置 − 新位置」当成一个位移
    ///     补到容器身上、动画回 0——面板（StackPanel / WrapPanel）本身不认动画，位置一变就是硬切，
    ///     所以要让「谁给谁让了位」看得出来，只能在容器上补这一下。
    ///     </para>
    ///     <para>关掉动效设置（个性化 → 外观）时只挪位置，不做动画。</para>
    /// </summary>
    private void MoveWithAnimation(int fromIndex, int toIndex)
    {
        if (ViewModel is not { } viewModel)
            return;

        if (_reorderPanel is null || !viewModel.Config.Appearance.EnableAnimations)
        {
            viewModel.MoveSubjectGroup(fromIndex, toIndex);
            return;
        }

        var before = CapturePositions();
        viewModel.MoveSubjectGroup(fromIndex, toIndex);

        // 新位置要等这次布局跑完才有；布局跑在 Layout 优先级，所以排到它后面的 Background 里量。
        Dispatcher.UIThread.Post(() => SlideIntoPlace(before), DispatcherPriority.Background);
    }

    /// <summary>按当前顺序记下每个区块容器在面板里的位置。</summary>
    private Dictionary<string, Rect> CapturePositions()
    {
        var positions = new Dictionary<string, Rect>();
        if (_reorderPanel is not { } panel || ViewModel is not { } viewModel)
            return positions;

        var count = Math.Min(panel.Children.Count, viewModel.SubjectGroups.Count);
        for (var i = 0; i < count; i++)
            positions[viewModel.SubjectGroups[i].Key] = panel.Children[i].Bounds;

        return positions;
    }

    /// <summary>
    ///     把旧位置与新位置的差值补成一次位移动画。
    ///     <para>
    ///         动效自己按帧算，不用 Avalonia 的 Animation：12.1.1 的 <c>Animation.RunAsync</c> 只吃
    ///     <see cref="Visual" />（传 <see cref="TranslateTransform" /> 会 InvalidCastException），
    ///     而 Transform 上的 <c>Transitions</c> 又得挂进可视树才跑得起来——这里要的只是「每帧挪一点」，
    ///     一个 16ms 的计时器最省事也最可控。
    ///     </para>
    /// </summary>
    private void SlideIntoPlace(Dictionary<string, Rect> before)
    {
        if (_reorderPanel is not { } panel || ViewModel is not { } viewModel)
            return;

        var now = Environment.TickCount64;
        var count = Math.Min(panel.Children.Count, viewModel.SubjectGroups.Count);
        for (var i = 0; i < count; i++)
        {
            if (!before.TryGetValue(viewModel.SubjectGroups[i].Key, out var oldBounds))
                continue;

            var container = panel.Children[i];
            var delta = oldBounds.Position - container.Bounds.Position;
            if (Math.Abs(delta.X) < 0.5 && Math.Abs(delta.Y) < 0.5)
                continue;

            // 同一块还在滑就先撤掉上一段，免得两个计时条目抢同一个容器。
            _slides.RemoveAll(slide => ReferenceEquals(slide.Container, container));

            var transform = new TranslateTransform(delta.X, delta.Y);
            container.RenderTransform = transform;
            _slides.Add(new SlideEntry(container, transform, delta, now));
        }

        if (_slides.Count == 0)
            return;

        _slideTimer ??= new DispatcherTimer { Interval = SlideFrameInterval };
        _slideTimer.Tick -= OnSlideFrame;
        _slideTimer.Tick += OnSlideFrame;
        _slideTimer.Start();
    }

    /// <summary>落位动画的每一帧：按缓出曲线把补出来的位移收回到 0。</summary>
    private void OnSlideFrame(object? sender, EventArgs e)
    {
        var now = Environment.TickCount64;
        for (var i = _slides.Count - 1; i >= 0; i--)
        {
            var slide = _slides[i];
            var progress = Math.Clamp((now - slide.StartedAt) / SlideDuration.TotalMilliseconds, 0d, 1d);
            var remaining = 1d - CubicEaseOut(progress);

            slide.Transform.X = slide.Offset.X * remaining;
            slide.Transform.Y = slide.Offset.Y * remaining;

            if (progress < 1d)
                continue;

            if (ReferenceEquals(slide.Container.RenderTransform, slide.Transform))
                slide.Container.RenderTransform = null;

            _slides.RemoveAt(i);
        }

        if (_slides.Count == 0)
            StopSlides();
    }

    /// <summary>缓出曲线（1-(1-t)³）：起步快、收尾稳，看着像被放下的。</summary>
    private static double CubicEaseOut(double progress) => 1d - Math.Pow(1d - progress, 3d);

    /// <summary>停掉落位动画并把还没滑完的位移清干净（页面离开可视树时也走这里）。</summary>
    private void StopSlides()
    {
        if (_slideTimer is not null)
        {
            _slideTimer.Stop();
            _slideTimer.Tick -= OnSlideFrame;
            _slideTimer = null;
        }

        foreach (var slide in _slides)
        {
            if (ReferenceEquals(slide.Container.RenderTransform, slide.Transform))
                slide.Container.RenderTransform = null;
        }

        _slides.Clear();
    }

    /// <summary>一条正在滑的区块：容器、挂在它身上的位移、要收掉的位移量、起始时刻。</summary>
    private sealed record SlideEntry(Control Container, TranslateTransform Transform, Vector Offset, long StartedAt);

    /// <summary>
    ///     根据指针落在哪个区块上算出目标下标。
    ///     <para>
    ///         容器每帧现取：区块一移动，ItemsControl 可能把容器重建，缓存下来的那份会失效
    ///     （表现为拖到一半就不跟手）。
    ///     </para>
    ///     <para>
    ///         先看指针压在哪个区块里；落在区块之间的空隙时退化成「离谁的中心近算谁」，
    ///     这样竖排（单列）与横排（区块）两种面板都用同一套判断。
    ///     </para>
    /// </summary>
    private int ResolveTargetIndex(PointerEventArgs e, int currentIndex)
    {
        if (_reorderPanel is not { } panel || panel.Children.Count == 0 || currentIndex >= panel.Children.Count)
            return -1;

        var point = e.GetPosition(panel);

        for (var i = 0; i < panel.Children.Count; i++)
        {
            if (panel.Children[i].Bounds.Contains(point))
                return i;
        }

        var nearest = currentIndex;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < panel.Children.Count; i++)
        {
            var bounds = panel.Children[i].Bounds;
            var dx = point.X - bounds.Center.X;
            var dy = point.Y - bounds.Center.Y;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = i;
            }
        }

        return nearest;
    }

    /// <summary>
    ///     收尾：撤掉拖动样式、还回指针捕获；<paramref name="commit" /> 为真时把新顺序落盘。
    ///     <para>
    ///         <b>捕获必须显式还回去</b>：指针在窗口外抬起、或手柄被区块重建带走时，
    ///     <c>PointerReleased</c> 都可能收不到，捕获留在页面上会让整窗控件都点不动。
    ///     </para>
    /// </summary>
    private void EndReorder(bool commit)
    {
        if (_reorderItem is null && _reorderPointer is null && !_isReordering)
            return;

        var moved = _isReordering;
        _isReordering = false;
        _reorderItem = null;
        _reorderPanel = null;
        _reorderNote?.Classes.Remove("dragging");
        _reorderNote = null;

        _reorderPointer?.Capture(null);
        _reorderPointer = null;

        if (commit && moved)
            ViewModel?.CommitSubjectOrder();
    }

    /// <summary>
    ///     当前排布用的那块面板：两种排布各自的 ItemsControl 只有一个可见，
    ///     <c>ItemsPanelRoot</c> 就是装区块容器的面板。每次拖动现解析一次，面板本身不会被重建。
    /// </summary>
    private Panel? FindItemsPanel()
    {
        foreach (var items in this.GetVisualDescendants().OfType<ItemsControl>())
        {
            if (!items.IsEffectivelyVisible)
                continue;

            if (items.Classes.Contains("layout-single")
                || items.Classes.Contains("layout-block"))
            {
                return items.ItemsPanelRoot;
            }
        }

        return null;
    }

    /// <summary>从标题行往上找所在区块的外框（模板根 <c>Border.note</c>），拖动时给它加样式。</summary>
    private static Border? FindNoteBorder(Control header)
    {
        foreach (var ancestor in header.GetVisualAncestors())
        {
            if (ancestor is Border border && border.Classes.Contains("note"))
                return border;
        }

        return null;
    }

    #endregion

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
