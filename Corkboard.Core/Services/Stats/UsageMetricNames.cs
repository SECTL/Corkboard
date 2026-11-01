namespace Corkboard.Core.Services.Stats;

/// <summary>
///     使用量指标名，值就是聚合上报道具（<c>POST /api/stats/report</c>）里
///     <c>metrics</c> 容器的键名。
/// </summary>
/// <remarks>
///     ⚠️ 服务端对每个平台有「已登记字段」白名单：没登记过的指标名会回
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
