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
using Avalonia.Threading;
using FluentAvalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Board;
using Corkboard.Core.Controls;
using Corkboard.Core.CrashRecovery;
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
using Corkboard.Core.Display;
using Corkboard.Core.Time;
using Corkboard.Platforms;
using Corkboard.Platforms.Abstractions;
using Corkboard.Services;
using Corkboard.Services.Widgets;
using Corkboard.Services.Auth;
using Corkboard.Services.Cloud;
using Corkboard.Services.CrashRecovery;
using Corkboard.Services.Desktop;
using Corkboard.Services.Exam;
using Corkboard.Services.Ipc;
using Corkboard.Services.Stats;
using Corkboard.Services.Display;
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
using Corkboard.ViewModels.SettingsPages.Display;
using Corkboard.ViewModels.DisplayBoard;
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
using Corkboard.Views.SettingsPages.Display;
using Corkboard.Views.SettingsPages.Update;
using Corkboard.Views.DisplayBoard;
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
    private static DisplayBoardWindow? _displayBoardWindow;
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

            // 运行期未处理异常：整个进程只挂这一处（Dispatcher 那条路能保住消息循环，
            // 崩溃窗口才画得出来；AppDomain 那条兜底在 Corkboard.Desktop/Program.cs 里）。
            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;

            // 崩溃恢复：这一轮进程是「上一次崩溃后重启起来的」，直接把崩溃窗口铺出来，
            // 正常启动路径（主窗口 / OOBE / 托盘 / 后台服务）一条都不跑。
            // 顺序与上游 SecRandom-C 一致：**Host 仍然要建**（崩溃窗口读配置、写日志都靠它），建失败也不拦着弹窗。
            if (CrashRecoveryRuntime.StartupPromptOptions is { } promptOptions)
            {
                try
                {
                    BuildHost(PlatformStartupContext.Current);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(exception);
                }

                desktop.MainWindow = ShowCrashRecoveryPromptOnly(promptOptions);
                base.OnFrameworkInitializationCompleted();
                return;
            }

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
                // 「启动时显示作业板」：关掉时**不把窗口交给生命周期**，Avalonia 就不会在 StartCore 里
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

        // 平台统计：在线状态（受隐私设置三态控制）、版本与使用量增量。三类上报全部走聚合入口
        // （POST /api/stats/report），共用同一个上报器（限流/超时口径只在那里定义一次）。
        services.AddSingleton<ISectlStatsReporter, SectlStatsReporter>();
        // 在线状态与使用量都要「后台跑 + 页面读缓存」，所以先注册单例、再把这个实例挂成托管服务，
        // 避免 DI 里出现两个互不相干的实例。
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

        // 考试文件的导入导出：一场考试存成 .cbexam，或者从这种文件读回来（格式在 Core 的 ExamEntryFile）。
        services.AddSingleton<ExamFileService>();

        // 收纳：把主窗口收成屏幕角落的一颗展开按钮，位置与大小来自「设置 → 作业板 → 收纳」。
        services.AddSingleton<BoardDockService>();

        // 作业板自动存档：板子改完等几秒把整块板面存成 PNG，按「设置 → 作业板 → 自动化与数据」的规则裁剪。
        services.AddSingleton<BoardAutoSaveService>();

        // 看板模式底部那一行一言 / 右上角那行天气：单例缓存（窗口开开关关共用一份），
        // 拉取时机由窗口决定，所以不挂后台轮询（见两个服务各自的注释）。
        services.AddSingleton<QuoteService>();
        services.AddSingleton<WeatherService>();

        // 作业自动清理：到点给过期作业打 CleanedAt 标记、从主页面上拿掉（规则与判定都在 Core 的 HomeworkCleanupService）。
        services.AddHostedService<HomeworkCleanupHostedService>();

        // 作业历史：把「历史与回放」的三项设置同步给存储层，并每小时按保留天数与容量上限清一次旧历史。
        services.AddHostedService<BoardHistoryHostedService>();

        // NTP 校时：按「设置 → 考试看板 → 时间同步」的间隔在后台对齐标准时间，看板时钟读它。
        services.AddHostedService<NtpSyncHostedService>();

        // 考试看板自动放映：到点自己把全屏看板弹出来，最后一场考完（延迟 N 分钟）再收回去。
        // 哪块看板参加由看板文件自己的 auto_show_enabled 决定，判定规则在 Core 的 ExamAutoShowPolicy。
        services.AddHostedService<ExamAutoShowHostedService>();

        // 看板模式自动进出：到点自己把全屏展示窗口铺出来，到点再收回去。
        // 只关「自动打开的那一次」，用户自己点开的不受 auto_hide 管；判定规则在 Core 的 DisplaySchedulePolicy。
        services.AddHostedService<DisplayAutoShowHostedService>();

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
        services.AddTransient<ExamFontSettingsPageViewModel>();
        services.AddTransient<ExamFloatingSettingsPageViewModel>();
        services.AddTransient<DisplayBoardViewModel>();
        services.AddTransient<DisplaySettingsPageViewModel>();
        services.AddTransient<DisplayWidgetsSettingsPageViewModel>();
        services.AddTransient<DisplayBackgroundSettingsPageViewModel>();

        // 主页面标题的默认文案；用户可以在作业板设置里改成别的名字。
        services.AddMainPage<BoardPage>(CR.Board_Title);

        // 顶层项顺序 = 注册顺序。「基础设置」之后紧跟「外观」，再往后才是「作业板」分组。
        // 侧边栏顶部不再放分隔线（原来的 AddSettingsPageSeparator 已按用户要求删掉）。
        services.AddSettingsPage<BasicSettingsPage>(CR.Settings_Basic_Title);
        services.AddSettingsPage<AppearanceSettingsPage>(CR.Settings_Appearance_Title);
        // 「看板模式」也是可折叠分组（用户要求的「侧边展开、下拉、再分类」）：一块全屏屏自己的显示与版式、
        // 背景、一言与天气、到点自己进出，四件事分四页，别挤在一页里。
        services.AddGroup(new PageGroupInfo(CR.Settings_Display_Group, "settings.display.group", FluentIcons.ClockFilled));
        services.AddSettingsPage<DisplaySettingsPage>(CR.Settings_Display_Content_Title);
        services.AddSettingsPage<DisplayBackgroundSettingsPage>(CR.Settings_Display_Background_Title);
        services.AddSettingsPage<DisplayWidgetsSettingsPage>(CR.Settings_Display_Widgets_Title);
        services.AddSettingsPage<DisplayScheduleSettingsPage>(CR.Settings_Display_Schedule_Title);

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

        // 「考试看板」是第二个可折叠分组，排在「作业板」下面：考试安排、看板显示、看板字号、时间同步、自动放映。
        services.AddGroup(new PageGroupInfo(CR.Settings_Exam_Group, "settings.exam.group", FluentIcons.CalendarFilled));
        services.AddSettingsPage<ExamScheduleSettingsPage>(CR.Settings_Exam_Schedule_Title);
        services.AddSettingsPage<ExamDisplaySettingsPage>(CR.Settings_Exam_Display_Title);
        // 「最小化与浮窗」紧跟在「看板显示」后面：两者都是「看板长什么样、怎么用」。
        services.AddSettingsPage<ExamFloatingSettingsPage>(CR.Settings_Exam_Floating_Title);
        // 「看板字号」单开一页：看板上每一处文字各有一个字号，塞进「看板显示」会把那一页撑到十几张卡。
        services.AddSettingsPage<ExamFontSettingsPage>(CR.Settings_Exam_Font_PageTitle);
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

            // 「用 corkboard:// 打开看板模式」同理：协议注册可能在别处被清掉了。
            EnsureUrlProtocolIntegration();

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

            // 同上：带 --open-exam-font 时直接落到「看板字号」页，方便对分组版式截图。
            if (args.Contains("--open-exam-font", StringComparer.OrdinalIgnoreCase))
                ShowSettingsWindow(AppConsts.SettingsExamFontPageId);

            // 同上：带 --open-exam-floating 时直接落到「最小化与浮窗」页，方便对设置卡截图。
            if (args.Contains("--open-exam-floating", StringComparer.OrdinalIgnoreCase))
                ShowSettingsWindow(AppConsts.SettingsExamFloatingPageId);

            // 同上：带 --open-exam-board 时直接铺开全屏考试看板，方便对版式截图。
            if (args.Contains("--open-exam-board", StringComparer.OrdinalIgnoreCase))
                ShowExamBoardWindow();

            // 同上：带 --open-oobe 时直接铺开首次运行引导，方便对版式截图（不改任何配置，
            // 退出时同正常引导一样走「没走完就退出」）。
            if (args.Contains("--open-oobe", StringComparer.OrdinalIgnoreCase))
                ShowOobeWindow();

            // 同上：带 --open-display-settings 时直接落到「看板模式」页，方便对设置卡版式截图。
            if (args.Contains("--open-display-settings", StringComparer.OrdinalIgnoreCase))
                ShowSettingsWindow(AppConsts.SettingsDisplayPageId);

            // 同上：带 --open-display 时直接铺开全屏看板模式，方便对版式截图。
            if (args.Contains("--open-display", StringComparer.OrdinalIgnoreCase))
                ShowDisplayBoardWindow();

            // 同上：带 --open-display-widgets 时直接落到「一言与天气」页（这一页曾经一点就崩）。
            if (args.Contains("--open-display-widgets", StringComparer.OrdinalIgnoreCase))
                ShowSettingsWindow(AppConsts.SettingsDisplayWidgetsPageId);

            // 同上：带 --open-display-background 时直接落到「背景」页，方便对背景编辑器截图。
            if (args.Contains("--open-display-background", StringComparer.OrdinalIgnoreCase))
                ShowSettingsWindow(AppConsts.SettingsDisplayBackgroundPageId);

            // 命令行里带 corkboard://display… 时按链接打开 / 关掉看板模式（协议唤起走这条路）。
            if (DisplayBoardUri.TryParseAny(args, out var displayCommand))
                ApplyDisplayCommand(displayCommand);

            // 单实例转发：第二份 Corkboard 被系统唤起时，命令行会被送到这里。
            // 先接上事件再倒一次队列——两件事之间到达的那一条不会丢。
            SingleInstanceGate.ArgumentsReceived += OnExternalArguments;
            foreach (var pending in SingleInstanceGate.DrainPendingArguments())
                HandleExternalArguments(pending);
        }
        catch (Exception exception)
        {
            IAppHost.TryGetService<ILogger<App>>()?.LogError(exception, "启动后台服务失败。");
        }
    }

    /// <summary>
    ///     别的实例把命令行转发过来了。这个回调在管道线程上，**必须先切到 UI 线程**再动窗口。
    /// </summary>
    private static void OnExternalArguments(IReadOnlyList<string> arguments) =>
        Dispatcher.UIThread.Post(() => HandleExternalArguments(arguments));

    /// <summary>
    ///     处理一份外部命令行（单实例转发过来的）。目前只认 <c>corkboard://</c>，
    ///     别的参数（<c>--open-*</c> 那些冒烟开关）只对自己启动的那一次有意义。
    /// </summary>
    private static void HandleExternalArguments(IReadOnlyList<string> arguments)
    {
        if (DisplayBoardUri.TryParseAny(arguments, out var command))
            ApplyDisplayCommand(command);
    }

    /// <summary>
    ///     按一条 <c>corkboard://</c> 指令打开 / 关掉全屏看板模式。
    ///     带 <c>from</c> / <c>to</c> 时那一段时刻只作用于这一次（窗口一关就作废）。
    /// </summary>
    private static void ApplyDisplayCommand(DisplayBoardCommand? command)
    {
        if (command is not { } parsed)
            return;

        if (parsed.Action == DisplayBoardAction.Close)
        {
            CloseDisplayBoardWindow();
            return;
        }

        DisplayScheduleOverride = parsed.Window;
        ShowDisplayBoardWindow();
    }

    /// <summary>
    ///     按配置重新写一遍 <c>corkboard://</c> 的协议注册。用户在系统里手动去掉之后，下次启动仍然恢复。
    ///     失败时把配置回滚并只写日志——协议注册是系统集成，不能把假成功留在配置里。
    /// </summary>
    private static void EnsureUrlProtocolIntegration()
    {
        var handler = IAppHost.TryGetService<MainConfigHandler>();
        var schemes = IAppHost.TryGetService<IUrlSchemeRegistrar>();
        if (handler is null || schemes is null || !schemes.IsSupported)
            return;

        var basic = handler.Data.Basic;
        if (basic.UrlProtocol
            && !schemes.TrySetEnabled(GlobalConstants.UrlProtocolScheme, true, out var error))
        {
            basic.UrlProtocol = false;
            handler.Save();
            IAppHost.TryGetService<ILogger<App>>()
                ?.LogWarning("无法恢复 corkboard:// 协议注册，已回滚：{Error}", error);
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
    ///     连同收纳浮钮的状态一起处理，留一个藏起来却还在管浮钮的主窗口最麻烦），
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
            // 只有「用户本来就该看到主窗口」时才自动叫回来：关掉「启动时显示作业板」的人
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
    ///         托盘菜单那一项同时改成「显示作业板」。
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
    ///         顺带重放置底 / 不透明度——隐藏会让平台摘掉这些样式，光 <see cref="Window.Show" />
    ///         是补不回来的。
    ///     </para>
    ///     <para>
    ///         没在收纳、只是窗口被藏起来（「启动时显示作业板」关掉）时走 <see cref="MainWindow.ShowFromDock" />，
    ///         同样把窗口能力重放一遍。
    ///     </para>
    /// </summary>
    public static void ShowMainWindow()
    {
        var window = _mainWindow ??= CreateMainWindow(AppConsts.MainWindowScope);

        // 「启动时显示作业板」关掉时没把它交给生命周期，这里补上，让主窗口仍是生命周期认定的主窗口。
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
            // 收起来的那份要先「还原」：只 Show 一个 Hide 过的窗口，
            // 屏幕上会既冒出看板、又留着那颗浮窗按钮。
            existing.RestoreFromMinimize();
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
    ///     <para>
    ///         ⚠️ **最小化也算开着**：收起来只是把窗口 Hide 掉、屏幕上留一颗浮窗按钮，
    ///         用户并没有退出看板。不算进来的话看门狗会把这一下当成「窗口已被用户关闭」，
    ///         当场结束放映；没有放映的那条路还会因为「看板没开」再弹一个回来。
    ///     </para>
    /// </summary>
    public static bool IsExamBoardWindowOpen =>
        _examBoardWindow is { IsVisible: true } || _examBoardWindow is ExamBoardWindow { IsMinimized: true };

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
    ///     打开全屏看板模式。窗口是独立窗口（不进任务栏、无边框、整屏），
    ///     关掉之后再把字段清空，下次点入口重新建一个（里面挂着每秒的心跳与订阅）。
    /// </summary>
    public static void ShowDisplayBoardWindow()
    {
        // 主动打开 / 新一轮自动打开：上一轮的「今天不再自动打开」与「延长」都不再有效。
        ClearDisplaySuppression();

        if (_displayBoardWindow is { } existing)
        {
            ShowAndActivate(existing);
            NotifyDisplayBoardMenuChanged();
            return;
        }

        var window = new DisplayBoardWindow();
        _displayBoardWindow = window;
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_displayBoardWindow, window))
                return;

            _displayBoardWindow = null;
            // 临时时段跟着这一扇窗走：窗关了，URI 里带来的 from/to 也就作废了。
            DisplayScheduleOverride = null;
            IsDisplayAutoShown = false;
            DisplayExtendedUntil = null;
            NotifyDisplayBoardMenuChanged();
        };

        ShowAndActivate(window);
        NotifyDisplayBoardMenuChanged();
    }

    /// <summary>
    ///     全屏看板模式此刻是不是开着。自动进出的看门狗靠它判断「该不该弹」「该不该收」——
    ///     用户自己开着的窗口不能被当成自动打开的结果去关掉。
    /// </summary>
    public static bool IsDisplayBoardWindowOpen => _displayBoardWindow is { IsVisible: true };

    /// <summary>
    ///     把全屏看板模式收起来（到点自动退出、设置页点「关闭全屏」、URI 的 <c>?exit=1</c>）。
    ///     窗口本来就没开就什么都不做。
    ///     <para>
    ///         先摘掉字段再关窗：看门狗下一拍就要读 <see cref="IsDisplayBoardWindowOpen" />，
    ///         不等 <c>Closed</c> 事件走完。
    ///     </para>
    /// </summary>
    public static void CloseDisplayBoardWindow()
    {
        var window = _displayBoardWindow;
        if (window is null)
            return;

        _displayBoardWindow = null;
        IsDisplayAutoShown = false;
        DisplayExtendedUntil = null;
        DisplayScheduleOverride = null;
        window.Close();
        NotifyDisplayBoardMenuChanged();
    }

    /// <summary>
    ///     这一次展示临时使用的时段（来自 <c>corkboard://display?from=19:00&amp;to=21:30</c>）。
    ///     <see langword="null" /> 表示按设置里的时刻来；窗口一关就清空。
    /// </summary>
    public static DisplayTimeWindow? DisplayScheduleOverride { get; set; }

    /// <summary>
    ///     看门狗自己弹出来的那一次（用户自己点开的是 <see langword="false" />）。
    ///     托盘据此决定「延长」这类动作有没有意义。
    /// </summary>
    public static bool IsDisplayAutoShown { get; set; }

    /// <summary>被手动关掉之后，到什么时候为止别再自动打开（看门狗关窗时记一笔）。</summary>
    public static DateTime? DisplaySuppressedUntil { get; set; }

    /// <summary>用户点了「今天不再自动打开」时记下的那一天。</summary>
    public static DateOnly? DisplaySuppressedDate { get; set; }

    /// <summary>「延长」延长到什么时候；<see langword="null" /> 表示没延长。</summary>
    public static DateTime? DisplayExtendedUntil { get; set; }

    /// <summary>现在是不是处在「别再自动打开」的抑制期里。</summary>
    public static bool IsDisplaySuppressed(DateTime now) =>
        (DisplaySuppressedUntil is { } until && now < until)
        || (DisplaySuppressedDate is { } date && date == DateOnly.FromDateTime(now));

    /// <summary>应用内的「现在」（有校时服务就用它，避免托盘与屏幕上的钟对不上）。</summary>
    private static DateTime DisplayNow =>
        IAppHost.TryGetService<NtpTimeService>()?.NowLocal ?? DateTime.Now;

    /// <summary>现在是不是处在抑制期里（给托盘 / 设置页用，不用自己找时间源）。</summary>
    public static bool IsDisplaySuppressed() => IsDisplaySuppressed(DisplayNow);

    /// <summary>今天剩下的时间都不再自动打开（托盘菜单那一项用）。</summary>
    public static void SuppressDisplayForToday() => SuppressDisplayForToday(DisplayNow);

    /// <summary>把当前这一轮自动放映往后延一段（托盘菜单那一项用）。</summary>
    public static void ExtendDisplaySession(TimeSpan extra) => ExtendDisplaySession(DisplayNow, extra);

    /// <summary>这一轮到点之前别再自动打开（手动关窗时由看门狗调用）。</summary>
    public static void SuppressDisplayUntil(DateTime until)
    {
        DisplaySuppressedUntil = until;
        DisplaySuppressedDate = null;
        NotifyDisplayBoardMenuChanged();
    }

    /// <summary>今天剩下的时间都不再自动打开。</summary>
    public static void SuppressDisplayForToday(DateTime now)
    {
        DisplaySuppressedDate = DateOnly.FromDateTime(now);
        DisplaySuppressedUntil = null;
        NotifyDisplayBoardMenuChanged();
    }

    /// <summary>把当前这一轮自动放映往后延一段（托盘 / 设置页的「延长」）。</summary>
    public static void ExtendDisplaySession(DateTime now, TimeSpan extra)
    {
        DisplayExtendedUntil = now + extra;
    }

    /// <summary>清掉抑制与延长（用户主动打开看板时调一次，别让上一轮的旧账挡住这一轮）。</summary>
    public static void ClearDisplaySuppression()
    {
        DisplaySuppressedUntil = null;
        DisplaySuppressedDate = null;
        DisplayExtendedUntil = null;
        NotifyDisplayBoardMenuChanged();
    }

    /// <summary>
    ///     看板模式的开合、抑制状态变了，托盘那张菜单要跟着重建：菜单是**快照**，
    ///     「延长 30 分钟」「关掉看板模式」「今天不再自动打开」这几项的显隐都按当时的
    ///     <see cref="IsDisplayBoardWindowOpen" /> / <see cref="IsDisplaySuppressed()" /> 算一次，
    ///     不重建就一直停在上次的样子（托盘模式本来就只在弹出那一刻读一遍）。
    /// </summary>
    private static void NotifyDisplayBoardMenuChanged() =>
        IAppHost.TryGetService<TaskBarIconService>()?.NotifyBoardVisibilityChanged();

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

    /// <summary>
    ///     Dispatcher 上的未处理异常：照崩溃恢复配置写报告、弹崩溃窗口或直接重启。
    ///     顺序与上游 SecRandom-C 一致——先把配置与日志落盘（崩溃窗口里复制出来的报告才有完整线索），
    ///     再交给 <see cref="DispatcherCrashRecovery" /> 决策，最后由它决定要不要把异常标成已处理。
    /// </summary>
    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            // 配置每次变更都会落盘，但内存里最后那几次改动可能还没写；崩溃重启后读的就是这份文件。
            IAppHost.TryGetService<MainConfigHandler>()?.Save();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        try
        {
            IAppHost.TryGetService<ILogger<App>>()?.LogCritical(e.Exception, "未处理的异常");
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        var handled = DispatcherCrashRecovery.TryRecover(
            e.Exception,
            CrashRecoveryRuntime.TryCreateCurrentProcessPromptOptions,
            ShowCrashRecoveryPrompt,
            exception => CrashRecoveryRuntime.TryHandlePromptDisplayFailure(exception,
                CrashRecoveryRuntime.StartLaunchPlan),
            exception => CrashRecoveryRuntime.TryHandleFatalException(exception,
                CrashRecoveryRuntime.StartLaunchPlan),
            RequestDesktopShutdown);

        e.Handled = handled;
    }

    /// <summary>崩溃后重启起来的那一轮：只把崩溃窗口交给生命周期，不当主窗口用（关掉即退出）。</summary>
    private static Window ShowCrashRecoveryPromptOnly(CrashRecoveryPromptOptions promptOptions)
    {
        var window = new CrashRecoveryWindow(promptOptions, RestartFromCrashRecoveryPrompt, canIgnore: false);
        window.Closed += (_, _) =>
        {
            if (!window.WasIgnored)
                RequestDesktopShutdown();
        };

        return window;
    }

    /// <summary>运行期弹崩溃窗口。返回 <c>true</c> 表示窗口已经显示出来（异常由这个窗口接管）。</summary>
    private static bool ShowCrashRecoveryPrompt(CrashRecoveryPromptOptions promptOptions)
    {
        var window = new CrashRecoveryWindow(promptOptions, RestartFromCrashRecoveryCurrentApp);
        window.Closed += (_, _) =>
        {
            if (!window.WasIgnored)
                RequestDesktopShutdown();
        };

        ShowAndActivate(window);
        return true;
    }

    /// <summary>崩溃提示窗口里点「重新启动」：拉起干净的一份，旧进程随后退出。</summary>
    private static bool RestartFromCrashRecoveryPrompt() => CrashRecoveryRuntime.StartLaunchPlan([]);

    /// <summary>运行期崩溃后点「重新启动」：与上面同一条路（都要先让出单实例）。</summary>
    private static bool RestartFromCrashRecoveryCurrentApp() => CrashRecoveryRuntime.StartLaunchPlan([]);

    /// <summary>请求退出进程（崩溃恢复用：可能在任意线程上调用）。</summary>
    private static void RequestDesktopShutdown()
    {
        var lifetime = _desktopLifetime;
        if (lifetime is null)
            return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            RequestDesktopShutdownCore(lifetime);
            return;
        }

        Dispatcher.UIThread.Post(() => RequestDesktopShutdownCore(lifetime));
    }

    private static void RequestDesktopShutdownCore(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        try
        {
            lifetime.Shutdown();
        }
        catch (Exception exception)
        {
            // 崩溃之后生命周期本身也可能已经坏了：最后手段是硬退出。
            Debug.WriteLine(exception);
            Environment.Exit(1);
        }
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
