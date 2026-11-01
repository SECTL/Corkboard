using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Corkboard.Core.Board;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Services.Desktop;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.ExamBoard;

/// <summary>
///     考试看板最小化之后留在屏幕上的那颗**圆角矩形**浮窗按钮：左边软件图标、右边一行字。
///     <para>
///         交互与用户约定的完全一致：**单击回到看板、按住 2 秒退出看板**。两种操作放在同一颗按钮上，
///         是因为屏幕上这时只有它——只给「恢复」用户就退不出去，只给「退出」用户就回不来。
///         进度用一条细的 <see cref="ProgressBar" /> 显示，让「按住 2 秒」这件事看得见。
///     </para>
///     <para>
///         文字、缩放（60%–200%）、位置（九宫格）都来自设置 → 考试看板 → 最小化与浮窗，
///         尺寸与夹取读 <see cref="ExamBoardFloatingStyle" />，别在这里另写一组数字。
///     </para>
///     <para>
///         ⚠️ 定位一律走 <see cref="FloatingWindowLayer.Place" />：窗口置底形态下
///         <see cref="Window.Position" /> 是宿主的客户区坐标，直接写屏幕坐标会偏。
///         另外窗口是 <c>SizeToContent</c>，尺寸要等排版完（<c>SizeChanged</c>）才准，所以每次变化都重摆一次。
///     </para>
/// </summary>
public partial class ExamBoardFloatingWindow : Window
{
    /// <summary>长按多久算退出：与看板上那颗角标同一个承诺（<see cref="ExamBoardFloatingStyle.HoldSeconds" />）。</summary>
    private static readonly TimeSpan HoldToExitDuration =
        TimeSpan.FromSeconds(ExamBoardFloatingStyle.HoldSeconds);

    /// <summary>进度刷新的间隔：约 60 fps，两秒一百多帧，顺滑又不费。</summary>
    private static readonly TimeSpan HoldFrameInterval = TimeSpan.FromMilliseconds(16);

    private readonly DispatcherTimer _holdTimer;
    private readonly Stopwatch _holdWatch = new();

    /// <summary>软件自己那张图标（与窗口 / 托盘同一份母图，<see cref="AppIcon.LoadBitmap" /> 内部有缓存）。</summary>
    private readonly Bitmap? _defaultIcon = AppIcon.LoadBitmap();

    /// <summary>用户自选的那张图标；换图 / 读失败 / 关窗时释放（默认那张归 AppIcon 缓存管，不许释放）。</summary>
    private Bitmap? _customIcon;
    private string _customIconPath = string.Empty;

    private DockAnchor _anchor = DockAnchor.BottomCenter;
    private bool _isHolding;

    /// <summary>长按已经走到「退出」这一档：松手时那颗按钮还会再报一次 Click，得把它吃掉。</summary>
    private bool _exited;

    /// <summary>正在用的那份配置（挂在窗口自己的订阅上，见 <see cref="Attach" />）。</summary>
    private ExamBoardSettingsConfig? _settings;

    public ExamBoardFloatingWindow()
    {
        InitializeComponent();

        AppIconImage.Source = _defaultIcon;

        // ⚠️ 这三个处理器**必须**自己挂、而且带 handledEventsToo：
        // Button 在它的类处理器里就把 PointerPressed / PointerReleased 标成 Handled 了（还顺手 Capture），
        // 挂在 XAML 上的实例处理器一个都收不到——表现出来就是「点这颗按钮毫无反应、长按也不退出」。
        // 单击交给 Button 自己的 Click（最稳），长按靠下面这两条拿到的按下/松开时刻。
        RestoreButton.AddHandler(PointerPressedEvent, OnButtonPointerPressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        RestoreButton.AddHandler(PointerReleasedEvent, OnButtonPointerReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
        RestoreButton.AddHandler(PointerCaptureLostEvent, OnButtonCaptureLost,
            RoutingStrategies.Bubble, handledEventsToo: true);

        // 圆角矩形的四个角在无边框窗口里必须靠透明底托着，否则四角会露出黑色方块。
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _holdTimer = new DispatcherTimer { Interval = HoldFrameInterval };
        _holdTimer.Tick += (_, _) => OnHoldTick();

        // SizeToContent：尺寸是排版之后才定下来的，每次变化都要重摆一次。
        SizeChanged += (_, _) => Reposition();
        Opened += (_, _) =>
        {
            FloatingWindowLayer.Register(this);
            Reposition();
        };
        Closed += (_, _) => DisposeCustomIcon();
    }

    /// <summary>用户点了这颗按钮（没按满 2 秒）：看板窗口收到就把自己显示回来。</summary>
    public event EventHandler? RestoreRequested;

    /// <summary>按住满 2 秒：看板窗口收到就真的关掉看板。</summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    ///     按设置刷新外观：文字（空则用默认文案）、文字颜色、图标（显示 / 自定义 / 无字模式）、
    ///     缩放、圆角、位置。最小化期间设置页里改了这几项，浮窗自己订阅了配置、会再调一次这里。
    ///     按钮底色不在这里——走 XAML 上的 <c>Classes="accent"</c>，跟随主题强调色。
    /// </summary>
    public void Apply(ExamBoardSettingsConfig settings)
    {
        var percent = ExamBoardFloatingStyle.ClampScalePercent(settings.FloatingScalePercent);
        var showIcon = settings.ShowFloatingIcon;

        // 图标关掉时「无字模式」就无从谈起（那样按钮上一个字一个图都没有），这里再兜一层。
        var iconOnly = showIcon && settings.FloatingIconOnly;

        var caption = ExamBoardFloatingStyle.NormalizeText(settings.FloatingButtonText);
        if (caption.Length == 0)
            caption = CR.Settings_Exam_Floating_DefaultText;

        CaptionText.Text = caption;
        CaptionText.FontSize = ExamBoardFloatingStyle.ResolveFontSize(percent);
        CaptionText.Foreground = new SolidColorBrush(settings.FloatingTextColor);
        CaptionText.IsVisible = !iconOnly;

        // 「按住 2 秒退出」没有任何别的地方说得出来：这颗按钮上那行小字就是唯一的提示，
        // 所以它跟标题一起常驻（无字模式时一起藏）。颜色跟着标题走，淡一点。
        HintText.Text = CR.Exam_Board_FloatingHoldHint;
        HintText.FontSize = ExamBoardFloatingStyle.ResolveHintFontSize(percent);
        HintText.Foreground = new SolidColorBrush(settings.FloatingTextColor);
        HintText.IsVisible = !iconOnly;

        AppIconImage.IsVisible = showIcon;

        // 文字那列藏掉之后 ColumnSpacing 还占着 8，会白留一道缝。
        IconCaptionGrid.ColumnSpacing = iconOnly ? 0 : 8;

        RestoreButton.CornerRadius = new CornerRadius(ExamBoardFloatingStyle.ResolveCornerRadius(percent));

        if (showIcon)
        {
            var iconSize = iconOnly
                ? ExamBoardFloatingStyle.ResolveIconOnlyIconSize(percent)
                : ExamBoardFloatingStyle.ResolveIconSize(percent);

            AppIconImage.Width = iconSize;
            AppIconImage.Height = iconSize;
            AppIconImage.Source = ResolveIcon(settings.FloatingIconPath);
        }

        RestoreButton.Padding = iconOnly
            ? new Thickness(ExamBoardFloatingStyle.ResolveIconOnlyPadding(percent))
            : new Thickness(
                ExamBoardFloatingStyle.ResolvePaddingHorizontal(percent),
                ExamBoardFloatingStyle.ResolvePaddingVertical(percent));

        ToolTip.SetTip(RestoreButton, CR.Exam_Board_FloatingToolTip);

        _anchor = DockAnchors.Normalize(settings.FloatingAnchor);
        Reposition();
    }

    /// <summary>
    ///     取图标位图：路径为空就用软件自己那张；给了路径就现读。
    ///     读不出来（文件被删、挑了个不是图片的东西）退回默认图标——浮窗上不能整块空掉，
    ///     但**不清掉用户选的那条路径**（把文件补回来就又能用）。
    /// </summary>
    private Bitmap? ResolveIcon(string path)
    {
        var normalized = path?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            DisposeCustomIcon();
            return _defaultIcon;
        }

        if (_customIcon is not null && string.Equals(_customIconPath, normalized, StringComparison.OrdinalIgnoreCase))
            return _customIcon;

        try
        {
            var loaded = new Bitmap(normalized);
            DisposeCustomIcon();
            _customIcon = loaded;
            _customIconPath = normalized;
            return _customIcon;
        }
        catch (Exception)
        {
            DisposeCustomIcon();
            return _defaultIcon;
        }
    }

    private void DisposeCustomIcon()
    {
        _customIcon?.Dispose();
        _customIcon = null;
        _customIconPath = string.Empty;
    }

    /// <summary>按九宫格锚点把按钮摆进当前屏幕的工作区（避开任务栏）。</summary>
    public void Reposition()
    {
        if (!IsVisible)
            return;

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var workArea = screen.WorkingArea;
        var widthPixels = Math.Max(1, (int)Math.Round(Bounds.Width * scaling));
        var heightPixels = Math.Max(1, (int)Math.Round(Bounds.Height * scaling));
        var inset = (int)Math.Round(ExamBoardFloatingStyle.EdgeInset * scaling);

        var column = DockAnchors.Column(_anchor);
        var row = DockAnchors.Row(_anchor);

        var x = column switch
        {
            0 => workArea.X + inset,
            1 => workArea.X + (workArea.Width - widthPixels) / 2,
            _ => workArea.Right - widthPixels - inset
        };

        var y = row switch
        {
            0 => workArea.Y + inset,
            1 => workArea.Y + (workArea.Height - heightPixels) / 2,
            _ => workArea.Bottom - heightPixels - inset
        };

        // 窗口比工作区还大、或者屏幕原点为负时也别画到屏幕外面去。
        x = Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.Right - widthPixels));
        y = Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - heightPixels));

        FloatingWindowLayer.Place(this, new PixelPoint(x, y));
    }

    /// <summary>
    ///     接上配置：把当前设置刷一遍外观，并在最小化期间跟着设置页的改动实时重刷
    ///     （文字、颜色、图标、缩放、位置都是用户随手会改的）。
    ///     <para>
    ///         订阅挂在浮窗自己身上，而不是「看板窗口收到配置变化再转给我」——看板这时是藏起来的，
    ///         多绕一层就多一个会断的地方。
    ///     </para>
    /// </summary>
    public void Attach(ExamBoardSettingsConfig settings)
    {
        if (!ReferenceEquals(_settings, settings))
        {
            Detach();
            _settings = settings;
            _settings.PropertyChanged += OnSettingsChanged;
        }

        Apply(settings);
    }

    /// <summary>断开配置订阅（浮窗关掉时调）。</summary>
    public void Detach()
    {
        if (_settings is null)
            return;

        _settings.PropertyChanged -= OnSettingsChanged;
        _settings = null;
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_settings is { } settings)
            Apply(settings);
    }

    /// <summary>单击这颗按钮：回到看板。长按走满 2 秒时已经被吃掉，不会在这里再恢复一次。</summary>
    private void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        if (_exited)
        {
            _exited = false;
            return;
        }

        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>按下：开始计时，进度条从零长起。松手时看走没走满决定「恢复」还是「退出」。</summary>
    private void OnButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control host)
            return;

        // 只有主键 / 触摸算数：右键、中键在这颗按钮上什么都不做。
        if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed && e.Pointer.Type != PointerType.Touch)
            return;

        // 已经按着了就不再重开计时（一次按下可能收到多条按下事件）。
        if (_isHolding)
            return;

        // 捕获不在这里抢：Button 自己已经 Capture 到它身上了，再抢一次只会把它那套点击判定搅乱。
        _exited = false;

        _holdTimer.Stop();
        _holdWatch.Restart();
        _isHolding = true;
        HoldProgress.Value = 0;
        HoldProgress.IsVisible = true;
        _holdTimer.Start();
    }

    /// <summary>松手：长按计时作废（单击由 Button 的 Click 接手，别在这儿重复发一次恢复）。</summary>
    private void OnButtonPointerReleased(object? sender, PointerReleasedEventArgs e) => CancelHold();

    /// <summary>指针被别处抢走（窗口失焦）：这次长按作废，什么都不发生。</summary>
    private void OnButtonCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CancelHold();

    private void CancelHold()
    {
        _holdTimer.Stop();
        _holdWatch.Reset();

        _isHolding = false;
        HoldProgress.IsVisible = false;
        HoldProgress.Value = 0;
    }

    /// <summary>每帧推进进度条；走满 <see cref="HoldToExitDuration" /> 就发退出请求。</summary>
    private void OnHoldTick()
    {
        var progress = Math.Clamp(_holdWatch.Elapsed / HoldToExitDuration, 0d, 1d);
        HoldProgress.Value = progress;

        if (progress < 1)
            return;

        // 走满了：先记账再发退出。松手时 Button 还会再报一次 Click，靠 _exited 把那次吃掉，
        // 否则「长按退出」会紧跟着「单击恢复」，屏幕上看板又跳回来了。
        _exited = true;
        CancelHold();
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
