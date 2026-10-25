using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using Corkboard.Platforms.Abstractions;
using Corkboard.Views;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Services.Desktop;

/// <summary>
///     任务栏托盘图标：主窗口没有系统标题栏之后的常驻入口
///     （显示主窗口 / 打开设置 / 重启 / 退出）。菜单文案沿用 <c>Corkboard.Core/Langs/Common</c> 里的 Menu_* 键。
/// </summary>
/// <remarks>
///     <para>
///         <b>左右键都弹菜单</b>，而且是同一份菜单：右键走平台原生菜单（<see cref="NativeMenu" />），
///         左键走自己画的 <see cref="TrayMenuWindow" />。条目只有一张表（<see cref="TrayMenuItem" />），
///         两边各自映射过去，不要在托盘那边另写一套。
///     </para>
///     <para>
///         为什么左键要自己画：Avalonia 的 Win32 托盘只在右键时弹 <c>NativeMenu</c>
///         （<c>WM_TRAYMOUSE</c> 的 lParam 514 → <c>Clicked</c>、517 → 弹菜单），左键只报一次点击。
///         而 macOS 的状态栏项与 Linux 的 SNI/AppIndicator 宿主本来就是「左键即菜单」，
///         那两边<b>不要</b>再挂 <c>Clicked</c>，否则会同时弹出两个菜单——
///         判据是 <see cref="PlatformCapabilities.SupportsTrayMenuOnLeftClick" />。
///     </para>
///     <para>
///         图标与窗口图标是同一份 PNG 母图（<see cref="AppIcon" />），平台按托盘需要的尺寸自己缩放；
///         不再用 ICO——那份多尺寸容器只在 exe 文件图标里还需要。
///     </para>
///     <para>
///         托盘只是入口之一：平台不具备托盘能力或创建失败时只写日志，不阻断启动、不影响其它入口。
///     </para>
/// </remarks>
public sealed class TaskBarIconService : IDisposable
{
    private readonly PlatformCapabilities _capabilities;
    private readonly IPointerLocator _pointer;
    private readonly ILogger<TaskBarIconService> _logger;
    private readonly MainConfigHandler _configHandler;
    private BoardSettingsConfig _watchedBoardSettings;
    private TrayMenuWindow? _menuWindow;
    private TrayIcon? _trayIcon;
    private TrayIcons? _icons;

    public TaskBarIconService(
        PlatformCapabilities capabilities,
        IPointerLocator pointer,
        ILogger<TaskBarIconService> logger,
        MainConfigHandler configHandler)
    {
        _capabilities = capabilities;
        _pointer = pointer;
        _logger = logger;
        _configHandler = configHandler;

        // 「启用作业板」关掉之后主窗口在这条菜单里就不该还有入口（点了也是把板子叫回来，
        // 与那个开关的意思直接冲突），所以菜单要跟着那个键重建。
        _watchedBoardSettings = configHandler.Data.BoardSettings;
        _watchedBoardSettings.PropertyChanged += OnBoardSettingsChanged;
        configHandler.Reloaded += OnConfigReloaded;
    }

    /// <summary>托盘图标是否已创建。重复调用 <see cref="Initialize" /> 不会重复创建。</summary>
    public bool IsCreated => _trayIcon is not null;

    /// <summary>创建托盘图标。必须在 UI 线程上调用（托盘图标依赖平台原生对象）。</summary>
    public void Initialize()
    {
        if (_trayIcon is not null)
            return;

        if (!_capabilities.SupportsTrayIcon)
        {
            _logger.LogInformation("当前平台不支持托盘图标，跳过创建。");
            return;
        }

        try
        {
            var trayIcon = new TrayIcon
            {
                Icon = CreateTrayIcon(),
                ToolTipText = GlobalConstants.AppName,
                Menu = CreateNativeMenu(),
                IsVisible = true
            };

            // 平台自己就会在左键上弹菜单（macOS / Linux）时不要再接：那样会同时出来两个菜单。
            if (!_capabilities.SupportsTrayMenuOnLeftClick)
                trayIcon.Clicked += OnTrayIconClicked;

            // 挂到 Application 的 TrayIcons 上，让图标随应用生命周期一起管理，而不是被 GC 掉。
            if (Application.Current is { } application)
            {
                _icons = new TrayIcons { trayIcon };
                TrayIcon.SetIcons(application, _icons);
            }

            _trayIcon = trayIcon;
            _logger.LogInformation("托盘图标已创建。");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "创建托盘图标失败；其它入口仍然可用。");
        }
    }

    public void Dispose()
    {
        var trayIcon = _trayIcon;
        _trayIcon = null;

        try
        {
            _icons?.Clear();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "从应用托盘集合里移除图标时出错。");
        }
        finally
        {
            _icons = null;
        }

        CloseMenuWindow();

        _watchedBoardSettings.PropertyChanged -= OnBoardSettingsChanged;
        _configHandler.Reloaded -= OnConfigReloaded;

        if (trayIcon is null)
            return;

        try
        {
            trayIcon.Clicked -= OnTrayIconClicked;
            trayIcon.IsVisible = false;
            trayIcon.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "释放托盘图标时出错。");
        }
    }

    /// <summary>托盘左键：贴着鼠标弹出菜单（与右键那份同一套条目）。</summary>
    private void OnTrayIconClicked(object? sender, EventArgs e) => ShowMenu();

    /// <summary>「启用作业板」变了：原生菜单是启动时按当次条目建出来的快照，要重建一遍。</summary>
    private void OnBoardSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or nameof(BoardSettingsConfig.BoardEnabled)))
            return;

        RefreshMenu();
    }

    /// <summary>从云端恢复设置会把配置对象整个换掉，手里那份旧的订阅要换下来。</summary>
    private void OnConfigReloaded(object? sender, EventArgs e)
    {
        _watchedBoardSettings.PropertyChanged -= OnBoardSettingsChanged;
        _watchedBoardSettings = _configHandler.Data.BoardSettings;
        _watchedBoardSettings.PropertyChanged += OnBoardSettingsChanged;
        RefreshMenu();
    }

    private void RefreshMenu()
    {
        var trayIcon = _trayIcon;
        if (trayIcon is null)
            return;

        try
        {
            trayIcon.Menu = CreateNativeMenu();

            // 已经弹出来的那份还是旧条目，直接收掉：它上面可能挂着一颗「显示作业板」。
            CloseMenuWindow();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "刷新托盘菜单时出错。");
        }
    }

    /// <summary>
    ///     对外的那一个刷新入口：主窗口显示 / 藏起来 / 最小化 / 收纳展开之后都要叫一次。
    ///     <para>
    ///         右键那份是启动时按当次条目建出来的**快照**——「显示作业板」还是「隐藏作业板」
    ///         取决于主窗口此刻在不在屏幕上，状态变了却不重建，菜单就会一直挂着旧的那一句。
    ///     </para>
    /// </summary>
    public void NotifyBoardVisibilityChanged() => RefreshMenu();

    /// <summary>
    ///     弹出左键菜单。位置由窗口自己按 <see cref="IPointerLocator" /> 给的鼠标坐标算；
    ///     拿不到坐标时退回主屏右下角——菜单不能因为问不到鼠标就弹不出来。
    /// </summary>
    private void ShowMenu()
    {
        try
        {
            // 连点两下：先把上一张收掉，别叠成两层。
            CloseMenuWindow();

            ScreenPoint? anchor = _pointer.TryGetCursorPosition(out var cursor) ? cursor : null;

            var window = new TrayMenuWindow();
            _menuWindow = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_menuWindow, window))
                    _menuWindow = null;
            };

            window.ShowMenu(CurrentMenuItems, anchor);
        }
        catch (Exception exception)
        {
            // 菜单弹不出来不该影响任何其它入口，记一笔就够了。
            _logger.LogWarning(exception, "弹出托盘菜单失败。");
        }
    }

    private void CloseMenuWindow()
    {
        var window = _menuWindow;
        _menuWindow = null;

        if (window is null)
            return;

        try
        {
            window.Close();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "关闭托盘菜单时出错。");
        }
    }

    /// <summary>托盘菜单的唯一定义：右键的原生菜单与左键的自绘菜单都从这里映射出去。</summary>
    /// <remarks>
    ///     <para>
    ///         第一项照上游 SecRandom-C 的样子：图标 + 软件名，点下去直接进「关于」页
    ///         ——托盘里点软件名，用户想看的通常就是「这是哪个版本」。
    ///     </para>
    ///     <para>
    ///         作业板那一项是<b>一颗开关</b>：板子在屏幕上时写「隐藏作业板」、点下去收起来；
    ///         不在屏幕上（藏起来 / 最小化 / 收进浮钮）时写「显示作业板」、点下去叫回来。
    ///         文案不能写死成一句——只给「显示」的话用户没法从托盘把板子收掉，
    ///         只给「隐藏」则收掉之后就再也叫不回来（主窗口没有任务栏按钮）。
    ///     </para>
    ///     <para>
    ///         「启用作业板」关着时整项不列：那个开关的意思就是不要作业板，
    ///         菜单里再留一个把板子叫回来的入口，两边说法就打架了。考试看板与设置照旧。
    ///     </para>
    /// </remarks>
    private static IReadOnlyList<TrayMenuItem> CreateMenuItems(bool boardEnabled, bool boardShown)
    {
        var items = new List<TrayMenuItem>
        {
            new(GlobalConstants.AppName, static () => App.ShowSettingsWindow(AppConsts.SettingsAboutPageId),
                ShowApplicationIcon: true),
            TrayMenuItem.Separator
        };

        if (boardEnabled)
        {
            items.Add(boardShown
                ? new TrayMenuItem(CR.Menu_HideMainWindow, static () => App.HideMainWindow())
                : new TrayMenuItem(CR.Menu_OpenMainWindow, static () => App.ShowMainWindow()));
        }

        items.Add(new TrayMenuItem(CR.Menu_OpenExamBoard, static () => App.ShowExamBoardWindow()));
        items.Add(new TrayMenuItem(CR.Menu_OpenSettings, static () => App.ShowSettingsWindow()));
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(CR.Menu_RestartProgram, static () => App.Current.Restart()));
        items.Add(new TrayMenuItem(CR.Menu_ExitProgram, static () => _ = App.Current.StopAsync()));

        return items;
    }

    private IReadOnlyList<TrayMenuItem> CurrentMenuItems =>
        CreateMenuItems(_configHandler.Data.BoardSettings.BoardEnabled, App.IsMainWindowShown);

    /// <summary>右键那份：交给平台弹的原生菜单。</summary>
    private NativeMenu CreateNativeMenu()
    {
        var menu = new NativeMenu();

        foreach (var item in CurrentMenuItems)
        {
            if (item.IsSeparator)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }

            var action = item.Action;
            var nativeItem = new NativeMenuItem { Header = item.Header };
            if (item.ShowApplicationIcon)
                nativeItem.Icon = CreateMenuIconSource();
            nativeItem.Click += (_, _) => action?.Invoke();
            menu.Items.Add(nativeItem);
        }

        return menu;
    }

    /// <summary>
    ///     原生菜单项的图标。<c>NativeMenuItem.Icon</c> 收的是 <see cref="Avalonia.Media.Imaging.Bitmap" />
    ///     （与自绘那份 <see cref="MenuItem.Icon" /> 收控件内容不同），所以两边各取各的：
    ///     这里给位图，自绘菜单那边包一层 <c>Image</c>。
    /// </summary>
    private static Avalonia.Media.Imaging.Bitmap CreateMenuIconSource() => AppIcon.LoadBitmap();

    /// <summary>
    ///     托盘图标与窗口图标共用一份 PNG 母图（<see cref="AppIcon" />），
    ///     平台按托盘需要的尺寸自己缩放。
    /// </summary>
    private static WindowIcon CreateTrayIcon() => AppIcon.LoadWindowIcon();
}
