using System.Text.Json;
using System.Text.Json.Serialization;

namespace Corkboard.Core.Services.Stats;

/// <summary>
///     聚合上报入口（<c>POST /api/stats/report</c>）的**批量**请求体：<c>{"events":[…]}</c>。
/// </summary>
/// <remarks>
///     <para>
///         一次请求里可以混装在线状态、版本、用量三类事件——服务端逐条按字段判定类型，
///         这样「一次心跳 + 一次版本 + 几项用量」只需要一个往返，而旧端点要发好几个。
///     </para>
///     <para>
///         ⚠️ 批量请求**最外层固定回 <c>207</c>**（哪怕全部成功），单条成败只能看
///         <c>results[i]</c>；所以调用方必须按 <see cref="StatsReportResult.IsAccepted" /> 判，
///         不能按 HTTP 状态码判，也不能把 207 当失败整批重发。
///     </para>
/// </remarks>
public sealed record StatsReportBatch(
    [property: JsonPropertyName("events")]
    IReadOnlyList<StatsReportEvent> Events)
{
    /// <summary>包装一批事件；空数组会被服务端以 <c>400</c> 拒绝，所以这里先挡住。</summary>
    /// <exception cref="ArgumentException">事件列表为空，或里面有 <see langword="null" /> 元素。</exception>
    public static StatsReportBatch Create(IReadOnlyList<StatsReportEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            throw new ArgumentException(@"events must be a non-empty array.", nameof(events));
        if (events.Any(item => item is null))
            throw new ArgumentException(@"events must not contain null items.", nameof(events));

        return new StatsReportBatch(events);
    }
}

/// <summary>
///     聚合上报的响应体。批量形态（<c>207</c>）带 <c>results[]</c>；单条形态（<c>200</c> / <c>201</c>）
///     把结果摊在顶层，两者都要认。
/// </summary>
public sealed record StatsReportResponse(
    [property: JsonPropertyName("success")]
    bool? Success,
    [property: JsonPropertyName("events")]
    int? EventCount,
    [property: JsonPropertyName("results")]
    IReadOnlyList<StatsReportResult>? Results,
    [property: JsonPropertyName("error")]
    string? Error,
    [property: JsonPropertyName("error_description")]
    string? ErrorDescription,
    [property: JsonPropertyName("retry_after_ms")]
    int? RetryAfterMs,
    [property: JsonPropertyName("online_count")]
    int? OnlineCount)
{
    /// <summary>限流错误码（服务端回 <c>429</c>，并带 <see cref="RetryAfterMs" />）。</summary>
    public const string RateLimitedErrorCode = "rate_limited";

    /// <summary>是否被限流（这一批整批没落库，可以过一会儿原样重发）。</summary>
    public bool IsRateLimited =>
        string.Equals(Error, RateLimitedErrorCode, StringComparison.Ordinal);
}

/// <summary>
///     批量响应里**一条**事件的结果：<c>{ matched, status, body }</c>。
/// </summary>
/// <remarks>
///     <c>results[i]</c> 与请求里的 <c>events[i]</c> 一一对应（服务端按顺序逐条执行），
///     所以调用方可以按序号把失败的那几条挑出来重发。
/// </remarks>
public sealed record StatsReportResult(
    [property: JsonPropertyName("matched")]
    string? Matched,
    [property: JsonPropertyName("status")]
    int? Status,
    [property: JsonPropertyName("body")]
    JsonElement? Body)
{
    /// <summary>这一条是否真的落库了（<c>status</c> 在 2xx 内）。</summary>
    public bool IsAccepted => Status is >= 200 and < 300;

    /// <summary>服务端把这条判定成了什么类型（判不出来时是 <see cref="StatsReportKind.Unknown" />）。</summary>
    public StatsReportKind MatchedKind => Matched switch
    {
        "online" => StatsReportKind.Online,
        "version" => StatsReportKind.Version,
        "usage" => StatsReportKind.Usage,
        _ => StatsReportKind.Unknown
    };

    /// <summary>
    ///     从这条事件的响应体里取在线人数：上报（<c>POST</c>）给顶层 <c>online_count</c>，
    ///     查询（<c>GET /api/stats/platform/{id}</c>）放在 <c>online.online_count</c> 里。
    /// </summary>
    public int? ResolveOnlineCount()
    {
        if (Body is not { ValueKind: JsonValueKind.Object } body)
            return null;

        if (body.TryGetProperty("online_count", out var flat) && flat.TryGetInt32(out var count))
            return Math.Max(0, count);

        if (body.TryGetProperty("online", out var online)
            && online.ValueKind == JsonValueKind.Object
            && online.TryGetProperty("online_count", out var nested)
            && nested.TryGetInt32(out var nestedCount))
            return Math.Max(0, nestedCount);

        return null;
    }
}
