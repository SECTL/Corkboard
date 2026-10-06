namespace Corkboard.Services.Ipc;

/// <summary>通知投递结果状态。</summary>
public enum NotificationDeliveryStatus
{
    /// <summary>已由 Corkboard4Ci 插件接收。</summary>
    Delivered,

    /// <summary>通道不可用：ClassIsland 未运行、插件未安装/未启用/版本过低，或正处于重试退避期。</summary>
    Unavailable,

    /// <summary>IPC 调用超时（对端卡住）。</summary>
    TimedOut,

    /// <summary>IPC 调用失败（连接中断、代理异常等）。</summary>
    Failed
}
