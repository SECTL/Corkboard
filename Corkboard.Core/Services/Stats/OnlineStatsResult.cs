using System.Text.Json.Serialization;

namespace Corkboard.Core.Services.Stats;

/// <summary>
///     在线人数**查询**接口（<c>GET /api/stats/platform/{id}</c>）的响应：人数放在
///     <c>online.online_count</c> 里。上报走聚合入口（见 <see cref="StatsReportBatch" />），
///     那条路径的人数在 <see cref="StatsReportResult.ResolveOnlineCount" /> 里读。
/// </summary>
public sealed record OnlineStatsResult(
    [property: JsonPropertyName("success")]
    bool? Success,
    [property: JsonPropertyName("online_count")]
    int? OnlineCount,
    [property: JsonPropertyName("error")]
    string? Error,
    [property: JsonPropertyName("online")]
    OnlineStatsSnapshot? Online)
{
    /// <summary>把在线人数抬成非负值；没有可用数字时返回 <see langword="null" />。</summary>
    public int? ResolveOnlineCount()
    {
        var count = OnlineCount ?? Online?.OnlineCount;
        return count is { } value ? Math.Max(0, value) : null;
    }
}

/// <summary>平台统计响应里 <c>online</c> 那一段（<c>online_count</c> 等）。</summary>
public sealed record OnlineStatsSnapshot(
    [property: JsonPropertyName("online_count")]
    int? OnlineCount);
