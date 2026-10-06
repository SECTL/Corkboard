using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms;

public sealed class PlatformServiceRootStub : IPlatformServiceRoot, IWindowFeatureService
{
    public static PlatformServiceRootStub Instance { get; } = new();

    private PlatformServiceRootStub()
    {
    }

    public PlatformKind Kind => PlatformKind.Unknown;

    public PlatformCapabilities Capabilities => PlatformCapabilities.Unsupported;

    public IWindowFeatureService WindowFeatures => this;

    public global::Corkboard.Platforms.Abstractions.WindowFeatures SupportedFeatures =>
        global::Corkboard.Platforms.Abstractions.WindowFeatures.None;

    public WindowFeatureApplyResult Apply(PlatformWindowHandle window, WindowFeatureRequest request) =>
        WindowFeatureApplyResult.Unsupported(request.Features, "The active platform does not support window features.");
}
