using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using FluentAvalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Enums;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Extensions.Registry;
using Corkboard.Core.Icons;
using Corkboard.Core.Models;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Models.SubConfigs.Personalized;
using Corkboard.Core.Services;
using Corkboard.Core.Services.Config;
using Corkboard.Platforms;
using Corkboard.Platforms.Abstractions;
using Corkboard.Services;
using Corkboard.Services.Auth;
using Corkboard.Services.Ipc;
using Corkboard.ViewModels;
using Corkboard.ViewModels.MainPages;
using Corkboard.ViewModels.SettingsPages;
using Corkboard.Views;
using Corkboard.Views.MainPages;
using Corkboard.Views.SettingsPages.About;
using Corkboard.Views.SettingsPages.General;
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

        AvaloniaXamlLoader.Load(this);

        ApplyThemeSettings(_startupSettings.Appearance);
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

            desktop.ShutdownMode = _startupSettings.Basic.BackgroundResident
                ? ShutdownMode.OnExplicitShutdown
                : ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) => AppStopping?.Invoke(this, EventArgs.Empty);

            desktop.MainWindow = CreateMainWindow(AppConsts.MainWindowScope);
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

        // ViewModel 与页面。AddMainPage/AddSettingsPage 同时写导航注册表和键控 DI，两者必须成对。
        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<BoardPageViewModel>();
        services.AddTransient<BasicSettingsPageViewModel>();
        services.AddTransient<AboutSettingsPageViewModel>();

        services.AddMainPage<BoardPage>(CR.Board_Title);

        services.AddSettingsPageSeparator();
        services.AddGroup(new PageGroupInfo(CR.Settings_Group_General, "settings.general", FluentIcons.HomeFilled));
        services.AddSettingsPage<BasicSettingsPage>(CR.Settings_Basic_Title);
        services.AddGroup(new PageGroupInfo(CR.Settings_Group_About, "settings.about", FluentIcons.InfoFilled));
        services.AddSettingsPage<AboutSettingsPage>(CR.Settings_About_Title);

        IAppHost.Host = builder.Build();
    }

    private static async Task StartHostAsync()
    {
        try
        {
            if (IAppHost.Host is { } host)
                await host.StartAsync().ConfigureAwait(true);

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
            LanguageMode.Japanese => "ja-JP",
            _ => "zh-Hans"
        });
    }

    /// <summary>切换语言：同时改线程文化与各资源程序集的 Culture。</summary>
    public static void InitializeLanguages(CultureInfo cultureInfo)
    {
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
        CR.Culture = cultureInfo;
        Langs.SettingsView.Resources.Culture = cultureInfo;
    }

    private void ApplyThemeSettings(AppearanceSettingsConfig settings)
    {
        RequestedThemeVariant = settings.Theme switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault() is { } theme
            && settings.ThemeColorMode == ThemeColorMode.Custom)
            theme.CustomAccentColor = settings.ThemeColor;
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
