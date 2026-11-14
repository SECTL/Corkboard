using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Exam;
using Corkboard.Services.Exam;
using Corkboard.ViewModels.ExamBoard;
using Corkboard.Views.Common;

namespace Corkboard.Views.ExamBoard;

/// <summary>
///     独立的布局编辑窗：整窗宿主真实的 <see cref="ExamBoardView" />，
///     内容换成示例考试、时间换成假时钟，顶部工具条切四种状态，跟随选中区块的属性面板改布局与字号。
///     编辑期间只改 ViewModel 里的布局草稿，点「完成」才整份写回配置，点「取消」直接丢弃。
/// </summary>
public partial class ExamBoardLayoutEditorWindow : Window
{
    private readonly ExamBoardThemeService _theme;
    private readonly ResourceDictionary _themeDictionary;

    private bool _draggingPanel;
    private Point _panelDragStart;
    private double _panelStartLeft;
    private double _panelStartTop;
    private bool _fontPointerDown;

    public ExamBoardLayoutEditorWindow()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamBoardLayoutEditorViewModel>();

        _theme = IAppHost.GetService<ExamBoardThemeService>();
        _themeDictionary = ExamBoardThemeResources.Create();
        Resources.MergedDictionaries.Add(_themeDictionary);
        ExamBoardThemeResources.Apply(_themeDictionary, _theme.Brushes);
        _theme.Changed += OnThemeChanged;

        // 缓存的拖动滑块期间不提交，松手才写配置；键盘改（没有指针按下）才即时提交。
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(RangeBase.ValueChangedEvent, OnFontSliderValueChanged, RoutingStrategies.Bubble, handledEventsToo: true);

        SizeChanged += (_, _) => PositionInspector();

        // 指针在窗口外松开时收不到 PointerReleased，窗口失活就把还在拖的字号补提交掉。
        Deactivated += (_, _) =>
        {
            if (_fontPointerDown)
            {
                _fontPointerDown = false;
                ViewModel?.CommitFontDrafts();
            }
        };

        Opened += (_, _) =>
        {
            if (ViewModel is not { } viewModel)
            {
                return;
            }

            viewModel.CloseRequested += OnCloseRequested;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.Attach();
            PositionInspector();
        };
        Closed += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.CloseRequested -= OnCloseRequested;
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                viewModel.CommitFontDrafts();
                viewModel.Detach();
            }

            _theme.Changed -= OnThemeChanged;
            Resources.MergedDictionaries.Remove(_themeDictionary);
        };
    }

    /// <summary>带初始状态打开：编辑窗进来先停在该状态。</summary>
    public ExamBoardLayoutEditorWindow(ExamStatus status) : this()
    {
        ViewModel?.SelectStatus(status);
    }

    private ExamBoardLayoutEditorViewModel? ViewModel => DataContext as ExamBoardLayoutEditorViewModel;

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnThemeChanged(object? sender, EventArgs e) =>
        ExamBoardThemeResources.Apply(_themeDictionary, _theme.Brushes);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExamBoardLayoutEditorViewModel.HasSelection)
            or nameof(ExamBoardLayoutEditorViewModel.SelectedComponent)
            or nameof(ExamBoardLayoutEditorViewModel.SelectedStatus))
        {
            // 等新选中项布局完再量区块位置，否则拿到的是上一次的 Bounds。
            Dispatcher.UIThread.Post(PositionInspector, DispatcherPriority.Loaded);
        }
    }

    /// <summary>把属性面板摆到被选中区块的右侧（放不下就翻到左侧），并夹在窗口内。</summary>
    private void PositionInspector()
    {
        if (ViewModel is not { HasSelection: true } viewModel)
        {
            return;
        }

        Inspector.MaxHeight = Math.Max(240, Bounds.Height - 96);
        Inspector.Measure(new Size(Inspector.Width, Inspector.MaxHeight));

        var panelWidth = Inspector.Width;
        var panelHeight = Math.Min(Inspector.DesiredSize.Height, Inspector.MaxHeight);

        double left;
        double top;

        var component = viewModel.SelectedComponent.Value;
        if (BoardView.TryGetComponentBounds(component, out var bounds)
            && BoardView.TranslatePoint(bounds.TopLeft, this) is { } origin)
        {
            var rect = new Rect(origin, bounds.Size);
            left = rect.Right + 14;
            if (left + panelWidth > Bounds.Width - 14)
            {
                left = rect.Left - panelWidth - 14;
            }

            top = rect.Top;
        }
        else
        {
            // 被「看板显示」设置隐藏、或还没量出来的区块：先摆到右边，用户可以自己拖。
            left = Bounds.Width - panelWidth - 16;
            top = 120;
        }

        left = Math.Clamp(left, 14, Math.Max(14, Bounds.Width - panelWidth - 14));
        top = Math.Clamp(top, 80, Math.Max(80, Bounds.Height - panelHeight - 14));
        SetInspectorPosition(left, top);
    }

    private void SetInspectorPosition(double left, double top)
    {
        var panelWidth = Inspector.Width;
        var panelHeight = Math.Min(Inspector.DesiredSize.Height, Inspector.MaxHeight);
        Canvas.SetLeft(Inspector, Math.Clamp(left, 14, Math.Max(14, Bounds.Width - panelWidth - 14)));
        Canvas.SetTop(Inspector, Math.Clamp(top, 80, Math.Max(80, Bounds.Height - panelHeight - 14)));
    }

    private void OnInspectorDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _draggingPanel = true;
        _panelDragStart = e.GetPosition(this);
        _panelStartLeft = Canvas.GetLeft(Inspector);
        _panelStartTop = Canvas.GetTop(Inspector);
        if (double.IsNaN(_panelStartLeft))
        {
            _panelStartLeft = 0;
        }

        if (double.IsNaN(_panelStartTop))
        {
            _panelStartTop = 0;
        }

        e.Pointer.Capture(InspectorDragHandle);
        e.Handled = true;
    }

    private void OnInspectorDragMoved(object? sender, PointerEventArgs e)
    {
        if (!_draggingPanel)
        {
            return;
        }

        var current = e.GetPosition(this);
        SetInspectorPosition(_panelStartLeft + current.X - _panelDragStart.X,
            _panelStartTop + current.Y - _panelDragStart.Y);
        e.Handled = true;
    }

    private void OnInspectorDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_draggingPanel)
        {
            return;
        }

        _draggingPanel = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnInspectorDragCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        _draggingPanel = false;

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _fontPointerDown = IsWithinSlider(e.Source as Visual);

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_fontPointerDown)
        {
            return;
        }

        _fontPointerDown = false;
        ViewModel?.CommitFontDrafts();
    }

    private void OnFontSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        // 指针拖动期间只更新草稿；松手后（OnWindowPointerReleased）统一提交。
        if (!_fontPointerDown)
        {
            ViewModel?.CommitFontDrafts();
        }
    }

    private static bool IsWithinSlider(Visual? source)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Slider)
            {
                return true;
            }
        }

        return false;
    }
}