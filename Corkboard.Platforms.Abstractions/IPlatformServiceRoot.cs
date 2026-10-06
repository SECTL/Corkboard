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
}
