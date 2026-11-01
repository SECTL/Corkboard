using Avalonia;
using Avalonia.Media;
using Corkboard.Core;
using Corkboard.Core.CrashRecovery;
using Corkboard.Platforms;
using Corkboard.Services.CrashRecovery;
using Corkboard.Services.Desktop;
using Corkboard.Shared;
#if CORKBOARD_PLATFORM_WINDOWS
using Corkboard.Platforms.Windows;
#elif CORKBOARD_PLATFORM_LINUX
using Corkboard.Platforms.Linux;
#elif CORKBOARD_PLATFORM_MACOS
using Corkboard.Platforms.MacOs;
#endif

namespace Corkboard.Desktop;

internal sealed class Program
{
    // 在 AppMain 之前不要使用 Avalonia 或需要同步上下文的 API：那时一切都还没初始化。
    [STAThread]
    public static void Main(string[] args)
    {
        // 版本元数据由头程序集承载，显式发布让解析路径与将来可能的移动头保持一致。
        GlobalConstants.SetVersionAssembly(typeof(Program).Assembly);

        // 数据根必须在第一条持久化路径被解析之前选定。
        Utils.PrepareDesktopDataRoot();

        // 崩溃恢复：先记住命令行里的 `--crash-report` / `--crash-auto-restart`。
        // 这一轮启动如果带着它们，App 会先弹崩溃窗口，正常启动路径根本不跑。
        CrashRecoveryRuntime.SetStartupArguments(args);

        // 单实例守门：已经有 Corkboard 在跑（典型场景是浏览器把 corkboard://display 交给系统启动了我们）
        // 就把命令行转给那一份，自己直接退出——不然点一次链接就多开一个实例、两份抢同一批数据文件。
        // 必须放在数据根之后：「是不是同一个实例」按数据根算哈希判，便携版与安装版互不干扰。
        // ⚠️ 崩溃重启那一轮必须**跳过**转发：那是旧进程已经让出单实例、专门起来显示崩溃窗口的一份，
        // 转发出去等于把崩溃窗口直接丢掉。
        if (CrashRecoveryRuntime.StartupPromptOptions is null && SingleInstanceGate.TryForwardToExistingInstance(args))
            return;

        ConfigurePlatformServices();

        // 启动期的最后一道兜底：Avalonia 的消息循环还没跑起来时，未处理异常只会走 AppDomain 这一条路。
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_OnUnhandledException;
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(
                CrashRecoveryPromptOptions.RemoveCrashRecoveryArguments(args));
        }
        catch (Exception exception)
        {
            // 启动期就炸了（Avalonia 初始化、首次建窗口、Host 装配……）：照崩溃恢复配置写报告并重启。
            if (!CrashRecoveryRuntime.TryHandleFatalException(exception, CrashRecoveryRuntime.StartLaunchPlan))
                throw;
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_OnUnhandledException;
        }
    }

    private static void CurrentDomain_OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            CrashRecoveryRuntime.TryHandleFatalException(exception, CrashRecoveryRuntime.StartLaunchPlan);
    }

    private static void ConfigurePlatformServices()
    {
#if CORKBOARD_PLATFORM_WINDOWS
        // Windows 头只能跑在 Windows 上，这个守卫同时让平台分析器认可下面的 Windows 专属调用。
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Windows desktop head must run on Windows.");

        WindowsTouchKeyboardIntegration.Initialize();
        PlatformStartupContext.Set(new WindowsPlatformServiceRoot());
#elif CORKBOARD_PLATFORM_LINUX
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The Linux desktop head must run on Linux.");

        PlatformStartupContext.Set(new LinuxPlatformServiceRoot());
#elif CORKBOARD_PLATFORM_MACOS
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("The macOS desktop head must run on macOS.");

        PlatformStartupContext.Set(new MacOsPlatformServiceRoot());
#else
        throw new PlatformNotSupportedException("No Corkboard desktop platform implementation was selected.");
#endif
    }

    // Avalonia 配置；视觉设计器也用它，不要删。
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                // 默认字体必须在这里定：只改 XAML 资源的话，首帧会先用系统字体渲染一次。
                DefaultFamilyName = GlobalConstants.DefaultFontFamily
            })
            .LogToTrace();
    }
}
