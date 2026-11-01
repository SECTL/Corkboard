using System.Globalization;
using System.IO;
using System.Text.Json;
using Corkboard.Core.Abstraction;

namespace Corkboard.Core.Exam;

/// <summary>
///     一场（或几场）考试导出成的小文件：<c>.cbexam</c>，内容是一段 JSON。
///     <para>
///         为什么还要一层外壳、而不是直接把 <see cref="ExamEntry" /> 序列化出去：文件要能被认出来。
///         外壳里带 <see cref="Format" /> 与 <see cref="Version" />，以后字段变了也能按版本兼容，
///         顺手把「哪块看板导出来的、什么时候导的」一起记上，用户翻文件夹时认得出。
///     </para>
///     <para>
///         外壳里装的是**一串**考试（不是一场）：单场导出就是只有一条的清单，导入时也就能
///         一次吃下别人手改过的多场文件，不用为「单场 / 多场」做两套格式。
///     </para>
/// </summary>
public sealed class ExamEntryFile
{
    /// <summary>文件里的格式标记，认文件用。</summary>
    public const string FormatName = "corkboard-exam";

    /// <summary>当前版本。以后字段有不兼容的变化时加一，读盘时按版本走不同分支。</summary>
    public const int CurrentVersion = 1;

    /// <summary>扩展名（不带点）。</summary>
    public const string FileExtension = "cbexam";

    /// <summary>格式标记。读到不是这个值的文件一律当「不是考试文件」。</summary>
    public string Format { get; set; } = FormatName;

    /// <summary>格式版本。</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>导出时刻（本地时间），只用于展示，导入时不参与任何判定。</summary>
    public DateTime ExportedAt { get; set; }

    /// <summary>从哪块看板导出来的（看板标题），可能为空。</summary>
    public string? BoardTitle { get; set; }

    /// <summary>全部考试。</summary>
    public List<ExamEntry> Exams { get; set; } = [];

    /// <summary>把一批考试装成一份文件（顺序按日期 + 开考时刻排，方便人看）。</summary>
    public static ExamEntryFile Create(IEnumerable<ExamEntry>? exams, string? boardTitle, DateTime exportedAt) => new()
    {
        Format = FormatName,
        Version = CurrentVersion,
        ExportedAt = exportedAt,
        BoardTitle = string.IsNullOrWhiteSpace(boardTitle) ? null : boardTitle.Trim(),
        Exams =
        [
            .. (exams ?? [])
                .Where(exam => exam is not null)
                .OrderBy(exam => exam.Date)
                .ThenBy(exam => exam.StartTime)
                .Select(exam => exam.Clone())
        ]
    };

    /// <summary>写成 JSON 文本（缩进、snake_case，与配置文件同一套选项，人也能直接改）。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, ConfigServiceBase.JsonOptions);

    /// <summary>
    ///     读一段 JSON。
    ///     <para>
    ///         容错到「手改过也认」为止：除了标准外壳，还接受裸数组（<c>[ {...} ]</c>）
    ///         与单场裸对象（<c>{ "subject": "语文", ... }</c>）——别人用记事本拼一份也能导进来。
    ///         认不出来（不是 JSON、不是考试文件、没有考试）时返回 <see langword="false" />。
    ///     </para>
    /// </summary>
    public static bool TryParse(string? json, out ExamEntryFile? file)
    {
        file = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // 裸数组：当成「只有考试清单」的外壳。
            if (root.ValueKind == JsonValueKind.Array)
            {
                var list = JsonSerializer.Deserialize<List<ExamEntry>>(json, ConfigServiceBase.JsonOptions);
                if (list is null || list.Count == 0)
                    return false;

                file = new ExamEntryFile { Exams = list };
                return true;
            }

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            // 外壳：带 exams 字段。没带格式标记的也放行（手拼的文件不会写 format）。
            if (root.TryGetProperty("exams", out _))
            {
                var parsed = JsonSerializer.Deserialize<ExamEntryFile>(json, ConfigServiceBase.JsonOptions);
                if (parsed is null || parsed.Exams is null || parsed.Exams.Count == 0)
                    return false;

                file = parsed;
                return true;
            }

            // 单场裸对象：能反序列化出一门有名字的科目就认。
            var single = JsonSerializer.Deserialize<ExamEntry>(json, ConfigServiceBase.JsonOptions);
            if (single is null || string.IsNullOrWhiteSpace(single.Subject))
                return false;

            file = new ExamEntryFile { Exams = [single] };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    ///     收拾成「能直接存进看板」的考试：逐条过 <see cref="ExamEntry.Normalize" />，
    ///     丢掉没有科目的那些（看板里本来就不许存空科目）。
    /// </summary>
    public IReadOnlyList<ExamEntry> ResolveImportableExams()
    {
        var result = new List<ExamEntry>();
        foreach (var exam in Exams ?? [])
        {
            if (exam is null)
                continue;

            var normalized = ExamEntry.Normalize(exam);
            if (normalized.Subject.Length == 0)
                continue;

            // Id 留着也没关系：AddMany 撞到同一个 Id 会自动换一个，不会覆盖已有的考试。
            result.Add(normalized);
        }

        return result;
    }

    /// <summary>
    ///     保存对话框里的默认文件名：<c>语文_2026-06-07.cbexam</c>。
    ///     科目里的非法字符换成下划线——文件名是给资源管理器用的，不能带 <c>: ? *</c> 这些。
    /// </summary>
    public static string BuildSuggestedFileName(ExamEntry exam)
    {
        ArgumentNullException.ThrowIfNull(exam);

        var subject = SanitizeFileNamePart(exam.Subject);
        if (subject.Length == 0)
            subject = "exam";

        var date = exam.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"{subject}_{date}.{FileExtension}";
    }

    /// <summary>
    ///     整块看板导出时的默认文件名：<c>期末考试_2026-06-07.cbexam</c>。
    ///     日子取**第一场**考试那天（没有考试就用今天），一眼看得出这份文件是哪一场考试的排期。
    /// </summary>
    public static string BuildSuggestedFileName(ExamBoardDocument board)
    {
        ArgumentNullException.ThrowIfNull(board);

        var title = SanitizeFileNamePart(board.ResolveTitle());
        if (title.Length == 0)
            title = "exam";

        var first = board.Exams?
            .Where(exam => exam is not null)
            .OrderBy(exam => exam.Date)
            .ThenBy(exam => exam.StartTime)
            .FirstOrDefault();

        var date = (first is null ? DateOnly.FromDateTime(DateTime.Now) : first.Date)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"{title}_{date}.{FileExtension}";
    }

    /// <summary>把一段文字收拾成能进文件名的样子：非法字符换下划线、收掉首尾的空白与点。</summary>
    private static string SanitizeFileNamePart(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);

        return builder.ToString().Trim().Trim('.');
    }
}
