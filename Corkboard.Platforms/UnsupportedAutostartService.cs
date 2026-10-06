namespace Corkboard.Platforms;

using Corkboard.Platforms.Abstractions;

/// <summary>没有平台实现的兜底：不支持开机自启，设置页据此整张卡隐藏。</summary>
public sealed class UnsupportedAutostartService : IAutostartService
{
    public static UnsupportedAutostartService Instance { get; } = new();

    private UnsupportedAutostartService()
    {
    }

    public bool IsSupported => false;

    public bool TrySetEnabled(bool enabled, out string? error)
    {
        error = "The active platform does not implement autostart.";
        return false;
    }
}
