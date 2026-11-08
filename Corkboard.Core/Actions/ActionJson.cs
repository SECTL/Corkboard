using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Corkboard.Core.Actions;

/// <summary>
///     行动域磁盘格式的唯一一处定义。
///     <para>
///         与 <c>settings.json</c> 同一套口径：键名 snake_case、枚举写字符串（<c>"kind": "Weekly"</c>）、
///         中文不转义、<see langword="null" /> 字段省略。JSON 键是冻结契约，读旧文件写新文件都不能变。
///     </para>
///     <para>
///         <see cref="Options" /> 给人看的文件用（缩进）；<see cref="Compact" /> 给 <c>runs/*.jsonl</c> 用
///         —— 一行必须是一条记录，不能带换行。
///     </para>
/// </summary>
public static class ActionJson
{
    /// <summary>rules.json / state.json 等整份文件用（缩进，方便人直接改）。</summary>
    public static readonly JsonSerializerOptions Options = Create(writeIndented: true);

    /// <summary>jsonl 单行记录用（不缩进，一条一行）。</summary>
    public static readonly JsonSerializerOptions Compact = Create(writeIndented: false);

    private static JsonSerializerOptions Create(bool writeIndented) => new()
    {
        WriteIndented = writeIndented,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
}
