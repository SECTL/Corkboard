namespace Corkboard.Platforms;

using Corkboard.Platforms.Abstractions;

/// <summary>没有平台实现的兜底：不支持自定义 URL 协议注册，设置页据此整张卡隐藏。</summary>
public sealed class UnsupportedUrlSchemeRegistrar : IUrlSchemeRegistrar
{
    public static UnsupportedUrlSchemeRegistrar Instance { get; } = new();

    private UnsupportedUrlSchemeRegistrar()
    {
    }

    public bool IsSupported => false;

    public bool TrySetEnabled(string scheme, bool enabled, out string? error)
    {
        error = "The active platform does not implement URL scheme registration.";
        return false;
    }
}
