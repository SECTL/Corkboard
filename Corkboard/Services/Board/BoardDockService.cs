using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Board;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Desktop;
using Corkboard.Views;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Board;

/// <summary>
///     「收纳作业板」的状态宿主：主窗口收起来之后只留一颗浮在屏幕上的展开按钮，点一下再展开。
///     <para>
///         按钮的位置、偏移与大小来自「设置 → 作业板 → 收纳」（<see cref="BoardSettingsConfig.DockAnchor" /> 等），
///         这个服务负责把主窗口藏起来、把按钮窗摆到对应位置，并在两边切换时维护状态。
///     </para>
///     <para>
///         它是单例：浮钮窗只允许一份（<see cref="Collapse" /> 反复调用也不会多开），
///         主窗口引用在第一次收起时从调用方或桌面生命周期里取。
///     </para>
/// </summary>
public partial class BoardDockService : ObservableObject, IDisposable
{
    /// <summary>按钮基准直径（DIP）：配置里「按钮大小」100% 就是这么大（见 BoardDockWindow 的初始尺寸）。</summary>
    private const double BaseButtonDiameter = 56;

    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<BoardDockService> _logger;

    /// <summary>当前订阅着的那份作业板配置。配置从云端恢复时整个对象会被换掉（见 OnConfigReloaded）。</summary>
    private BoardSettingsConfig _settings;

    /// <summary>当前订阅着的那份基础配置（只为了盯「置底到桌面」这一个键，见 OnBasicSettingsChanged）。</summary>
    private BasicSettingsConfig _basic;

    private Window? _mainWindow;
    private BoardDockWindow? _dockWindow;
    private bool _disposed;

    public BoardDockService(MainConfigHandler configHandler, ILogger<BoardDockService> logger)
    {
        _configHandler = configHandler;
        _logger = logger;

        _settings = configHandler.Data.BoardSettings;
        _settings.PropertyChanged += OnSettingsChanged;

        // 置底到桌面是基础配置里的键，但它决定了浮钮的层级（见 ApplyDockWindowLayer），
        // 所以要单独盯一下：用户收起来之后再改置底，浮钮也得跟着换层级。
        _basic = configHandler.Data.Basic;
        _basic.PropertyChanged += OnBasicSettingsChanged;

        // 从云端恢复设置会把配置对象整个换掉（ConfigHandlerBase.Reload），
        // 手里那份旧的要换下来，否则拖设置页时浮钮不会再跟着动。
        _configHandler.Reloaded += OnConfigReloaded;
    }

    /// <summary>作业板现在是不是收起状态。</summary>
    [ObservableProperty]
    private bool _isDocked;

    /// <summary>
    ///     把作业板收起来（改由浮在屏幕上的展开按钮代替主窗口）。
    /// </summary>
    /// <param name="owner">
    ///     主窗口。由主界面按钮传入（<c>TopLevel.GetTopLevel(this)</c>）；
    ///     不传时按桌面生命周期里的主窗口兜底。窗口取不到（应用正在退出等）就什么都不做，不抛。
    /// </param>
    public void Collapse(Window? owner = null)
    {
        if (_disposed || IsDocked)
            return;

        // 开关关着时标题栏那颗按钮本来就不显示，这里再挡一道：不该出现「没有入口却收起来了」的状态。
        if (!_configHandler.Data.BoardSettings.DockEnabled)
            return;

        var mainWindow = ResolveMainWindow(owner);
        if (mainWindow is null)
        {
            _logger.LogDebug("找不到主窗口，收纳请求被忽略。");
            return;
        }

        AttachMainWindow(mainWindow);

        var dockWindow = EnsureDockWindow();

        // 先置状态再藏主窗口：藏的时候主窗口会报可见性变化，那时已经是「收起」态，不会被当成
        // 「外部把主窗口显示回来了」（见 OnMainWindowPropertyChanged）。
        IsDocked = true;
        HideMainWindow(mainWindow);

        ApplyDockWindowPosition();

        if (!dockWindow.IsVisible)
            dockWindow.Show();

        // 显示之后再摆一次：有些平台在原生窗口创建出来之前不接受 Position，
        // 只在 Show 之前设会落到平台默认位置（屏幕左上角），显示后补一次才可靠。
        ApplyDockWindowPosition();

        // 层级最后再定：句柄要等 Show 之后才存在，而且置底状态是主窗口那边现算的。
        ApplyDockWindowLayer();
    }

    /// <summary>把作业板展开（收起状态的逆操作）：收掉浮钮、把主窗口显示回来并重放窗口能力。</summary>
    public void Expand()
    {
        if (_disposed)
            return;

        var mainWindow = _mainWindow;

        // 先清状态：Show 之后主窗口的可见性变化不能再被当成「外部显示」。
        IsDocked = false;
        HideDockWindow();

        if (mainWindow is null)
            return;

        ShowMainWindow(mainWindow);
    }

    /// <summary>主窗口被外部重新显示（托盘菜单「显示主窗口」、别的代码 Show）时自动收起浮钮。</summary>
    private void OnMainWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || _disposed || !IsDocked)
            return;

        if (sender is Window { IsVisible: true })
        {
            _logger.LogDebug("主窗口被外部重新显示，顺手收起收纳浮钮。");
            Expand();
        }
    }

    /// <summary>「设置 → 作业板 → 收纳」里改位置/偏移/大小，或关掉开关时实时生效。</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _configHandler.Data.BoardSettings))
            return;

        // PropertyName 为 null 表示「所有属性都可能变了」。
        if (e.PropertyName is null or nameof(BoardSettingsConfig.DockEnabled))
        {
            // 开关关掉时不能把板子留在收起态：标题栏按钮会跟着消失，用户就没有入口把它叫回来了。
            if (!_configHandler.Data.BoardSettings.DockEnabled && IsDocked)
                Expand();
        }

        if (e.PropertyName is null
            or nameof(BoardSettingsConfig.DockAnchor)
            or nameof(BoardSettingsConfig.DockOffsetX)
            or nameof(BoardSettingsConfig.DockOffsetY)
            or nameof(BoardSettingsConfig.DockScalePercent))
        {
            if (IsDocked)
                ApplyDockWindowPosition();
        }
    }

    /// <summary>主窗口的「置底到桌面」变了：浮钮的层级要跟着换（它自己是独立窗口，不会自动跟随）。</summary>
    private void OnBasicSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(BasicSettingsConfig.PinToDesktop))
            ApplyDockWindowLayerDeferred();
    }

    /// <summary>
    ///     推迟到下一轮消息循环再定浮钮层级。
    ///     <para>
    ///         ⚠️ 不能在这一拍直接读 <c>MainWindow.IsPinnedToDesktop</c>：本服务在构造期就订阅了配置，
    ///         而主窗口是 <c>Loaded</c> 时才订阅——同一份配置变化，主窗口那条处理器排在后面，
    ///         此刻 <c>_isPinnedToDesktop</c> 还是旧值，浮钮会晚一步才跟上来（表现是「取消置底后浮钮还是
    ///         不置顶，要再点一次」）。挪到 <see cref="DispatcherPriority.Background" /> 之后，
    ///         主窗口那条处理器已经跑完，读到的就是这一拍的新值。
    ///     </para>
    /// </summary>
    private void ApplyDockWindowLayerDeferred() =>
        Dispatcher.UIThread.Post(ApplyDockWindowLayer, DispatcherPriority.Background);

    /// <summary>配置对象被整体换掉（云端恢复设置）：重新挂订阅，并按新配置摆一次。</summary>
    private void OnConfigReloaded(object? sender, EventArgs e)
    {
        var settings = _configHandler.Data.BoardSettings;
        if (!ReferenceEquals(settings, _settings))
        {
            _settings.PropertyChanged -= OnSettingsChanged;
            _settings = settings;
            _settings.PropertyChanged += OnSettingsChanged;
        }

        var basic = _configHandler.Data.Basic;
        if (!ReferenceEquals(basic, _basic))
        {
            _basic.PropertyChanged -= OnBasicSettingsChanged;
            _basic = basic;
            _basic.PropertyChanged += OnBasicSettingsChanged;
        }

        if (!_configHandler.Data.BoardSettings.DockEnabled && IsDocked)
        {
            Expand();
            return;
        }

        if (IsDocked)
        {
            ApplyDockWindowPosition();
            ApplyDockWindowLayer();
        }
    }

    /// <summary>屏幕变了（接了显示器、改了分辨率/缩放）：浮钮跟着重新摆。</summary>
    private void OnScreensChanged(object? sender, EventArgs e)
    {
        if (!_disposed && IsDocked)
            ApplyDockWindowPosition();
    }

    /// <summary>主窗口关了：浮钮没有存在意义，一起收掉（不重新展开窗口）。</summary>
    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        DetachMainWindow();

        IsDocked = false;
        HideDockWindow();
    }

    #region 主窗口

    private Window? ResolveMainWindow(Window? hint)
    {
        if (hint is not null)
            return hint;

        if (_mainWindow is not null)
            return _mainWindow;

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        return desktop.MainWindow ?? desktop.Windows.OfType<MainWindow>().FirstOrDefault();
    }

    private void AttachMainWindow(Window window)
    {
        if (ReferenceEquals(_mainWindow, window))
            return;

        DetachMainWindow();

        _mainWindow = window;
        window.PropertyChanged += OnMainWindowPropertyChanged;
        window.Closed += OnMainWindowClosed;

        try
        {
            window.Screens.Changed += OnScreensChanged;
        }
        catch (Exception exception)
        {
            // 平台还没准备好屏幕信息时取不到，浮钮照样能显示，只是不跟着显示器变化重摆。
            _logger.LogDebug(exception, "订阅屏幕变化失败，收纳按钮将不会跟随显示器变化。");
        }
    }

    private void DetachMainWindow()
    {
        if (_mainWindow is not { } window)
            return;

        window.PropertyChanged -= OnMainWindowPropertyChanged;
        window.Closed -= OnMainWindowClosed;

        try
        {
            window.Screens.Changed -= OnScreensChanged;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "退订屏幕变化失败。");
        }

        _mainWindow = null;
    }

    /// <summary>
    ///     藏主窗口。主窗口自己的 <c>HideForDock</c> 会顺带收掉锁定徽标（那是它的属主窗口，
    ///     主窗口藏起来后徽标不该单独留在屏幕上）。
    /// </summary>
    private static void HideMainWindow(Window window)
    {
        if (window is MainWindow mainWindow)
            mainWindow.HideForDock();
        else
            window.Hide();
    }

    /// <summary>
    ///     显示主窗口。主窗口自己的 <c>ShowFromDock</c> 会在 Show 之后重放置底/穿透/不透明度——
    ///     隐藏与最小化都会让平台摘掉 <c>WS_EX_LAYERED</c> / <c>WS_EX_TRANSPARENT</c> / 置底的 <c>WS_CHILD</c>。
    /// </summary>
    private static void ShowMainWindow(Window window)
    {
        if (window is MainWindow mainWindow)
        {
            mainWindow.ShowFromDock();
            return;
        }

        if (!window.IsVisible)
            window.Show();

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
    }

    #endregion

    #region 浮钮

    private BoardDockWindow EnsureDockWindow()
    {
        if (_dockWindow is { } existing)
            return existing;

        var window = new BoardDockWindow();
        window.ExpandRequested += OnDockExpandRequested;
        window.Closed += OnDockWindowClosed;

        // 记下这颗窗口自己的默认层级（XAML 里写的是置顶），置底关掉时按它还原。
        FloatingWindowLayer.Register(window);

        _dockWindow = window;
        return window;
    }

    private void OnDockExpandRequested(object? sender, EventArgs e) => Expand();

    private void OnDockWindowClosed(object? sender, EventArgs e)
    {
        if (_dockWindow is { } window)
        {
            window.ExpandRequested -= OnDockExpandRequested;
            window.Closed -= OnDockWindowClosed;
        }

        _dockWindow = null;

        // 浮钮被意外关掉（它不是用户能关的窗口，正常只会由本服务收）：板子还藏着的话必须兜底展开，
        // 否则屏幕上既没有板子也没有按钮，用户只能去托盘菜单里找。
        if (!_disposed && IsDocked && _mainWindow is { } mainWindow)
        {
            _logger.LogWarning("收纳浮钮意外关闭，改为展开主窗口。");
            IsDocked = false;
            ShowMainWindow(mainWindow);
        }
    }

    private void HideDockWindow()
    {
        if (_dockWindow is not { IsVisible: true } window)
            return;

        window.Hide();
    }

    /// <summary>
    ///     按主窗口是否置底，决定收纳浮钮是「置顶」还是「跟板子一样当普通窗口」。
    ///     <para>
    ///         用户反馈：开了置底之后，板子已经沉到桌面了，这颗展开按钮却还浮在所有窗口之上。
    ///         所以置底生效时它跟着退掉置顶（被普通窗口盖住），与板子待在同一个层级；
    ///         置底没生效时照旧置顶，板子被别的窗口盖住时也仍然点得到。
    ///     </para>
    ///     <para>
    ///         ⚠️ 独立窗口不会自己跟着主窗口变层级：主窗口重放置底、浮钮显示之后都要调一次
    ///         （见 <see cref="Collapse" />）。
    ///     </para>
    /// </summary>
    private void ApplyDockWindowLayer()
    {
        if (_dockWindow is not { } dockWindow)
            return;

        FloatingWindowLayer.Apply(dockWindow, ResolvePinnedToDesktop());
    }

    /// <summary>
    ///     主窗口的置底是否实际生效。主窗口不是 <see cref="MainWindow" />（或还没建好）时按配置值兜底。
    /// </summary>
    private bool ResolvePinnedToDesktop()
    {
        if (_mainWindow is MainWindow mainWindow)
            return mainWindow.IsPinnedToDesktop;

        return _configHandler.Data.Basic.PinToDesktop;
    }

    /// <summary>
    ///     按「贴靠位置 + 偏移 + 按钮大小」把浮钮摆到主窗口所在屏幕的工作区里。
    ///     <para>
    ///         偏移一律按「离选中那条边的距离」理解：选了右边，正数就往左收、选了下边就往回收，
    ///         这样默认值（右下角 + 24）在任何缩放下看起来都是离屏幕边 24 的间距，而不是贴边。
    ///         中间那一列 / 一行只按工作区中心对齐、不吃偏移
    ///         （见 <see cref="DockAnchors.IsHorizontalCenter" /> / <see cref="DockAnchors.IsVerticalCenter" />）。
    ///     </para>
    ///     <para>
    ///         最后还会把位置夹回工作区：偏移再大也不能把按钮推出屏幕，推出去就再也点不到了
    ///         （负偏移会先被夹回屏幕边缘）。
    ///     </para>
    /// </summary>
    private void ApplyDockWindowPosition()
    {
        if (_dockWindow is not { } dockWindow || _mainWindow is not { } mainWindow)
            return;

        var settings = _configHandler.Data.BoardSettings;
        var diameter = BaseButtonDiameter * (BoardSettingsConfig.ClampDockScale(settings.DockScalePercent) / 100.0);
        dockWindow.SetButtonDiameter(diameter);

        if (!TryGetScreen(mainWindow, dockWindow, out var screen) || screen is null)
        {
            // 屏幕信息取不到（平台未就绪 / 刚拔了显示器）：保持窗口原位置，不抛、也不把按钮挪到奇怪的地方。
            _logger.LogDebug("取不到屏幕工作区，收纳按钮保持原位。");
            return;
        }

        // 屏幕的 Bounds / WorkingArea 都是物理像素，而窗口位置写的是物理像素、按钮大小是 DIP，
        // 所以要按这块屏幕的缩放把两边换算到同一坐标系。
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var workArea = screen.WorkingArea;
        var sizePixels = Math.Max(1, (int)Math.Round(diameter * scaling));
        var offsetXPixels = (int)Math.Round(BoardSettingsConfig.ClampDockOffset(settings.DockOffsetX) * scaling);
        var offsetYPixels = (int)Math.Round(BoardSettingsConfig.ClampDockOffset(settings.DockOffsetY) * scaling);
        var anchor = DockAnchors.Normalize(settings.DockAnchor);
        var column = DockAnchors.Column(anchor);
        var row = DockAnchors.Row(anchor);

        // 贴着哪条边：左列贴左边、右列贴右边，中间那列居中；上下同理。
        var baseX = column switch
        {
            0 => workArea.X,
            1 => workArea.X + (workArea.Width - sizePixels) / 2,
            _ => workArea.Right - sizePixels
        };

        var baseY = row switch
        {
            0 => workArea.Y,
            1 => workArea.Y + (workArea.Height - sizePixels) / 2,
            _ => workArea.Bottom - sizePixels
        };

        // 偏移朝屏幕内侧：左边往右收、右边往左收；上边往下、下边往上。中间列 / 行不参与。
        var x = column switch
        {
            0 => baseX + offsetXPixels,
            1 => baseX,
            _ => baseX - offsetXPixels
        };

        var y = row switch
        {
            0 => baseY + offsetYPixels,
            1 => baseY,
            _ => baseY - offsetYPixels
        };

        var maxX = Math.Max(workArea.X, workArea.Right - sizePixels);
        var maxY = Math.Max(workArea.Y, workArea.Bottom - sizePixels);
        x = Math.Clamp(x, workArea.X, maxX);
        y = Math.Clamp(y, workArea.Y, maxY);

        // 位置交给 FloatingWindowLayer：浮钮可能被挂进桌面层（主窗口置底时），
        // 那时它是桌面宿主的子窗口，写屏幕坐标会整块偏掉（副显示器上尤其明显）。
        FloatingWindowLayer.Place(dockWindow, new PixelPoint(x, y));
    }

    /// <summary>
    ///     主窗口所在屏幕优先（多显示器时浮钮要出现在用户正在用的那块），
    ///     取不到再退到浮钮自己所在的屏幕，最后退到主屏幕。
    /// </summary>
    private bool TryGetScreen(Window mainWindow, Window dockWindow, out Screen? screen)
    {
        screen = null;

        try
        {
            var screens = mainWindow.Screens;
            if (screens is null)
                return false;

            screen = screens.ScreenFromWindow(mainWindow)
                     ?? screens.ScreenFromWindow(dockWindow)
                     ?? screens.Primary;

            return screen is not null;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "读取屏幕信息失败。");
            return false;
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _settings.PropertyChanged -= OnSettingsChanged;
        _basic.PropertyChanged -= OnBasicSettingsChanged;
        _configHandler.Reloaded -= OnConfigReloaded;

        DetachMainWindow();

        if (_dockWindow is { } window)
        {
            window.ExpandRequested -= OnDockExpandRequested;
            window.Closed -= OnDockWindowClosed;
            _dockWindow = null;
            window.Close();
        }

        IsDocked = false;
    }
}
