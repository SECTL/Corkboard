using System.Runtime.Versioning;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms.MacOs;

[SupportedOSPlatform("macos")]
public sealed class MacOsPlatformServiceRoot : IPlatformServiceRoot
{
    public PlatformKind Kind => PlatformKind.MacOs;

    public PlatformCapabilities Capabilities { get; } = new(
        Kind: PlatformKind.MacOs,
        SupportsSingleView: false,
        SupportsMultipleWindows: true,
        SupportsWindowPositioning: true,
        SupportsTopmost: true,
        SupportsTaskSwitcherExclusion: false,
        SupportsNoActivate: false,
        SupportsClickThrough: true,
        SupportsCaptureExclusion: false,
        SupportsTrayIcon: true,

        // NSStatusItem 挂上菜单后左右键都弹同一个菜单，应用不需要自己再弹一个。
        SupportsTrayMenuOnLeftClick: true,
        SupportsGlobalShortcuts: false,
        SupportsUrlSchemeRegistration: true,
        SupportsUiAccess: false,
        SupportsBackgroundResidency: true);

    public IWindowFeatureService WindowFeatures { get; } = new MacOsWindowFeatureService();

    public IAutostartService Autostart { get; } = new MacOsAutostartService();

    /// <summary>macOS 侧没有取全局指针的实现；托盘菜单用不到它（左键菜单由状态栏项自己弹）。</summary>
    public IPointerLocator Pointer { get; } = UnsupportedPointerLocator.Instance;
}
