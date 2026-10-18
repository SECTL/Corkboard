using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Services.Stats;

/// <summary>在线状态上报的纯规则：哪些模式要上报、模式值怎么归一。</summary>
public static class OnlineStatusPolicy
{
    /// <summary>
    ///     是否允许上报在线状态。<see cref="OnlineStatusMode.Off" /> 时完全不动网络；
    ///     <see cref="OnlineStatusMode.Full" /> 与 <see cref="OnlineStatusMode.Anonymous" /> 都上报。
    /// </summary>
    public static bool ShouldReport(OnlineStatusMode mode) => Normalize(mode) != OnlineStatusMode.Off;

    /// <summary>未知枚举值收回默认的 <see cref="OnlineStatusMode.Full" />（手改配置的兜底）。</summary>
    public static OnlineStatusMode Normalize(OnlineStatusMode mode) =>
        Enum.IsDefined(mode) ? mode : OnlineStatusMode.Full;
}
