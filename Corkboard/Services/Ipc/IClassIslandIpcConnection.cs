using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Corkboard4Ci.Interface.Enums;
using Corkboard4Ci.Interface.Models;

namespace Corkboard.Services.Ipc;

/// <summary>
/// ClassIsland IPC 通知通道：连接可用性探测 / 发送通知 / 断开。
/// </summary>
/// <remarks>
/// 这是与业务无关的通用通知出口：不承载抽取、名单、课程表等语义，
/// 也不依赖任何业务服务。实现必须优雅降级——连接或投递失败只体现在返回值里，
/// 不在启动路径上抛致命异常。
/// </remarks>
public interface IClassIslandIpcConnection : IDisposable
{
    /// <summary>ClassIsland 的 IPC 管道是否已连上。</summary>
    bool IsConnected { get; }

    /// <summary>Corkboard4Ci 插件是否可用（管道已连接、自报存活且版本满足契约要求）。</summary>
    bool IsPluginAvailable { get; }

    /// <summary>最近一次成功探测到的插件版本；未探测到时为 <c>null</c>。</summary>
    Version? PluginVersion { get; }

    /// <summary>连接或插件可用性发生变化的通知（每次探测/重连成功后触发一次）。</summary>
    event EventHandler? StateChanged;

    /// <summary>连接可用性探测：必要时发起连接或重试，返回当前状态。</summary>
    /// <remarks>处于重试退避期内时立即返回，不会阻塞等待一整个超时窗口。</remarks>
    Task<IpcProbeResult> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送一条通知。失败时返回失败结果（含原因与异常），不抛异常、也不静默吞掉。
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> 被取消。</exception>
    Task<NotificationSendResult> SendNotificationAsync(
        NotificationData notification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送一条文本通知（标题 + 正文 + 时长 + 级别）的便捷重载。
    /// </summary>
    /// <param name="displayDuration">显示时长（秒），取值会被夹到 1~60。</param>
    /// <param name="items">可选的条目列表；与标题/正文同时使用时由接收端决定如何排版。</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> 被取消。</exception>
    Task<NotificationSendResult> SendNotificationAsync(
        string title,
        string body,
        double displayDuration = 5.0,
        NotificationLevel level = NotificationLevel.Unknown,
        string? boardId = null,
        IReadOnlyList<NotificationItem>? items = null,
        bool animation = true,
        CancellationToken cancellationToken = default);

    /// <summary>主动断开连接并释放 IPC 资源；之后仍可再次探测并重连。</summary>
    void Disconnect();
}
