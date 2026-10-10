using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAvalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Controls;
using Corkboard.Core.Enums;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Extensions.Registry;
using Corkboard.Core.Icons;
using Corkboard.Core.Models;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Models.SubConfigs.Personalized;
using Corkboard.Core.Services;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Services.Fonts;
using Corkboard.Platforms;
using Corkboard.Platforms.Abstractions;
using Corkboard.Services;
using Corkboard.Services.Auth;
using Corkboard.Services.Desktop;
using Corkboard.Services.Ipc;
using Corkboard.Services.Ui;
using Corkboard.ViewModels;
using Corkboard.ViewModels.MainPages;
using Corkboard.ViewModels.SettingsPages;
using Corkboard.Views;
using Corkboard.Views.MainPages;
using Corkboard.Views.SettingsPages.About;
using Corkboard.Views.SettingsPages.Board;
using Corkboard.Views.SettingsPages.General;
using Corkboard.Views.SettingsPages.Personalized;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard;

/// <summary>
///     应用宿主：负责三件事——启动期语言/主题、DI 容器装配（<see cref="BuildHost" />）、窗口生命周期。
///     <para>
///         装配顺序刻意保持为「语言 → XAML → 外观 → Host」，因为 XAML 里的 <c>x:Static</c> 资源
///         在加载那一刻就会按当前文化取值，晚一步改文化不会生效。
///     </para>
/// </summary>
public partial class App : Application
{
    private static MainWindow? _mainWindow;
    private static MainWindow? _settingsWindow;
    private static IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private static Task? _hostStartupTask;
    private MainConfigModel _startupSettings = new();
    private bool _isStopping;

    public new static App Current => (Application.Current as App)!;
    public static bool IsDesktop;

    public event EventHandler? AppStarted;
    public event EventHandler? AppStopping;

    public override void Initialize()
    {
        _startupSettings = LoadStartupSettings();
        InitializeLanguages(ResolveCulture(_startupSettings.Basic.Language));

        // 「快速颜色」不是我们自己画的色块，而是库自带工具栏的静态色板；库在**造工具栏的时候**
        // 才把它读进弹出层，所以必须赶在第一个工具栏建出来之前写进去（这里早于任何窗口）。
        RichTextToolbarPalette.Apply(_startupSettings.BoardSettings.ResolvePaletteColors());

        AvaloniaXamlLoader.Load(this);

        ApplyThemeSettings(_startupSettings.Appearance);
        ApplyFontSettings(_startupSettings.Appearance);
        Resources[@"NavigationViewItemOnLeftIconBoxHeight"] = 20.0;

        // 开发期诊断（Avalonia DevTools）需要 Zeronia.Diagnostics / Avalonia.Diagnostics 包，
        // 骨架阶段先不引入；接回时在这里加 this.AttachDevTools()。
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktopLifetime = desktop;
            IsDesktop = true;

            BuildHost(PlatformStartupContext.Current);

            // 主窗口是桌面挂件（无系统标题栏），进程常驻由托盘菜单控制：
            // 关掉任一个窗口都不结束进程，「退出程序」才走 StopAsync。
            // 这也取代了原来的「关闭窗口后继续驻留」开关——常驻是固定行为。
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => AppStopping?.Invoke(this, EventArgs.Empty);

            // 「启动时显示主窗口」：关掉时**不把窗口交给生命周期**，Avalonia 就不会在 StartCore 里
            // 自动显示它（比先显示再隐藏稳妥：开机自启不该闪一下窗口）。窗口仍然建好放 _mainWindow，
            // 托盘一点就补上 MainWindow 并显示。
            var mainWindow = CreateMainWindow(AppConsts.MainWindowScope);
            if (_startupSettings.Basic.ShowStartupWindow)
                desktop.MainWindow = mainWindow;

            // 托盘图标要在 UI 线程、平台渲染就绪之后创建；失败只写日志，不阻断启动。
            IAppHost.TryGetService<TaskBarIconService>()?.Initialize();

            _hostStartupTask = StartHostAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    ///     唯一的 DI 注册点。新增服务只改这里，不要在别处偷偷 new。
    /// </summary>
    private void BuildHost(IPlatformServiceRoot platform)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.SetMinimumLevel(GlobalConstants.IsDevelopment ? LogLevel.Debug : LogLevel.Information);
        builder.Logging.AddCoreFileLogger();

        var services = builder.Services;

        // 平台原生服务：窗口能力契约由平台模块注册，视图只依赖 Platforms.Abstractions。
        services.AddPlatformServices(platform);

        // Core 运行时（配置、日志、领域服务）。
        services.AddCoreRuntimeServices();

        // 应用层服务：SECTL 账号登录（OAuth 2.0 PKCE + loopback 回调）。
        // User-Agent 里的产品版本取构建版本，避免出现第二处硬编码版本号。
        SectlAuthEndpoints.ProductVersion = GlobalConstants.Version;
        services.AddHttpClient();
        services.AddSingleton<SectlTokenStore>();
        services.AddSingleton<IDeviceIdProvider, SectlDeviceIdProvider>();
        services.AddSingleton<IAuthRedirectBrokerFactory, LoopbackAuthRedirectBrokerFactory>();
        services.AddSingleton<IAuthBrowser, SystemBrowserAuthBrowser>();
        services.AddSingleton<SectlAuthService>();
        services.AddSingleton<ISectlAuthService>(sp => sp.GetRequiredService<SectlAuthService>());
        services.AddHostedService<SectlHeartbeatService>();
        // 版本使用人数上报：每次启动一次，尽力而为，失败只写日志（平台标识见 GlobalConstants.PlatformId）。
        services.AddHostedService<PlatformVersionReportService>();

        // ClassIsland 通知通道：与抽取业务无关的通用 IPC 出口，连不上时按失败结果降级。
        services.AddSingleton<IClassIslandIpcConnection, ClassIslandIpcConnection>();

        // 托盘图标：主窗口没有系统标题栏，托盘是显示主窗口与退出的常驻入口。
        services.AddSingleton<TaskBarIconService>();

        // 用系统默认程序打开网址：关于页的链接用它，视图不直接碰原生启动 API。
        services.AddSingleton<IExternalLauncher, SystemExternalLauncher>();

        // 页面级弹层宿主：页面把表单塞进来，壳在窗口内容最上层画出来（遮罩才能盖住标题栏）。
        services.AddSingleton<PageOverlayService>();

        // 时空回放：入口在主界面标题栏，作业列表在主页面，两边靠这个单例共享状态。
        services.AddSingleton<BoardReplayService>();

        // 作业自动清理：到点把过期作业收进回收目录（规则与判定都在 Core 的 BoardCleanupService）。
        services.AddHostedService<BoardCleanupHostedService>();

        // ViewModel 与页面。AddMainPage/AddSettingsPage 同时写导航注册表和键控 DI，两者必须成对。
        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<BoardPageViewModel>();
        services.AddTransient<BoardAssignmentFormViewModel>();
        services.AddTransient<BasicSettingsPageViewModel>();
        services.AddTransient<BoardSettingsPageViewModel>();
        services.AddTransient<AppearanceSettingsPageViewModel>();
        services.AddTransient<AboutSettingsPageViewModel>();

        // 主页面标题的默认文案；用户可以在作业板设置里改成别的名字。
        services.AddMainPage<BoardPage>(CR.Board_Title);

        services.AddSettingsPageSeparator();
        // 设置项直接平铺：注册表按 GroupId 是否为空决定「顶层项」还是「可折叠分组」，
        // 单页分组只会在侧边栏多一个 chevron，所以这里不建分组（要归组再加 AddGroup + groupId）。
        services.AddSettingsPage<BasicSettingsPage>(CR.Settings_Basic_Title);
        services.AddSettingsPage<BoardSettingsPage>(CR.Settings_Board_Title);
        services.AddSettingsPage<AppearanceSettingsPage>(CR.Settings_Appearance_Title);
        services.AddSettingsPage<AboutSettingsPage>(CR.Settings_About_Title);

        IAppHost.Host = builder.Build();
    }

    private static async Task StartHostAsync()
    {
        try
        {
            if (IAppHost.Host is { } host)
                await host.StartAsync().ConfigureAwait(true);

            // 开机自启是系统集成：启动时按配置重新写一遍（用户可能在别处删掉了自启项）。
            EnsureAutostartIntegration();

            // 账号会话恢复只影响登录态，失败不能拖住启动。
            _ = RestoreAccountSessionAsync();
            // ClassIsland 通道探测同样是后台行为，结果只写日志。
            _ = ProbeClassIslandAsync();

            Current.AppStarted?.Invoke(Current, EventArgs.Empty);

            // 冒烟验证用：带 --open-settings 启动时直接打开设置窗口，不影响正常启动流程。
            if (Environment.GetCommandLineArgs()
                .Contains("--open-settings", StringComparer.OrdinalIgnoreCase))
                ShowSettingsWindow();
        }
        catch (Exception exception)
        {
            IAppHost.TryGetService<ILogger<App>>()?.LogError(exception, "启动后台服务失败。");
        }
    }

    /// <summary>
    ///     按配置重新写一遍开机自启项：用户在系统里手动删掉自启项后，下次启动仍然按设置恢复。
    ///     失败时把配置回滚并只写日志——自启是系统集成，不能把假成功留在配置里。
    /// </summary>
    private static void EnsureAutostartIntegration()
    {
        var handler = IAppHost.TryGetService<MainConfigHandler>();
        var autostart = IAppHost.TryGetService<IAutostartService>();
        if (handler is null || autostart is null || !autostart.IsSupported)
            return;

        var basic = handler.Data.Basic;
        if (basic.Autostart && !autostart.TrySetEnabled(true, out var error))
        {
            basic.Autostart = false;
            handler.Save();
            IAppHost.TryGetService<ILogger<App>>()
                ?.LogWarning("无法恢复开机自启设置，已回滚：{Error}", error);
        }
    }

    private static async Task RestoreAccountSessionAsync()
    {
        try
        {
            if (IAppHost.TryGetService<ISectlAuthService>() is { } auth)
                await auth.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            IAppHost.TryGetService<ILogger<App>>()?.LogWarning(exception, "恢复账号会话失败。");
        }
    }

    private static async Task ProbeClassIslandAsync()
    {
        try
        {
            if (IAppHost.TryGetService<IClassIslandIpcConnection>() is not { } ipc)
                return;

            var result = await ipc.ProbeAsync().ConfigureAwait(false);
            IAppHost.TryGetService<ILogger<App>>()
                ?.LogInformation("ClassIsland IPC 探测结果：{State}（插件版本 {Version}）",
                    result.State, result.PluginVersion);
        }
        catch (Exception exception)
        {
            IAppHost.TryGetService<ILogger<App>>()?.LogDebug(exception, "ClassIsland IPC 探测失败。");
        }
    }

    /// <summary>
    ///     启动期读取 <c>settings.json</c>。此时还没有 DI 容器，因此直接解析 JSON；
    ///     读失败就退回默认配置，绝不因为一份坏配置拒绝启动。
    /// </summary>
    private static MainConfigModel LoadStartupSettings()
    {
        try
        {
            var path = new MainConfigModel().ConfigFilePath;
            if (!File.Exists(path))
                return new MainConfigModel();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)
                   ?? new MainConfigModel();
        }
        catch (Exception)
        {
            return new MainConfigModel();
        }
    }

    private static CultureInfo ResolveCulture(LanguageMode language)
    {
        return new CultureInfo(language switch
        {
            LanguageMode.English => "en-US",
            _ => "zh-Hans"
        });
    }

    /// <summary>切换语言：同时改线程文化、各资源程序集的 Culture 与库自带富文本界面的文案。</summary>
    public static void InitializeLanguages(CultureInfo cultureInfo)
    {
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
        CR.Culture = cultureInfo;
        Langs.SettingsView.Resources.Culture = cultureInfo;
        RichTextLocalization.Apply(!string.Equals(cultureInfo.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     应用主题设置：主题模式（跟随系统 / 浅色 / 深色）与主题色（跟随系统 / 自定义）。
    ///     写法与上游 SecRandom-C 的 <c>ApplyThemeSettings</c> 一致，两点顺序不能颠倒：
    ///     <list type="bullet">
    ///         <item>先设 <c>PreferSystemTheme</c>、再设显式变体：FluentAvalonia 在切资源集的过程中
    ///         会用自己的系统跟踪覆盖 <see cref="Application.RequestedThemeVariant" />，顺序反了会被吃掉。</item>
    ///         <item>「自定义主题色」要先关掉 <c>PreferUserAccentColor</c>，否则用户系统主题色一直压着自定义值；
    ///         切回「跟随系统」则要把 <c>CustomAccentColor</c> 清成 null，否则自定义色还挂在主题上。</item>
    ///     </list>
    /// </summary>
    private void ApplyThemeSettings(AppearanceSettingsConfig settings)
    {
        var useSystemTheme = settings.Theme == ThemeMode.FollowSystem;
        var requestedThemeVariant = settings.Theme switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault() is { } theme)
        {
            if (theme.PreferSystemTheme != useSystemTheme)
                theme.PreferSystemTheme = useSystemTheme;

            if (settings.ThemeColorMode == ThemeColorMode.Default)
            {
                if (theme.CustomAccentColor is not null)
                    theme.CustomAccentColor = null;
                if (!theme.PreferUserAccentColor)
                    theme.PreferUserAccentColor = true;
            }
            else
            {
                if (theme.PreferUserAccentColor)
                    theme.PreferUserAccentColor = false;
                if (theme.CustomAccentColor is not { } accentColor || accentColor != settings.ThemeColor)
                    theme.CustomAccentColor = settings.ThemeColor;
            }
        }

        // 「跟随系统」时不写 Default：那时由 PreferSystemTheme 的系统跟踪决定实际变体，
        // 再写一次 Default 会让 FluentAvalonia 刚同步好的资源集又切一遍。
        if (!Equals(RequestedThemeVariant, requestedThemeVariant) && requestedThemeVariant != ThemeVariant.Default)
            RequestedThemeVariant = requestedThemeVariant;
    }

    /// <summary>
    ///     外观设置改动后重新应用（设置页改字体、字重或主题时调用）。不需要重启。
    /// </summary>
    public void RefreshAppearanceSettings()
    {
        if (IAppHost.TryGetService<MainConfigHandler>()?.Data.Appearance is not { } settings)
            return;

        ApplyThemeSettings(settings);
        ApplyFontSettings(settings);
    }

    /// <summary>
    ///     应用字体族与字重。三个资源缺一不可：
    ///     <c>ContentControlThemeFontFamily</c> 是 FluentAvalonia / Avalonia 控件主题取字体的键，
    ///     <c>AppFontFamily</c> 供页面与自定义样式引用（两者必须同值），
    ///     <c>AppFontWeight</c> 由 Core 的 <c>:is(Window)</c> 样式继承下去。
    /// </summary>
    private void ApplyFontSettings(AppearanceSettingsConfig settings)
    {
        var fontFamily = FontFamilyCatalog.Resolve(settings.Font);
        Resources[@"AppFontFamily"] = fontFamily;
        Resources[@"ContentControlThemeFontFamily"] = fontFamily;
        Resources[@"AppFontWeight"] = ToFontWeight(settings.FontWeight);
    }

    private static FontWeight ToFontWeight(FontWeightMode mode)
    {
        return mode switch
        {
            FontWeightMode.Thin => FontWeight.Thin,
            FontWeightMode.ExtraLight => FontWeight.ExtraLight,
            FontWeightMode.Light => FontWeight.Light,
            FontWeightMode.Medium => FontWeight.Medium,
            FontWeightMode.SemiBold => FontWeight.SemiBold,
            FontWeightMode.Bold => FontWeight.Bold,
            FontWeightMode.ExtraBold => FontWeight.ExtraBold,
            FontWeightMode.Black => FontWeight.Black,
            _ => FontWeight.Normal
        };
    }

    public static MainWindow CreateMainWindow(string scope)
    {
        var window = new MainWindow(scope)
        {
            Title = scope == AppConsts.SettingsWindowScope ? CR.Settings_Title : CR.App_Title
        };

        if (scope == AppConsts.SettingsWindowScope)
        {
            _settingsWindow = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_settingsWindow, window))
                    _settingsWindow = null;
            };
        }
        else
        {
            _mainWindow = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_mainWindow, window))
                    _mainWindow = null;
            };
        }

        return window;
    }

    public static void ShowMainWindow()
    {
        var window = _mainWindow ??= CreateMainWindow(AppConsts.MainWindowScope);

        // 「启动时显示主窗口」关掉时没把它交给生命周期，这里补上，让主窗口仍是生命周期认定的主窗口。
        if (_desktopLifetime is { MainWindow: null } desktop)
            desktop.MainWindow = window;

        ShowAndActivate(window);
    }

    public static void ShowSettingsWindow()
    {
        var window = _settingsWindow ??= CreateMainWindow(AppConsts.SettingsWindowScope);
        ShowAndActivate(window);
    }

    private static void ShowAndActivate(Window window)
    {
        if (!window.IsVisible)
            window.Show();

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
    }

    /// <summary>退出应用：先停 Host，再关窗口。</summary>
    public async Task StopAsync()
    {
        if (_isStopping)
            return;

        _isStopping = true;
        AppStopping?.Invoke(this, EventArgs.Empty);

        // 托盘图标先摘掉：它挂在平台原生对象上，不能等到 Host 释放之后再清理。
        IAppHost.TryGetService<TaskBarIconService>()?.Dispose();

        try
        {
            if (_hostStartupTask is not null)
                await _hostStartupTask.ConfigureAwait(true);

            if (IAppHost.Host is { } host)
            {
                await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                host.Dispose();
                IAppHost.Host = null;
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        _desktopLifetime?.Shutdown();
    }

    /// <summary>重启：启动同一份可执行文件后退出当前进程。便携包应交给 Launcher 处理，骨架阶段先直接重启。</summary>
    public void Restart()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return;

        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        _ = StopAsync();
    }
}
