using System;

namespace Corkboard.Services.Ipc;

/// <summary>
/// 通知发送结果。失败原因与异常都会带回来，便于调用方记录日志或触发内置回退通知。
/// </summary>
/// <param name="Status">投递状态。</param>
/// <param name="Error">失败原因的人类可读描述；成功时为 <c>null</c>。</param>
/// <param name="Exception">底层异常（IPC 超时/连接中断等）；无异常时为 <c>null</c>。</param>
public readonly record struct NotificationSendResult(
    NotificationDeliveryStatus Status,
    string? Error = null,
    Exception? Exception = null)
{
    /// <summary>是否已成功投递。</summary>
    public bool IsSuccess => Status == NotificationDeliveryStatus.Delivered;

    /// <summary>投递成功的结果。</summary>
    public static NotificationSendResult Delivered { get; } = new(NotificationDeliveryStatus.Delivered);

    /// <summary>构造一个「通道不可用」的结果。</summary>
    public static NotificationSendResult Unavailable(string reason, Exception? exception = null)
        => new(NotificationDeliveryStatus.Unavailable, reason, exception);

    /// <summary>构造一个「IPC 调用超时」的结果。</summary>
    public static NotificationSendResult TimedOut(string reason, Exception? exception = null)
        => new(NotificationDeliveryStatus.TimedOut, reason, exception);

    /// <summary>构造一个「IPC 调用失败」的结果。</summary>
    public static NotificationSendResult Failed(string reason, Exception? exception = null)
        => new(NotificationDeliveryStatus.Failed, reason, exception);
}
