namespace Corkboard.Services.Ipc;

/// <summary>ClassIsland IPC 的连接状态。</summary>
public enum IpcConnectionState
{
    /// <summary>未连接：ClassIsland 未运行、管道不可用，或正处于重试退避期。</summary>
    Disconnected,

    /// <summary>管道已连接，但 Corkboard4Ci 插件不可用（未安装、未启用或版本低于契约要求）。</summary>
    PluginUnavailable,

    /// <summary>管道与插件都可用。</summary>
    Available
}
