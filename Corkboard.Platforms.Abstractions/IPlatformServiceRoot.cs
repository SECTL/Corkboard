namespace Corkboard.Platforms.Abstractions;

public interface IPlatformServiceRoot
{
    PlatformKind Kind { get; }

    PlatformCapabilities Capabilities { get; }

    IWindowFeatureService WindowFeatures { get; }
}
