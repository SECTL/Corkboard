using Corkboard.Core.Services.Stats;

namespace Corkboard.Services.Stats;

/// <summary>
///     SECTL 平台统计的**唯一**出口：所有上报都走聚合入口（<c>POST /api/stats/report</c>），
///     一次请求可以混装在线状态、版本、用量三类事件。
/// </summary>
/// <remarks>
///     在线状态、版本、用量三个服务共用这一个单例，服务端限流（600 次/分钟）与超时口径只在这里定义一次。
/// </remarks>
public interface ISectlStatsReporter
{
    /// <summary>
    ///     把一批事件发出去。**不抛异常**（除调用方自己取消），失败只记日志并让返回项不达标。
    /// </summary>
    /// <returns>
    ///     与 <paramref name="events" /> **一一对应**、长度相同的结果列表：第 i 项说的是第 i 条事件的成败。
    ///     批量接口最外层固定回 <c>207</c>，所以必须按 <see cref="StatsReportResult.IsAccepted" /> 判，
    ///     不能按 HTTP 状态码判。
    /// </returns>
    Task<IReadOnlyList<StatsReportResult>> ReportAsync(
        IReadOnlyList<StatsReportEvent> events,
        CancellationToken cancellationToken = default);
}
