namespace Corkboard.Platforms.Abstractions;

/// <summary>平台能力表：每个平台根按自己的实际实现申报，界面据此决定显隐。</summary>
/// <param name="SupportsTrayMenuOnLeftClick">
///     原生托盘的<b>左键</b>是否本来就弹菜单。macOS 的状态栏项与 Linux 的 SNI/AppIndicator
///     宿主都是「左键即菜单」，而 Windows 的托盘只把左键当一次点击（<c>TrayIcon.Clicked</c>）、
///     菜单只给右键——所以这个位为 <see langword="false" /> 的平台，应用层要自己在左键上
///     画一个菜单弹出来（见 <c>TrayMenuWindow</c>）。
/// </param>
public sealed record PlatformCapabilities(
    PlatformKind Kind,
    bool SupportsSingleView,
    bool SupportsMultipleWindows,
    bool SupportsWindowPositioning,
    bool SupportsTopmost,
    bool SupportsTaskSwitcherExclusion,
    bool SupportsNoActivate,
    bool SupportsClickThrough,
    bool SupportsCaptureExclusion,
    bool SupportsTrayIcon,
    bool SupportsTrayMenuOnLeftClick,
    bool SupportsGlobalShortcuts,
    bool SupportsUrlSchemeRegistration,
    bool SupportsUiAccess,
    bool SupportsBackgroundResidency)
{
    public static PlatformCapabilities Unsupported { get; } = new(
        PlatformKind.Unknown,
        SupportsSingleView: false,
        SupportsMultipleWindows: false,
        SupportsWindowPositioning: false,
        SupportsTopmost: false,
        SupportsTaskSwitcherExclusion: false,
        SupportsNoActivate: false,
        SupportsClickThrough: false,
        SupportsCaptureExclusion: false,
        SupportsTrayIcon: false,
        SupportsTrayMenuOnLeftClick: false,
        SupportsGlobalShortcuts: false,
        SupportsUrlSchemeRegistration: false,
        SupportsUiAccess: false,
        SupportsBackgroundResidency: false);
}
