using System;

namespace Corkboard.Services.Ipc;

/// <summary>一次连接可用性探测的结果。</summary>
/// <param name="State">当前连接状态。</param>
/// <param name="PluginVersion">Corkboard4Ci 插件的版本；未探测到时为 <c>null</c>。</param>
/// <param name="Error">不可用时的原因描述；可用时为 <c>null</c>。</param>
public readonly record struct IpcProbeResult(
    IpcConnectionState State,
    Version? PluginVersion = null,
    string? Error = null)
{
    /// <summary>管道与插件都可用。</summary>
    public bool IsAvailable => State == IpcConnectionState.Available;

    /// <summary>管道已连接（插件是否可用另见 <see cref="State"/>）。</summary>
    public bool IsConnected => State != IpcConnectionState.Disconnected;
}
