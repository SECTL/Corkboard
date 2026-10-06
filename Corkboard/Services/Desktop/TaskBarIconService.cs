using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using Corkboard.Core;
using Corkboard.Core.Icons;
using Corkboard.Platforms.Abstractions;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Services.Desktop;

/// <summary>
///     任务栏托盘图标：主窗口没有系统标题栏之后的常驻入口
///     （显示主窗口 / 打开设置 / 重启 / 退出）。菜单文案沿用 <c>Corkboard.Core/Langs/Common</c> 里的 Menu_* 键。
/// </summary>
/// <remarks>
///     <para>
///         图标暂时用图标字体里的字形现画一张位图，接入正式的多尺寸图标后换成
///         <c>Assets/Icons/board-icon.ico</c> 即可。
///     </para>
///     <para>
///         托盘只是入口之一：平台不具备托盘能力或创建失败时只写日志，不阻断启动、不影响其它入口。
///     </para>
/// </remarks>
public sealed class TaskBarIconService : IDisposable
{
    private const int GlyphIconSize = 32;

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
                Icon = CreateGlyphIcon(),
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
    ///     托盘只要位图，所以把字形（<see cref="FluentIcons.BoardFilled" />）画进一张位图再包成
    ///     <see cref="WindowIcon" />。用主题蓝而不是纯白/纯黑，浅色和深色任务栏上都看得见。
    /// </summary>
    private static WindowIcon CreateGlyphIcon()
    {
        var typeface = new Typeface(GlobalConstants.FluentIconsFontFamily);
        var text = new FormattedText(
            FluentIcons.BoardFilled,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            GlyphIconSize * 0.9,
            new SolidColorBrush(Color.Parse(GlobalConstants.DefaultThemeColor)));

        using var bitmap = new RenderTargetBitmap(new PixelSize(GlyphIconSize, GlyphIconSize), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawText(text, new Point(
                (GlyphIconSize - text.Width) / 2,
                (GlyphIconSize - text.Height) / 2));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return new WindowIcon(stream);
    }
}
