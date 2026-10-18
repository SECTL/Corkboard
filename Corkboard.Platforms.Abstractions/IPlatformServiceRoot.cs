namespace Corkboard.Platforms.Abstractions;

public interface IPlatformServiceRoot
{
    PlatformKind Kind { get; }

    PlatformCapabilities Capabilities { get; }

    IWindowFeatureService WindowFeatures { get; }

    /// <summary>
    ///     开机自启实现。没有实现开机自启的平台返回不支持的空实现，
    ///     设置页据此整张卡隐藏（不留一个点不动的开关）。
    /// </summary>
    IAutostartService Autostart { get; }

    /// <summary>
    ///     指针定位实现。拿不到全局指针位置的平台返回不支持的空实现，
    ///     调用方退回兜底位置（托盘菜单不能因此打不开）。
    /// </summary>
    IPointerLocator Pointer { get; }
}
