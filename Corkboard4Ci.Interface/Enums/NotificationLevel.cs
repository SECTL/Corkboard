namespace Corkboard4Ci.Interface.Enums;

/// <summary>
/// 通知的级别/阶段。用于让接收端区分「过程性通知」与「最终通知」，
/// 以及旧版调用方只提供内容、不区分阶段的情形。
/// </summary>
/// <remarks>
/// 与上游参考实现的 <c>ResultType</c> 的对应关系：
/// <c>Unknown</c>、<c>Legacy</c> 原样保留；上游的点名/闪抽/抽奖三组「进行中」取值
/// （<c>PartialRollCall</c> / <c>PartialQuickDraw</c> / <c>PartialLottery</c>）统一收敛为
/// <see cref="Partial"/>；对应的三组「已完成」取值统一收敛为 <see cref="Final"/>。
/// 抽取领域的三分类属于业务语义，不进入本通用通道。
/// </remarks>
public enum NotificationLevel
{
    /// <summary>未指定。</summary>
    Unknown = 0,

    /// <summary>旧版调用方：只带内容，不区分过程与最终。</summary>
    Legacy = 1,

    /// <summary>过程性通知：内容会被同一看板的后续通知替换（预览/动画阶段）。</summary>
    Partial = 2,

    /// <summary>最终通知：内容为该次通知的最终结果。</summary>
    Final = 3
}
