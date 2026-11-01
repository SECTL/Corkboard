using System.Runtime.Versioning;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms.Linux;

[SupportedOSPlatform("linux")]
public sealed class LinuxPlatformServiceRoot : IPlatformServiceRoot
{
    private static readonly bool IsX11Session = !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("DISPLAY"));

    public PlatformKind Kind => PlatformKind.Linux;

    public PlatformCapabilities Capabilities { get; } = new(
        Kind: PlatformKind.Linux,
        SupportsSingleView: false,
        SupportsMultipleWindows: true,
        SupportsWindowPositioning: false,
        SupportsTopmost: IsX11Session,
        SupportsTaskSwitcherExclusion: IsX11Session,
        SupportsNoActivate: false,
        SupportsCaptureExclusion: false,
        SupportsTrayIcon: true,

        // SNI / AppIndicator 宿主本身就是「左键即菜单」，不需要应用自己再弹一个。
        SupportsTrayMenuOnLeftClick: true,
        SupportsGlobalShortcuts: false,
        SupportsUrlSchemeRegistration: true,
        SupportsUiAccess: false,
        SupportsBackgroundResidency: true);

    public IWindowFeatureService WindowFeatures { get; } = new LinuxWindowFeatureService();

    public IAutostartService Autostart { get; } = new LinuxAutostartService();

    /// <summary>Linux 侧没有取全局指针的实现；托盘菜单用不到它（左键菜单由托盘宿主自己弹）。</summary>
    public IPointerLocator Pointer { get; } = UnsupportedPointerLocator.Instance;

    /// <summary>写桌面项 + <c>xdg-mime</c> 设默认处理程序，都是用户级操作，不需要 root。</summary>
    public IUrlSchemeRegistrar UrlSchemes { get; } = new LinuxUrlSchemeRegistrar();
}
