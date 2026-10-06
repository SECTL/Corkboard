using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Services.Config;
using Corkboard.Platforms.Abstractions;
using Corkboard.Services.Ui;

namespace Corkboard.Views;

/// <summary>
///     桌面窗口外壳。<paramref name="scope" /> 决定它承载主界面还是设置界面，
///     并决定尺寸记忆写到配置的哪一组字段上。
///     <para>
///         主窗口是「桌面挂件」形态：不要系统标题栏（拖动交给主界面自绘的顶部条），
///         并且可以按配置置底到桌面（能力请求走 <see cref="IWindowFeatureService" />，
///         视图不接触任何原生 API）。
///     </para>
///     <para>
///         两个窗口各自记一份尺寸（<c>MainWindow*</c> / <c>SettingsWindow*</c>）。
///         尺寸变化会连发多次，所以落盘走 <see cref="DispatcherTimer.RunOnce" /> 防抖；
///         关窗前再落一次最终值，防抖那一轮没到也不丢。
///     </para>
/// </summary>
public partial class MainWindow : FAAppWindow
{
    /// <summary>尺寸落盘防抖：拖动 / 最大化过程中会连发多次 Bounds 变化，攒够闲下来再写一次。</summary>
    private static readonly TimeSpan WindowSizeSaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly string _scope;
    private readonly bool _isMainScope;
    private double _lastNormalWidth = 1200;
    private double _lastNormalHeight = 800;
    private WindowState _lastNonMinimizedState = WindowState.Normal;
    private BasicSettingsConfig? _watchedBasicSettings;
    private bool _hasBeenShown;
    private bool _isWindowSizeSavePending;
    private bool _isPinnedToDesktop;
    private bool _isDesktopBottomReleased;

    /// <summary>
    ///     锁定态（点击穿透开着）时承载「开锁」按钮的独立小窗口。
    ///     它必须待在主窗口之外：点击穿透是整窗生效的，画在主窗口里的按钮自己也会点不动。
    /// </summary>
    private ClickThroughLockWindow? _lockWindow;

    /// <summary>配置里是否要求显示锁定徽标（= 点击穿透开着）。实际显隐还要看窗口在不在、是不是最小化。</summary>
    private bool _isLockBadgeRequested;

    public MainWindow() : this(AppConsts.MainWindowScope)
    {
    }

    public MainWindow(string scope)
    {
        _scope = scope;
        _isMainScope = scope != AppConsts.SettingsWindowScope;
        InitializeComponent();

        TitleBar.Height = 48;
        TitleBar.ExtendsContentIntoTitleBar = true;

        if (_isMainScope)
        {
            // 桌面挂件不画系统标题栏：最小化/关闭按钮随之消失，操作入口交给托盘与主界面顶部条。
            WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        }

        Content = scope == AppConsts.SettingsWindowScope ? new SettingsView() : new MainView();

        Loaded += OnLoaded;
        Opened += OnOpened;
        PropertyChanged += OnWindowPropertyChanged;
        Closing += OnClosing;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (IAppHost.TryGetService<MainConfigHandler>() is not { } handler)
            return;

        RestoreSize(handler.Data.Basic);
        _hasBeenShown = true;

        // 启动时就开着点击穿透的话，徽标要在窗口显示出来之后才摆得下（Load/Opened 的先后顺序
        // 不保证，两处都同步一次，谁后到谁把位置定下来）。
        SyncLockBadge();
    }

    /// <summary>
    ///     原生窗口句柄直到窗口打开才存在，所以置底只能在 <see cref="Window.Opened" /> 之后应用。
    /// </summary>
    private void OnOpened(object? sender, EventArgs e)
    {
        if (!_isMainScope)
            return;

        if (IAppHost.TryGetService<MainConfigHandler>() is { } handler)
        {
            _watchedBasicSettings = handler.Data.Basic;
            _watchedBasicSettings.PropertyChanged += OnBasicSettingsChanged;
        }

        ApplyPinToDesktop();
        ApplyClickThrough();
        ApplyWindowOpacity();

        // 锁定徽标是独立窗口，主窗口移动/缩放/最小化时它不会跟着动，得自己跟。
        PositionChanged += OnWindowPositionChanged;
        ScalingChanged += OnWindowScalingChanged;
    }

    /// <summary>设置页改开关时立即生效：主窗口与设置窗口在同一个进程，直接订阅配置变化。</summary>
    private void OnBasicSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // PropertyName 为 null 表示「所有属性都可能变了」，三条都要重放。
        if (e.PropertyName is null or nameof(BasicSettingsConfig.PinToDesktop))
            ApplyPinToDesktop();

        if (e.PropertyName is null or nameof(BasicSettingsConfig.ClickThrough))
            ApplyClickThrough();

        if (e.PropertyName is null or nameof(BasicSettingsConfig.MainWindowOpacity))
            ApplyWindowOpacity();
    }

    #region 窗口尺寸记忆

    /// <summary>
    ///     恢复上次的尺寸与最大化状态。关闭「记住窗口尺寸」时用默认尺寸，而不是记住的值：
    ///     开关因此有明确效果，配置里仍保留上一次的值，重新打开就能拿回来。
    /// </summary>
    private void RestoreSize(BasicSettingsConfig basic)
    {
        var defaultWidth = _isMainScope ? 1200d : 1000d;
        var defaultHeight = _isMainScope ? 800d : 720d;

        _lastNormalWidth = basic.AutoSaveWindowSize
            ? Normalize(GetStoredWidth(basic), MinWidth, defaultWidth)
            : defaultWidth;
        _lastNormalHeight = basic.AutoSaveWindowSize
            ? Normalize(GetStoredHeight(basic), MinHeight, defaultHeight)
            : defaultHeight;

        ApplySize(_lastNormalWidth, _lastNormalHeight);

        if (!basic.AutoSaveWindowSize || !GetStoredMaximized(basic))
            return;

        _lastNonMinimizedState = WindowState.Maximized;
        WindowState = WindowState.Maximized;
    }

    /// <summary>
    ///     跟着窗口几何变化刷新「上次普通尺寸」，并按开关决定是否防抖落盘。
    ///     最大化/全屏时 Bounds 是屏幕尺寸，不能拿去当上次普通尺寸。
    /// </summary>
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_hasBeenShown || IAppHost.TryGetService<MainConfigHandler>() is not { } handler)
            return;

        var basic = handler.Data.Basic;

        if (e.Property == BoundsProperty)
        {
            if (WindowState == WindowState.Normal)
            {
                _lastNormalWidth = Math.Max(MinWidth, Bounds.Width);
                _lastNormalHeight = Math.Max(MinHeight, Bounds.Height);
                QueueSizeSave(basic);
            }

            // 缩放会让「最右上角」跟着挪（右端按内容区宽度算），徽标要重摆。
            SyncLockBadge();

            return;
        }

        if (e.Property != WindowStateProperty)
            return;

        if (WindowState != WindowState.Minimized)
            _lastNonMinimizedState = WindowState;

        // 最小化 / 还原都会让主窗口从屏幕上挪开或挪回来，徽标得跟着收放。
        SyncLockBadge();

        // 最小化时 Windows 会把窗口的分层扩展样式（WS_EX_LAYERED / WS_EX_TRANSPARENT）摘掉，
        // 还原后它们不会自己回来。结果是：配置里仍写着「开启点击穿透 + 不透明度」、锁定徽标也照旧
        // 显示「已锁」，但主窗口其实又能被点、也恢复成完全不透明了——界面在说一件事、窗口在做另一件事。
        // 还原时把这几项能力重放一次，让原生样式回到配置描述的状态（重放是幂等的）。
        if (WindowState != WindowState.Minimized)
        {
            ApplyPinToDesktop();
            ApplyClickThrough();
            ApplyWindowOpacity();
        }

        // 最小化不算用户的尺寸选择，跳过这一轮。
        if (WindowState != WindowState.Minimized)
            QueueSizeSave(basic);
    }

    private void QueueSizeSave(BasicSettingsConfig basic)
    {
        if (!basic.AutoSaveWindowSize || _isWindowSizeSavePending)
            return;

        _isWindowSizeSavePending = true;
        DispatcherTimer.RunOnce(
            () =>
            {
                _isWindowSizeSavePending = false;
                SaveSize(basic);
            },
            WindowSizeSaveDelay);
    }

    /// <summary>
    ///     把「上次普通尺寸 + 最大化状态」写进配置。配置管道会自动落盘（<c>ConfigHandlerBase</c>），
    ///     所以这里只赋值；每项都先比对，避免一次保存把同一个值写好几遍。
    /// </summary>
    private void SaveSize(BasicSettingsConfig basic)
    {
        if (!basic.AutoSaveWindowSize || !IsVisible)
            return;

        var stateToSave = WindowState == WindowState.Minimized ? _lastNonMinimizedState : WindowState;
        if (stateToSave == WindowState.Normal)
        {
            _lastNormalWidth = Math.Max(MinWidth, Bounds.Width);
            _lastNormalHeight = Math.Max(MinHeight, Bounds.Height);
        }

        SetStoredMaximized(basic, stateToSave == WindowState.Maximized);
        SetStoredWidth(basic, _lastNormalWidth);
        SetStoredHeight(basic, _lastNormalHeight);
    }

    private void ApplySize(double width, double height)
    {
        Width = Math.Max(MinWidth, width);
        Height = Math.Max(MinHeight, height);
    }

    private static double Normalize(double value, double minimum, double fallback)
    {
        return double.IsFinite(value) && value >= minimum ? value : Math.Max(minimum, fallback);
    }

    private double GetStoredWidth(BasicSettingsConfig basic) =>
        _isMainScope ? basic.MainWindowWidth : basic.SettingsWindowWidth;

    private double GetStoredHeight(BasicSettingsConfig basic) =>
        _isMainScope ? basic.MainWindowHeight : basic.SettingsWindowHeight;

    private bool GetStoredMaximized(BasicSettingsConfig basic) =>
        _isMainScope ? basic.MainWindowMaximized : basic.SettingsWindowMaximized;

    private void SetStoredWidth(BasicSettingsConfig basic, double value)
    {
        if (_isMainScope)
        {
            if (basic.MainWindowWidth != value)
                basic.MainWindowWidth = value;
        }
        else if (basic.SettingsWindowWidth != value)
        {
            basic.SettingsWindowWidth = value;
        }
    }

    private void SetStoredHeight(BasicSettingsConfig basic, double value)
    {
        if (_isMainScope)
        {
            if (basic.MainWindowHeight != value)
                basic.MainWindowHeight = value;
        }
        else if (basic.SettingsWindowHeight != value)
        {
            basic.SettingsWindowHeight = value;
        }
    }

    private void SetStoredMaximized(BasicSettingsConfig basic, bool value)
    {
        if (_isMainScope)
        {
            if (basic.MainWindowMaximized != value)
                basic.MainWindowMaximized = value;
        }
        else if (basic.SettingsWindowMaximized != value)
        {
            basic.SettingsWindowMaximized = value;
        }
    }

    #endregion

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // 关窗前先落一次最终尺寸（防抖那一轮可能还没到）。
        if (IAppHost.TryGetService<MainConfigHandler>() is { } handler)
            SaveSize(handler.Data.Basic);

        // 独立窗口承载的表单要在主窗口之前收掉，否则它会留在屏幕上，
        // 还可能把「主窗口已关」的判断拖住（主窗口关了、弹层还开着）。
        IAppHost.TryGetService<PageOverlayService>()?.Close();

        // 锁定徽标同样是独立窗口：主窗口没了它就没人管了，必须一起收掉。
        CloseLockBadge();

        ReleaseDesktopBottom();
        ReleaseBasicSettingsWatcher();
    }

    private void ApplyPinToDesktop()
    {
        if (!_isMainScope || IAppHost.TryGetService<MainConfigHandler>() is not { } handler)
            return;

        ApplyWindowFeature(WindowFeatures.DesktopBottom, handler.Data.Basic.PinToDesktop);
    }

    /// <summary>
    ///     点击穿透：主窗口不再接收鼠标与触摸。开了之后窗口里一个控件都点不动
    ///     （标题栏动作区因此整块隐藏），只能从托盘菜单打开设置窗口、或点锁定徽标把它关掉。
    /// </summary>
    private void ApplyClickThrough()
    {
        if (!_isMainScope || IAppHost.TryGetService<MainConfigHandler>() is not { } handler)
            return;

        ApplyWindowFeature(WindowFeatures.ClickThrough, handler.Data.Basic.ClickThrough);

        // 点击穿透开着时窗口里没有可点的东西了，锁定徽标就是唯一的「解锁」入口。
        _isLockBadgeRequested = handler.Data.Basic.ClickThrough;
        SyncLockBadge();
    }

    #region 锁定徽标

    /// <summary>
    ///     显示 / 隐藏锁定徽标，并把它摆回主窗口标题栏最右上角。
    ///     <para>
    ///         徽标是独立顶层窗口，不会跟着主窗口走：位置、显隐都靠这里主动同步。
    ///         窗口还没显示完（原生句柄没就绪）或主窗口最小化时不摆——最小化后主窗口挪到了
    ///         屏幕外，徽标留在原地就会变成屏幕角落一颗孤零零的按钮。
    ///     </para>
    /// </summary>
    private void SyncLockBadge()
    {
        if (!_isMainScope)
            return;

        var shouldShow = _isLockBadgeRequested && _hasBeenShown && WindowState != WindowState.Minimized;

        if (!shouldShow)
        {
            // 只是藏起来，不销毁：解锁/锁定会来回切，重开一个窗口要重新走一遍原生创建。
            // 关窗才真的销毁（见 CloseLockBadge）。
            if (_lockWindow is { IsVisible: true } hidden)
                hidden.Hide();

            return;
        }

        if (_lockWindow is null)
        {
            _lockWindow = new ClickThroughLockWindow();
            _lockWindow.UnlockRequested += OnLockBadgeUnlockRequested;
            _lockWindow.Closed += OnLockBadgeClosed;
        }

        // 用 Show(this) 建立属主关系：主窗口最小化/关闭时徽标跟着一起收，
        // 也不会在任务栏多出一个条目（徽标自己 ShowInTaskbar=false）。
        if (!_lockWindow.IsVisible)
            _lockWindow.Show(this);

        _lockWindow.PlaceAtTitleBarCorner(this);
    }

    /// <summary>点了徽标上的「开锁」：把点击穿透关掉。只写配置，生效与显隐都由配置变化那条路走。</summary>
    private void OnLockBadgeUnlockRequested(object? sender, EventArgs e)
    {
        if (IAppHost.TryGetService<MainConfigHandler>() is not { } handler)
            return;

        handler.Data.Basic.ClickThrough = false;
    }

    private void OnLockBadgeClosed(object? sender, EventArgs e)
    {
        if (_lockWindow is { } window)
            window.UnlockRequested -= OnLockBadgeUnlockRequested;

        _lockWindow = null;
    }

    /// <summary>关窗时收掉徽标：它没有属主之外的存活理由，留下就是屏幕上洗不掉的一颗按钮。</summary>
    private void CloseLockBadge()
    {
        if (_lockWindow is not { } window)
            return;

        window.UnlockRequested -= OnLockBadgeUnlockRequested;
        window.Closed -= OnLockBadgeClosed;
        _lockWindow = null;

        // 主窗口正在关，徽标是它的子窗口，正常会跟着一起关；
        // 这里显式关一次，覆盖「徽标不是通过 Show(owner) 建出来的」那种极端情况。
        window.Close();
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e) => SyncLockBadge();

    private void OnWindowScalingChanged(object? sender, EventArgs e) => SyncLockBadge();

    #endregion

    /// <summary>
    ///     主窗口不透明度：配置里是 0–1 的标量（1 完全不透明），由平台写进原生窗口。
    ///     平台自己会夹一个下限，这里不再重复夹。
    /// </summary>
    private void ApplyWindowOpacity()
    {
        if (!_isMainScope || IAppHost.TryGetService<MainConfigHandler>() is not { } handler)
            return;

        ApplyWindowFeature(WindowFeatures.WindowOpacity, enabled: true, opacity: handler.Data.Basic.MainWindowOpacity);
    }

    /// <summary>
    ///     把窗口能力请求转给平台实现。视图只认识 <c>Corkboard.Platforms.Abstractions</c> 里的契约，
    ///     原生调用全部留在 <c>Corkboard.Platforms.&lt;OS&gt;</c>。
    /// </summary>
    private void ApplyWindowFeature(WindowFeatures features, bool enabled, double opacity = 1.0)
    {
        var logger = IAppHost.TryGetService<ILogger<MainWindow>>();
        if (IAppHost.TryGetService<IWindowFeatureService>() is not { } windowFeatures)
        {
            logger?.LogDebug("平台没有注册窗口能力服务，窗口能力 {Features} 请求被忽略。", features);
            return;
        }

        if (TryGetPlatformHandle() is not { } handle)
        {
            logger?.LogDebug("窗口句柄尚未就绪，窗口能力 {Features} 请求被忽略。", features);
            return;
        }

        var result = windowFeatures.Apply(
            new PlatformWindowHandle(handle.Handle, handle.HandleDescriptor),
            new WindowFeatureRequest(features, enabled, opacity));

        if ((features & WindowFeatures.DesktopBottom) != 0)
        {
            // 注意：Apply 在「关闭」时也会把 DesktopBottom 报成已应用（没有可解除的挂载就算成功），
            // 所以这里必须结合请求方向判断，否则「取消置底」会被记成仍然置底。
            var applied = (result.AppliedFeatures & WindowFeatures.DesktopBottom) != 0;
            _isPinnedToDesktop = enabled && applied;

            // 置底生效时，主窗口成了桌面宿主的子窗口，Windows 不再给它键盘焦点
            // （hwndFocus 恒为 0），壳内自绘的表单一个字都打不进去。
            // 把这件事告诉弹层宿主：之后的表单改由独立顶层窗口承载（见 PageOverlayWindow）。
            // 用「实际生效」而不是配置值：平台可能报不支持或降级，那种情况壳内弹层照样能输入。
            if (IAppHost.TryGetService<PageOverlayService>() is { } overlay)
                overlay.UseSeparateWindow = _isPinnedToDesktop;
        }

        if (result.FailedFeatures != WindowFeatures.None)
            logger?.LogWarning("应用窗口能力 {Features} 失败：{Detail}", result.FailedFeatures, result.Detail);
        else if (result.UnsupportedFeatures != WindowFeatures.None)
            logger?.LogInformation("当前平台不支持窗口能力 {Features}。", result.UnsupportedFeatures);
        else if (!string.IsNullOrWhiteSpace(result.Detail))
            logger?.LogWarning("窗口能力 {Features} 已降级应用：{Detail}", features, result.Detail);
    }

    /// <summary>
    ///     关窗前先解除桌面层挂载：窗口一旦销毁就再也摘不下来了，
    ///     而配置里的开关仍然是用户的选择，下次重建窗口时会重新应用。
    /// </summary>
    private void ReleaseDesktopBottom()
    {
        if (!_isPinnedToDesktop || _isDesktopBottomReleased)
            return;

        _isDesktopBottomReleased = true;
        _isPinnedToDesktop = false;

        // 触屏/改配置等路径下解除置底后，主窗口恢复成普通顶层窗口，能正常收键盘焦点，
        // 弹层就应该回到壳内自绘那条路。
        if (IAppHost.TryGetService<PageOverlayService>() is { } overlay)
            overlay.UseSeparateWindow = false;

        ApplyWindowFeature(WindowFeatures.DesktopBottom, false);
    }

    private void ReleaseBasicSettingsWatcher()
    {
        if (_watchedBasicSettings is null)
            return;

        _watchedBasicSettings.PropertyChanged -= OnBasicSettingsChanged;
        _watchedBasicSettings = null;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
