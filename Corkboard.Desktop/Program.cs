using Avalonia;
using Avalonia.Media;
using Corkboard.Core;
using Corkboard.Platforms;
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

        ConfigurePlatformServices();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
