using System.Runtime.Versioning;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms.Windows;

/// <summary>
///     Windows 平台根：窗口能力与开机自启都只在这个平台上可用，
///     调用方（入口层）已经用 <c>OperatingSystem.IsWindows()</c> 挡过一道。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformServiceRoot : IPlatformServiceRoot
{
    public WindowsPlatformServiceRoot()
    {
        WindowFeatures = new WindowsWindowFeatureService();
        Autostart = new WindowsAutostartService();
    }

    public PlatformKind Kind => PlatformKind.Windows;

    public PlatformCapabilities Capabilities { get; } = new(
        Kind: PlatformKind.Windows,
        SupportsSingleView: false,
        SupportsMultipleWindows: true,
        SupportsWindowPositioning: true,
        SupportsTopmost: true,
        SupportsTaskSwitcherExclusion: true,
        SupportsNoActivate: true,
        SupportsClickThrough: true,
        SupportsCaptureExclusion: OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041),
        SupportsTrayIcon: true,
        SupportsGlobalShortcuts: true,
        SupportsUrlSchemeRegistration: true,
        SupportsUiAccess: true,
        SupportsBackgroundResidency: true);

    public IWindowFeatureService WindowFeatures { get; }

    public IAutostartService Autostart { get; }
}
