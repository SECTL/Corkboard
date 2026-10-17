using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;
using Corkboard.Shared;
using Corkboard.Shared.Abstraction;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Notes.Legacy;

/// <summary>
///     旧版作业数据的形状：<c>data/config/board.json</c> 里作业、类型、科目挤在一起。
///     只在一次性搬迁时读它，读不到就返回 <c>null</c>（不重写、不抛异常）。
///     <para>
///         里面的 <c>types</c> 与作业上的 <c>type_id</c>/<c>values</c> 都是已删除的旧功能留下的，
///     反序列化时静默忽略即可。
///     </para>
/// </summary>
public sealed class LegacyNoteFile
{
    public List<Note> Notes { get; set; } = [];

    public List<string> Subjects { get; set; } = [];

    /// <summary>旧文件路径。刻意只拼路径不建目录：只读探测不该有副作用。</summary>
    public static string FilePath => Path.Combine(Utils.DataRoot, "config", "board.json");

    public static LegacyNoteFile? TryRead(ILogger logger, string? filePath = null)
    {
        var path = filePath ?? FilePath;
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<LegacyNoteFile>(json, ConfigServiceBase.JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Unable to read the legacy board file: {Path}", path);
            return null;
        }
    }
}
