using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Corkboard.Core;
using Corkboard.Platforms.Abstractions;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Services.Desktop;

/// <summary>
///     任务栏托盘图标：主窗口没有系统标题栏之后的常驻入口
///     （显示主窗口 / 打开设置 / 重启 / 退出）。菜单文案沿用 <c>Corkboard.Core/Langs/Common</c> 里的 Menu_* 键。
/// </summary>
/// <remarks>
///     <para>
///         图标用随包分发的多尺寸 <c>Assets/Icons/board-icon.ico</c>，与窗口图标、exe 图标同一份：
///         平台图标装载器会自己挑合适的那一档，不用手画位图、也不会在缩放比变化时糊掉。
///     </para>
///     <para>
///         托盘只是入口之一：平台不具备托盘能力或创建失败时只写日志，不阻断启动、不影响其它入口。
///     </para>
/// </remarks>
public sealed class TaskBarIconService : IDisposable
{
    /// <summary>图标资源：Corkboard 程序集里的多尺寸 ICO。</summary>
    private static readonly Uri IconAsset = new("avares://Corkboard/Assets/Icons/board-icon.ico");

    private readonly PlatformCapabilities _capabilities;
    private readonly ILogger<TaskBarIconService> _logger;
    private TrayIcon? _trayIcon;
    private TrayIcons? _icons;

    public TaskBarIconService(PlatformCapabilities capabilities, ILogger<TaskBarIconService> logger)
    {
        _capabilities = capabilities;
        _logger = logger;
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
                Menu = CreateMenu(),
                IsVisible = true
            };
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

    private void OnTrayIconClicked(object? sender, EventArgs e) => App.ShowMainWindow();

    private static NativeMenu CreateMenu()
    {
        var menu = new NativeMenu();
        menu.Items.Add(CreateMenuItem(CR.Menu_OpenMainWindow, static () => App.ShowMainWindow()));
        menu.Items.Add(CreateMenuItem(CR.Menu_OpenSettings, static () => App.ShowSettingsWindow()));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(CreateMenuItem(CR.Menu_RestartProgram, static () => App.Current.Restart()));
        menu.Items.Add(CreateMenuItem(CR.Menu_ExitProgram, static () => _ = App.Current.StopAsync()));
        return menu;
    }

    private static NativeMenuItem CreateMenuItem(string header, Action action)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    ///     读随包分发的多尺寸 ICO 交给平台图标装载器：Windows 直接拿整份 ICO 去建原生图标
    ///     （托盘/任务栏各取各自需要的尺寸），其它平台由平台层自行解码。
    /// </summary>
    private static WindowIcon CreateTrayIcon()
    {
        using var stream = AssetLoader.Open(IconAsset);
        return new WindowIcon(stream);
    }
}
