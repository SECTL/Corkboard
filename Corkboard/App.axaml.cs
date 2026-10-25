using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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
using Corkboard.Core.Board;
using Corkboard.Core.Controls;
using Corkboard.Core.Enums;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;
using Corkboard.Core.Extensions.Registry;
using Corkboard.Core.Icons;
using Corkboard.Core.Models;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Models.SubConfigs.Personalized;
using Corkboard.Core.Services;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Services.FirstRun;
using Corkboard.Core.Services.Fonts;
using Corkboard.Platforms;
using Corkboard.Platforms.Abstractions;
using Corkboard.Services;
using Corkboard.Services.Auth;
using Corkboard.Services.Cloud;
using Corkboard.Services.Desktop;
using Corkboard.Services.Exam;
using Corkboard.Services.Ipc;
using Corkboard.Services.Stats;
using Corkboard.Services.Time;
using Corkboard.Services.Board;
using Corkboard.Services.Homework;
using Corkboard.Services.Ui;
using Corkboard.Services.Updates;
using Corkboard.Shared;
using Corkboard.ViewModels;
using Corkboard.ViewModels.ExamBoard;
using Corkboard.ViewModels.MainPages.Board;
using Corkboard.ViewModels.MainPages.Homework;
using Corkboard.ViewModels.SettingsPages;
using Corkboard.ViewModels.SettingsPages.Board;
using Corkboard.ViewModels.SettingsPages.Exam;
using Corkboard.ViewModels.SettingsPages.Export;
using Corkboard.Views;
using Corkboard.Views.ExamBoard;
using Corkboard.Views.MainPages.Board;
using Corkboard.Views.MainPages.Homework;
using Corkboard.Views.SettingsPages.About;
using Corkboard.Views.SettingsPages.Account;
using Corkboard.Views.SettingsPages.Board;
using Corkboard.Views.SettingsPages.Cloud;
using Corkboard.Views.SettingsPages.Exam;
using Corkboard.Views.SettingsPages.Export;
using Corkboard.Views.SettingsPages.General;
using Corkboard.Views.SettingsPages.Personalized;
using Corkboard.Views.SettingsPages.Update;
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
    private static FirstRunOobeWindow? _oobeWindow;
    private static ExamBoardWindow? _examBoardWindow;
    private static ExamBoardEditorWindow? _examBoardEditorWindow;
    private static ExamPlanWizardWindow? _examPlanWizardWindow;
    private static IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private static Task? _hostStartupTask;

    /// <summary>正在盯「启用作业板」的那份配置（云端恢复设置会把配置对象整个换掉，所以要跟着换）。</summary>
    private static MainConfigHandler? _watchedConfigHandler;

    /// <summary>上述配置里的作业板子配置。</summary>
    private static BoardSettingsConfig? _watchedBoardSettings;

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

            // 首次运行引导（OOBE）：要引导时**不建主窗口**，先把引导铺出来。
            // 顺序不能反——引导里能关掉作业板，那时主窗口根本不该出现；而且引导要抢在
            // 任何窗口之前显示，否则会先闪一下主界面。
            if (IAppHost.TryGetService<FirstRunOobeService>() is { IsRequired: true })
            {
                ShowOobeWindow();
            }
            else
            {
                // 「启动时显示主窗口」：关掉时**不把窗口交给生命周期**，Avalonia 就不会在 StartCore 里
                // 自动显示它（比先显示再隐藏稳妥：开机自启不该闪一下窗口）。窗口仍然建好放 _mainWindow，
                // 托盘一点就补上 MainWindow 并显示。
                // 「启用作业板」关掉时干脆不建：这时主窗口没有任何入口（托盘里那一项也藏着），
                // 建出来只会白跑一遍页面与 ViewModel 的装配。
                if (_startupSettings.BoardSettings.BoardEnabled)
                {
                    var mainWindow = CreateMainWindow(AppConsts.MainWindowScope);
                    if (_startupSettings.Basic.ShowStartupWindow)
                        desktop.MainWindow = mainWindow;
                }
            }

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

        // 平台统计：在线状态（受隐私设置三态控制）与使用量增量。两者都要「后台跑 + 页面读缓存」，
        // 所以先注册单例、再把这个实例挂成托管服务，避免 DI 里出现两个互不相干的实例。
        services.AddSingleton<OnlineStatusService>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<OnlineStatusService>());
        services.AddSingleton<PlatformUsageReportService>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<PlatformUsageReportService>());

        // 云同步：把作业板与设置备份到思拓创联云存储。构造期就挂上配置与作业板的变化监听，
        // 所以启动时必须解析一次（见 StartHostAsync 的 InitializeCloudSyncAsync）。
        services.AddSingleton<ICloudStorageClient, CloudStorageClient>();
        services.AddSingleton<ICloudSyncService, CloudSyncService>();

        // ClassIsland 通知通道：与抽取业务无关的通用 IPC 出口，连不上时按失败结果降级。
        services.AddSingleton<IClassIslandIpcConnection, ClassIslandIpcConnection>();

        // 托盘图标：主窗口没有系统标题栏，托盘是显示主窗口与退出的常驻入口。
        services.AddSingleton<TaskBarIconService>();

        // 用系统默认程序打开网址：关于页的链接用它，视图不直接碰原生启动 API。
        services.AddSingleton<IExternalLauncher, SystemExternalLauncher>();

        // 自动更新：metadata 渠道 → 签名更新清单 → 下载/校验 → 便携部署或系统安装器。
        // 更新页与后台调度必须共用同一个单例，否则页面看不到调度器查出来的状态。
        services.AddHttpClient("updates", client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<UpdateCenterService>(serviceProvider => new UpdateCenterService(
            serviceProvider.GetRequiredService<MainConfigHandler>(),
            serviceProvider.GetRequiredService<ILogger<UpdateCenterService>>(),
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("updates")));
        services.AddSingleton<IUpdateNotificationService, UpdateNotificationService>();
        services.AddHostedService<UpdateScheduler>();

        // 页面级弹层宿主：页面把表单塞进来，壳在窗口内容最上层画出来（遮罩才能盖住标题栏）。
        services.AddSingleton<PageOverlayService>();

        // 时空回放：入口在主界面标题栏，作业列表在主页面，两边靠这个单例共享状态。
        services.AddSingleton<BoardReplayService>();

        // 导出作业板图片：只渲染板子上面那块内容，不带上窗口底色与桌面。
        services.AddSingleton<BoardExportService>();

        // 收纳：把主窗口收成屏幕角落的一颗展开按钮，位置与大小来自「设置 → 作业板 → 收纳」。
        services.AddSingleton<BoardDockService>();

        // 作业板自动存档：板子改完等几秒把整块板面存成 PNG，按「设置 → 作业板 → 自动化与数据」的规则裁剪。
        services.AddSingleton<BoardAutoSaveService>();

        // 作业自动清理：到点给过期作业打 CleanedAt 标记、从主页面上拿掉（规则与判定都在 Core 的 HomeworkCleanupService）。
        services.AddHostedService<HomeworkCleanupHostedService>();

        // 作业历史：把「历史与回放」的三项设置同步给存储层，并每小时按保留天数与容量上限清一次旧历史。
        services.AddHostedService<BoardHistoryHostedService>();

        // NTP 校时：按「设置 → 考试看板 → 时间同步」的间隔在后台对齐标准时间，看板时钟读它。
        services.AddHostedService<NtpSyncHostedService>();

        // 考试看板自动放映：到点自己把全屏看板弹出来，最后一场考完（延迟 N 分钟）再收回去。
        // 哪块看板参加由看板文件自己的 auto_show_enabled 决定，判定规则在 Core 的 ExamAutoShowPolicy。
        services.AddHostedService<ExamAutoShowHostedService>();

        // ViewModel 与页面。AddMainPage/AddSettingsPage 同时写导航注册表和键控 DI，两者必须成对。
        services.AddTransient<MainViewModel>();
        services.AddTransient<FirstRunOobeViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<BoardPageViewModel>();
        services.AddTransient<HomeworkAssignmentFormViewModel>();
        services.AddTransient<BasicSettingsPageViewModel>();
        services.AddTransient<BoardSettingsPageViewModel>();
        services.AddTransient<BoardContentSettingsPageViewModel>();
        services.AddTransient<BoardAutomationSettingsPageViewModel>();
        services.AddTransient<BoardWindowSettingsPageViewModel>();
        services.AddTransient<AppearanceSettingsPageViewModel>();
        services.AddTransient<AboutSettingsPageViewModel>();
        services.AddTransient<AccountSettingsPageViewModel>();
        services.AddTransient<CloudSyncSettingsPageViewModel>();
        services.AddTransient<UpdateSettingsPageViewModel>();
        services.AddTransient<BoardSectionsSettingsPageViewModel>();
        services.AddTransient<HandwritingSettingsPageViewModel>();
        services.AddTransient<QuickTagsSettingsPageViewModel>();
        services.AddTransient<CleanupSettingsPageViewModel>();
        services.AddTransient<HistorySettingsPageViewModel>();
        services.AddTransient<AutoSaveSettingsPageViewModel>();
        services.AddTransient<DockSettingsPageViewModel>();
        services.AddTransient<ExportSettingsPageViewModel>();
        services.AddTransient<ExamBoardViewModel>();
        services.AddTransient<ExamBoardEditorViewModel>();
        services.AddTransient<ExamPlanWizardViewModel>();
        services.AddTransient<ExamScheduleSettingsPageViewModel>();
        services.AddTransient<ExamDisplaySettingsPageViewModel>();
        services.AddTransient<ExamTimeSettingsPageViewModel>();
        services.AddTransient<ExamAutoShowSettingsPageViewModel>();

        // 主页面标题的默认文案；用户可以在作业板设置里改成别的名字。
        services.AddMainPage<BoardPage>(CR.Board_Title);

        // 顶层项顺序 = 注册顺序。「基础设置」之后紧跟「外观」，再往后才是「作业板」分组。
        // 侧边栏顶部不再放分隔线（原来的 AddSettingsPageSeparator 已按用户要求删掉）。
        services.AddSettingsPage<BasicSettingsPage>(CR.Settings_Basic_Title);
        services.AddSettingsPage<AppearanceSettingsPage>(CR.Settings_Appearance_Title);

        // 「作业板」是可折叠分组：分组本身出现在侧边栏，点开才是它底下的各个设置子页。
        // 子页的 PageInfo 上必须带同一个 groupId，否则会被当成顶层项平铺出去。
        services.AddGroup(new PageGroupInfo(CR.Settings_Board_Group, "settings.board.group", FluentIcons.BoardFilled));
        services.AddSettingsPage<BoardSettingsPage>(CR.Settings_Board_General_Title);
        services.AddSettingsPage<BoardContentSettingsPage>(CR.Settings_Board_Content_Title);
        services.AddSettingsPage<BoardSectionsSettingsPage>(CR.Settings_Board_Sections_Title);
        services.AddSettingsPage<BoardAutomationSettingsPage>(CR.Settings_Board_Automation_Title);
        services.AddSettingsPage<BoardWindowSettingsPage>(CR.Settings_Board_Window_Title);
        // 「导出」在作业板分组里：导出的就是作业板，放顶层会让人找不着。
        services.AddSettingsPage<ExportSettingsPage>(CR.Settings_Export_Title);

        // 「考试看板」是第二个可折叠分组，排在「作业板」下面：考试安排、看板显示、时间同步、自动放映。
        services.AddGroup(new PageGroupInfo(CR.Settings_Exam_Group, "settings.exam.group", FluentIcons.CalendarFilled));
        services.AddSettingsPage<ExamScheduleSettingsPage>(CR.Settings_Exam_Schedule_Title);
        services.AddSettingsPage<ExamDisplaySettingsPage>(CR.Settings_Exam_Display_Title);
        services.AddSettingsPage<ExamTimeSettingsPage>(CR.Settings_Exam_Time_Title);
        services.AddSettingsPage<ExamAutoShowSettingsPage>(CR.Settings_Exam_AutoShow_Title);

        // 「账号」「云同步」暂不开放入口：两个页面的 PageInfo 上标了 isHide: true，不进侧边栏。
        // 注册保留（键控 DI 仍按 Id 可取到），以后开放时去掉 PageInfo 上的 isHide 即可。
        services.AddSettingsPage<AccountSettingsPage>(CR.Settings_Account_Title);
        services.AddSettingsPage<CloudSyncSettingsPage>(CR.Settings_Cloud_Title);
        // 更新页沉在底部（PageInfo 的 location），与「关于」同区；注册顺序只影响上半区。
        services.AddSettingsPage<UpdateSettingsPage>(CR.Settings_Update_Title);
        // 「关于」沉在导航栏底部：位置由 AboutSettingsPage 上的 PageInfo(Location = Bottom) 决定，
        // 和这里注册在第几位无关（注册顺序只决定上半区那几项的先后）。
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

            // 「启用作业板」是全局开关：设置页里一拨，主窗口就该当场收掉或叫回来。
            WatchBoardEnabled();

            // 自动保存是「构造即开工」的单例（构造函数里起防抖与补存循环），
            // 这里主动取一次：页面还没建出来时它也在跑，跨天之后补存今天那张不会漏。
            IAppHost.TryGetService<BoardAutoSaveService>();

            // 账号会话恢复只影响登录态，失败不能拖住启动。
            var accountSession = RestoreAccountSessionAsync();
            // 云同步要用账号会话，所以串在它后面：已开启同步且已登录时查一遍云端。
            _ = InitializeCloudSyncAsync(accountSession);
            // ClassIsland 通道探测同样是后台行为，结果只写日志。
            _ = ProbeClassIslandAsync();

            Current.AppStarted?.Invoke(Current, EventArgs.Empty);

            // 冒烟验证用：带 --open-settings 启动时直接打开设置窗口，不影响正常启动流程。
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--open-settings", StringComparer.OrdinalIgnoreCase))
                ShowSettingsWindow();

            // 同上：带 --open-about 时直接落到「关于」页，方便对页头版式截图。
            if (args.Contains("--open-about", StringComparer.OrdinalIgnoreCase))
                ShowSettingsWindow(AppConsts.SettingsAboutPageId);

            // 同上：带 --open-exam-board 时直接铺开全屏考试看板，方便对版式截图。
            if (args.Contains("--open-exam-board", StringComparer.OrdinalIgnoreCase))
                ShowExamBoardWindow();

            // 同上：带 --open-oobe 时直接铺开首次运行引导，方便对版式截图（不改任何配置，
            // 退出时同正常引导一样走「没走完就退出」）。
            if (args.Contains("--open-oobe", StringComparer.OrdinalIgnoreCase))
                ShowOobeWindow();
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

    /// <summary>
    ///     盯住「启用作业板」这一个键：关掉时把主窗口收起来（进程留在托盘里），打开时把它叫回来。
    ///     <para>
    ///         之所以放在 App 而不是设置页：这个开关在引导里、设置页里都能改，还可能从云端恢复设置时
    ///     被整个覆盖（配置对象会换一份，所以还要跟着 <see cref="ConfigHandlerBase{T}.Reloaded" /> 换订阅）。
    ///         一处监听，两条入口写的是同一个键，行为就不会分叉。
    ///     </para>
    /// </summary>
    private static void WatchBoardEnabled()
    {
        if (_watchedConfigHandler is not null)
            return;

        var handler = IAppHost.TryGetService<MainConfigHandler>();
        if (handler is null)
            return;

        _watchedConfigHandler = handler;
        handler.Reloaded += OnConfigReloadedForBoardEnabled;

        _watchedBoardSettings = handler.Data.BoardSettings;
        _watchedBoardSettings.PropertyChanged += OnBoardSettingsChanged;
    }

    private static void OnConfigReloadedForBoardEnabled(object? sender, EventArgs e)
    {
        if (_watchedBoardSettings is { } previous)
            previous.PropertyChanged -= OnBoardSettingsChanged;

        _watchedBoardSettings = _watchedConfigHandler?.Data.BoardSettings;
        if (_watchedBoardSettings is { } current)
            current.PropertyChanged += OnBoardSettingsChanged;
    }

    /// <summary>PropertyName 为空表示「所有属性都可能变了」，按「变了」处理。</summary>
    private static void OnBoardSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or nameof(BoardSettingsConfig.BoardEnabled)))
            return;

        ApplyBoardEnabled(_watchedConfigHandler?.Data.BoardSettings.BoardEnabled ?? true);
    }

    /// <summary>
    ///     作业板开关的实际效果：关掉就把主窗口收掉（<see cref="MainWindow.HideForDock" /> 那套——
    ///     连同锁定徽标、右下角浮标一起收，留一个藏起来却还在管浮钮的主窗口最麻烦），
    ///     打开就把窗口显示回来。
    ///     <para>
    ///         窗口不销毁：作业板是常驻对象，来回拨开关不该每次都重建一遍视图与 ViewModel。
    ///     </para>
    /// </summary>
    private static void ApplyBoardEnabled(bool enabled)
    {
        // 引导正开着时不动主窗口：用户此刻是在「先选」而不是「已经在用」，
        // 中途拨一下开关就把主窗口铺出来，会盖在引导上面。
        if (_oobeWindow is { IsVisible: true })
            return;

        if (enabled)
        {
            // 只有「用户本来就该看到主窗口」时才自动叫回来：关掉「启动时显示主窗口」的人
            // 不想因为拨一下作业板开关就蹦出一个窗口。
            if (IAppHost.TryGetService<MainConfigHandler>() is { } handler
                && !handler.Data.Basic.ShowStartupWindow)
                return;

            ShowMainWindow();
            return;
        }

        _mainWindow?.HideForDock();
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

    /// <summary>
    ///     启动云同步：解析出服务（构造期会挂上配置与作业板的变化监听），再让它查一遍云端，
    ///     必要时自动拉取。网络与云端问题都不能拖住启动，失败只写日志。
    /// </summary>
    private static async Task InitializeCloudSyncAsync(Task accountSession)
    {
        try
        {
            await accountSession.ConfigureAwait(false);

            if (IAppHost.TryGetService<ICloudSyncService>() is { } cloud)
                await cloud.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            IAppHost.TryGetService<ILogger<App>>()?.LogWarning(exception, "云同步初始化失败。");
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

        // 界面字号：AppFontSize 供 Core 的 :is(Window) 样式直接继承，
        // ControlContentThemeFontSize 是 Fluent 控件模板取字号的键——只改窗口 FontSize
        // 管不到模板里写死的字号，两个一起写才能让标签、按钮、菜单跟着一起变。
        var uiFontSize = AppearanceSettingsConfig.ClampUiFontSize(settings.UiFontSize);
        Resources[@"AppFontSize"] = uiFontSize;
        Resources[@"ControlContentThemeFontSize"] = uiFontSize;
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

    /// <summary>
    ///     打开首次运行引导窗口。引导走完之后按配置决定要不要把主窗口显示出来。
    /// </summary>
    /// <param name="exitOnCancel">
    ///     点 × 并确认之后要不要把整个程序退掉。首次启动时传真（半套配置留在后台没有意义），
    ///     用户从设置里主动再跑一遍时传假（只关这个窗口，别把他正在做的事一起关掉）。
    /// </param>
    /// <remarks>
    ///     <para>
    ///         引导窗口**不交给生命周期**（不设 <c>desktop.MainWindow</c>）：它是一次性流程，
    ///         交给生命周期会让「关掉它」看起来像「关掉了程序」。进程靠
    ///         <see cref="ShutdownMode.OnExplicitShutdown" /> 常驻，退出由窗口自己走
    ///         <see cref="StopAsync" />。
    ///     </para>
    ///     <para>
    ///         用户从设置页点「重新运行首次设置」也会走这里：这时已经走过引导、也已经有主窗口了，
    ///         所以收尾时不能再建一个主窗口，只把原来那个叫回来。
    ///     </para>
    /// </remarks>
    public static void ShowOobeWindow(bool exitOnCancel = true)
    {
        if (_oobeWindow is { } existing)
        {
            ShowAndActivate(existing);
            return;
        }

        FirstRunOobeViewModel viewModel;
        try
        {
            viewModel = IAppHost.GetService<FirstRunOobeViewModel>();
        }
        catch (Exception exception)
        {
            // 取不到 VM 说明 DI 还没就绪（只可能发生在启动早期）：引导缺席不该让整个应用起不来，
            // 记一笔然后照常进主界面。
            IAppHost.TryGetService<ILogger<App>>()?.LogError(exception, "无法创建首次运行引导，跳过。");
            return;
        }

        var window = new FirstRunOobeWindow(viewModel, exitOnCancel);
        _oobeWindow = window;

        // 只有首次启动那次才把「走完 → 建主窗口」接下来：从设置页重跑时主窗口已经有了，
        // 再走一遍 OnOobeCompleted 会把它重新显示出来，跟用户当下的意图无关。
        if (exitOnCancel)
            window.SetupCompleted += (_, _) => OnOobeCompleted();

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_oobeWindow, window))
                _oobeWindow = null;
        };

        ShowAndActivate(window);
    }

    /// <summary>
    ///     引导收尾：关掉作业板时什么都不开（进程留在托盘里），否则把主窗口按「启动时显示」的
    ///     开关决定要不要显示。
    /// </summary>
    private static void OnOobeCompleted()
    {
        var handler = IAppHost.TryGetService<MainConfigHandler>();
        var basic = handler?.Data.Basic;
        var boardEnabled = handler?.Data.BoardSettings.BoardEnabled ?? true;

        if (!boardEnabled)
        {
            // 作业板关着：连窗口都不建——建了也没有可看的东西，用户要的是「只有托盘」。
            // 之后从托盘点「显示主窗口」时再按当时的配置现建。
            return;
        }

        if (basic is { ShowStartupWindow: false })
        {
            // 建好放字段里但不显示：主窗口是常驻对象，托盘一点就要立刻回来，
            // 那时再建会重新走一遍 ViewModel 装配。
            _mainWindow ??= CreateMainWindow(AppConsts.MainWindowScope);
            return;
        }

        ShowMainWindow();
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

    /// <summary>
    ///     作业板此刻在不在屏幕上。托盘菜单靠它决定那一项是「显示作业板」还是「隐藏作业板」。
    ///     <para>
    ///         收纳（<see cref="BoardDockService.IsDocked" />）、藏起来、最小化都算「不在」——
    ///         三种状态下用户看到的都是「没有板子」，菜单就不该再说「隐藏」。
    ///     </para>
    /// </summary>
    public static bool IsMainWindowShown
    {
        get
        {
            if (_mainWindow is not { IsVisible: true } window)
                return false;

            if (window.WindowState == WindowState.Minimized)
                return false;

            return IAppHost.TryGetService<BoardDockService>() is not { IsDocked: true };
        }
    }

    /// <summary>
    ///     把作业板从屏幕上收掉（托盘菜单的「隐藏作业板」）。
    ///     <para>
    ///         走 <see cref="MainWindow.HideForDock" />：窗口只是藏起来、不销毁，位置还在原处，
    ///         顺带把锁定徽标与右下角浮标一起收掉——只藏主窗口会把这两颗独立窗口留在屏幕上当孤儿。
    ///     </para>
    /// </summary>
    public static void HideMainWindow()
    {
        // 已经在收纳浮钮里了：板子本来就不在屏幕上，别再动它（否则会把浮钮的状态搅乱）。
        if (IAppHost.TryGetService<BoardDockService>() is { IsDocked: true })
            return;

        _mainWindow?.HideForDock();
    }

    /// <summary>
    ///     把作业板叫回来：托盘左键、托盘菜单「显示作业板」以及别处对主窗口的显示请求都走这里。
    ///     <para>
    ///         收纳（「设置 → 作业板 → 收纳」把板子收成屏幕角落一颗浮钮）状态下要**展开**板子：
    ///         先让 <see cref="BoardDockService.Expand" /> 收掉浮钮再显示窗口，
    ///         顺带重放置底 / 点击穿透 / 不透明度——隐藏会让平台摘掉这些样式，光 <see cref="Window.Show" />
    ///         是补不回来的。
    ///     </para>
    ///     <para>
    ///         没在收纳、只是窗口被藏起来（「启动时显示主窗口」关掉）时走 <see cref="MainWindow.ShowFromDock" />，
    ///         同样把窗口能力重放一遍。
    ///     </para>
    /// </summary>
    public static void ShowMainWindow()
    {
        var window = _mainWindow ??= CreateMainWindow(AppConsts.MainWindowScope);

        // 「启动时显示主窗口」关掉时没把它交给生命周期，这里补上，让主窗口仍是生命周期认定的主窗口。
        if (_desktopLifetime is { MainWindow: null } desktop)
            desktop.MainWindow = window;

        if (IAppHost.TryGetService<BoardDockService>() is { IsDocked: true } dock)
        {
            dock.Expand();

            // 浮钮那边拿不到主窗口（服务刚建出来、还没挂过窗口）时别把「显示」也一起吞掉。
            if (window.IsVisible)
                return;
        }

        if (window is MainWindow mainWindow)
        {
            mainWindow.ShowFromDock();
            return;
        }

        ShowAndActivate(window);
    }

    public static void ShowSettingsWindow()
    {
        ShowSettingsWindow(null);
    }

    /// <summary>
    ///     打开设置窗口，并（可选）直接翻到某一页。
    /// </summary>
    /// <param name="pageId">要打开的设置页 Id（如 <c>settings.about</c>）；为空就停在默认页。</param>
    /// <remarks>
    ///     窗口已经开着时也要翻页：托盘菜单的「关于」入口在设置窗口重复点击时必须能到达，
    ///     否则用户点第二次什么都没有发生。
    /// </remarks>
    public static void ShowSettingsWindow(string? pageId)
    {
        var window = _settingsWindow ??= CreateMainWindow(AppConsts.SettingsWindowScope);
        ShowAndActivate(window);

        if (!string.IsNullOrWhiteSpace(pageId) && window.Content is SettingsView view)
            view.NavigateToPage(pageId);
    }

    /// <summary>
    ///     打开全屏考试看板。看板是独立窗口（不进任务栏、无边框、整屏），
    ///     关掉之后再把字段清空，下次点入口重新建一个（里面挂着每秒的心跳与订阅）。
    /// </summary>
    public static void ShowExamBoardWindow()
    {
        if (_examBoardWindow is { } existing)
        {
            ShowAndActivate(existing);
            return;
        }

        var window = new ExamBoardWindow();
        _examBoardWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_examBoardWindow, window))
                _examBoardWindow = null;
        };

        ShowAndActivate(window);
    }

    /// <summary>
    ///     全屏考试看板此刻是不是开着。自动放映的看门狗靠它判断「该不该弹」「该不该收」——
    ///     用户自己开着的看板不能被当成自动放映的结果去关掉。
    /// </summary>
    public static bool IsExamBoardWindowOpen => _examBoardWindow is { IsVisible: true };

    /// <summary>
    ///     把全屏考试看板收起来（自动放映到点收工）。窗口本来就没开就什么都不做。
    ///     <para>
    ///         先摘掉字段再关窗：看门狗下一拍就要读 <see cref="IsExamBoardWindowOpen" />，
    ///         不等 <c>Closed</c> 事件走完。
    ///     </para>
    /// </summary>
    public static void CloseExamBoardWindow()
    {
        var window = _examBoardWindow;
        if (window is null)
            return;

        _examBoardWindow = null;
        window.Close();
    }

    /// <summary>
    ///     打开「编辑看板」窗口，编辑指定的那一块看板（<paramref name="boardId" /> 为 <see langword="null" /> 时编辑当前那块）。
    ///     窗口已经开着就换成新的一块、不再另开一个。
    /// </summary>
    public static void ShowExamBoardEditorWindow(Guid? boardId = null)
    {
        var examService = IAppHost.GetService<IExamService>();
        if (boardId is { } id)
            examService.SelectBoard(id);

        if (_examBoardEditorWindow is { } existing)
        {
            existing.ReloadBoard();
            ShowAndActivate(existing);
            return;
        }

        var window = new ExamBoardEditorWindow();
        _examBoardEditorWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_examBoardEditorWindow, window))
                _examBoardEditorWindow = null;
        };

        ShowAndActivate(window);
    }

    /// <summary>
    ///     打开「快速布置考试」向导。
    ///     <para>
    ///         <paramref name="mode" /> 决定这一趟是新建一块看板还是往当前看板追加。
    ///         向导一次只开一个：已经开着就把它拉到前面（它是模态流程，两三个并排跑没有意义）。
    ///     </para>
    /// </summary>
    public static void ShowExamPlanWizardWindow(ExamPlanWizardMode mode = ExamPlanWizardMode.CreateBoard)
    {
        if (_examPlanWizardWindow is { } existing)
        {
            ShowAndActivate(existing);
            return;
        }

        var viewModel = IAppHost.GetService<ExamPlanWizardViewModel>();
        viewModel.ModeOverride = mode;

        var window = new ExamPlanWizardWindow(viewModel);
        _examPlanWizardWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_examPlanWizardWindow, window))
                _examPlanWizardWindow = null;
        };

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
        await StopCoreAsync().ConfigureAwait(true);
        _desktopLifetime?.Shutdown();
    }

    /// <summary>
    ///     通过便携版 Launcher 重启到刚部署的新版本：先停 Host（释放文件与后台服务，
    ///     与上游 SecRandom-C 同序：Stop → Start Launcher → Shutdown），再拉起 Launcher，
    ///     由它从 <c>app-*</c> 目录里挑最新的一份启动，最后关掉当前进程。
    ///     安装版走系统安装程序，不会调用这里。
    /// </summary>
    public async Task RestartThroughLauncherAsync()
    {
        var launcherName = OperatingSystem.IsWindows() ? "CorkboardLauncher.exe" : "CorkboardLauncher";
        var launcherPath = Path.Combine(Utils.PackageRoot, launcherName);
        if (!File.Exists(launcherPath))
            throw new FileNotFoundException("便携版 Launcher 不存在。", launcherPath);

        await StopCoreAsync().ConfigureAwait(true);
        Process.Start(new ProcessStartInfo(launcherPath)
        {
            WorkingDirectory = Utils.PackageRoot,
            UseShellExecute = false
        });
        _desktopLifetime?.Shutdown();
    }

    /// <summary>停 Host 但不关生命周期；<see cref="StopAsync" /> 与 Launcher 重启共用。</summary>
    private async Task StopCoreAsync()
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
    }

    /// <summary>重启：直接拉起当前可执行文件后退出。便携版更新请用 <see cref="RestartThroughLauncherAsync" />。</summary>
    public void Restart()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return;

        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        _ = StopAsync();
    }
}
