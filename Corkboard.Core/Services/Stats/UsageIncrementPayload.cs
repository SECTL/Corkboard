using System.Text.Json.Serialization;

namespace Corkboard.Core.Services.Stats;

/// <summary>
///     使用量指标名，值就是 SECTL 统计 API（<c>POST /api/stats/usage/increment</c>）的
///     <c>field_key</c>。
/// </summary>
/// <remarks>
///     ⚠️ 服务端对每个平台有「已登记字段」白名单：没登记过的 <c>field_key</c> 会回
///     <c>200 {"status":"ignored","reason":"field_not_registered"}</c>（不报错、也不计数）。
///     所以这些键名必须与 SECTL 侧登记的一致；只允许追加，改名等于旧数据断档。
/// </remarks>
public static class UsageMetricNames
{
    /// <summary>一次应用启动。</summary>
    public const string AppLaunch = "daily_app_launch_count";

    /// <summary>新增一条作业（编辑不算）。</summary>
    public const string NoteAdd = "daily_note_add_count";

    /// <summary>成功导出一次（图片或表格，复制到剪贴板也算）。</summary>
    public const string Export = "daily_export_count";
}

/// <summary>
///     一次使用量增量上报。服务端只收增量并累加，客户端不需要保存总数；
///     失败时把增量放回本地待上报队列，下次一起重试。
/// </summary>
/// <remarks>
///     JSON 名是 API 的（<c>platform_id</c> / <c>field_key</c>），不是应用侧命名：
///     写错（例如把 <c>field_key</c> 写成 <c>field</c>）服务端会按缺字段拒绝，计数永远到不了。
/// </remarks>
public sealed record UsageIncrementPayload(
    [property: JsonPropertyName("platform_id")]
    string PlatformId,
    [property: JsonPropertyName("field_key")]
    string FieldKey,
    [property: JsonPropertyName("delta")]
    long Delta)
{
    /// <exception cref="ArgumentException">平台标识或指标名为空。</exception>
    /// <exception cref="ArgumentOutOfRangeException">增量不是正数。</exception>
    public static UsageIncrementPayload Create(string platformId, string fieldKey, long delta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(delta);

        return new UsageIncrementPayload(platformId.Trim(), fieldKey.Trim(), delta);
    }
}
