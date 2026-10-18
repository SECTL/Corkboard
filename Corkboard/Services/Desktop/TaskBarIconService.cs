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
///         左键点图标就是把作业板叫回来（收纳状态下等于「展开作业板」，见 <see cref="App.ShowMainWindow" />），
///         右键才出菜单——与 Windows 上绝大多数常驻程序的习惯一致。
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

    /// <summary>
    ///     托盘左键：把作业板叫回来——收纳状态下就是「展开作业板」（收起浮钮、显示主窗口），
    ///     与菜单里第一项「显示主窗口」走同一条路（<see cref="App.ShowMainWindow" />）。
    /// </summary>
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
    ///     托盘图标与窗口图标共用一份 PNG 母图（<see cref="AppIcon" />），
    ///     平台按托盘需要的尺寸自己缩放。
    /// </summary>
    private static WindowIcon CreateTrayIcon() => AppIcon.LoadWindowIcon();
}
