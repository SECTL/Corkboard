using System.Globalization;
using System.Text.Json.Nodes;
using Corkboard.Core.Exam;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     把考试看板做成一份只读快照，跟作业快照一起送进集控（同一个 <c>examBoard</c> 字段）。
///     <para>
///         只送「最近若干场」：按开考时间倒序取前 <c>recentCount</c> 场，已经考完超过
///         <see cref="FinishedVisibilityGrace" /> 的不再送（网页上看板只关心还要考的与刚考完的）。
///         这样即使板子上积累了几百场历史，上传的载荷也不会跟着膨胀。
///     </para>
/// </summary>
public static class ControlExamBoardSnapshot
{
    /// <summary>单份快照最多带多少场考试（与设置上限一致）。</summary>
    public const int MaxExams = 50;

    /// <summary>备注最多带多少字符。</summary>
    public const int MaxNoteLength = 200;

    /// <summary>
    ///     考完多久之后就不再出现在快照里（刚考完的留着给老师核对，超过一天就只留在本机）。
    /// </summary>
    public static readonly TimeSpan FinishedVisibilityGrace = TimeSpan.FromHours(24);

    /// <summary>
    ///     生成考试看板快照。
    /// </summary>
    /// <param name="board">当前看板（取标题与副标题），可空。</param>
    /// <param name="exams">看板里的全部考试。</param>
    /// <param name="now">生成时刻（状态与「最近」都按它算）。</param>
    /// <param name="recentCount">最多带多少场（1–<see cref="MaxExams" />）。</param>
    public static JsonObject Build(
        ExamBoardDocument? board,
        IEnumerable<ExamEntry> exams,
        DateTimeOffset now,
        int recentCount)
    {
        var take = Math.Clamp(recentCount, 1, MaxExams);
        var localNow = now.LocalDateTime;
        var visibleUntil = localNow - FinishedVisibilityGrace;

        var ordered = exams
            .Where(exam => !string.IsNullOrWhiteSpace(exam.Subject))
            .OrderByDescending(exam => exam.StartAt)
            .ToList();

        var visible = ordered.Where(exam => exam.EndAt >= visibleUntil).ToList();
        var droppedFinished = ordered.Count - visible.Count;
        var picked = visible.Take(take).ToList();
        var truncated = visible.Count > picked.Count;

        var payloadExams = new JsonArray();
        foreach (var exam in picked)
        {
            payloadExams.Add(new JsonObject
            {
                ["id"] = exam.Id.ToString("D"),
                ["subject"] = exam.Subject.Trim(),
                ["date"] = exam.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["start"] = exam.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                ["end"] = exam.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                ["paperCount"] = exam.PaperCount,
                ["pageCount"] = exam.PageCount,
                ["questionCount"] = exam.QuestionCount,
                ["note"] = NormalizeNote(exam.Note)
            });
        }

        return new JsonObject
        {
            ["generatedAt"] = now.ToString("O", CultureInfo.InvariantCulture),
            ["title"] = board?.ResolveTitle() ?? string.Empty,
            ["subtitle"] = board?.ResolveSubtitle() ?? string.Empty,
            ["recentCount"] = take,
            ["droppedFinished"] = droppedFinished,
            ["truncated"] = truncated,
            ["examCount"] = picked.Count,
            ["exams"] = payloadExams
        };
    }

    /// <summary>
    ///     挑出「已经考完足够久、可以从看板删掉」的考试 Id。
    ///     <para>
    ///         由托管服务在自动清理开关打开时调用；只给 Id，真正删由调用方经
    ///         <c>IExamService.Remove</c> 做（那边才会落盘与通知界面）。
    ///     </para>
    /// </summary>
    /// <param name="exams">看板里的全部考试。</param>
    /// <param name="now">当前时刻。</param>
    /// <param name="graceDays">考完多少天之后删（至少 1 天）。</param>
    public static IReadOnlyList<Guid> SelectFinishedForRemoval(
        IEnumerable<ExamEntry> exams,
        DateTimeOffset now,
        int graceDays)
    {
        var grace = TimeSpan.FromDays(Math.Max(1, graceDays));
        var cutoff = now.LocalDateTime - grace;

        return exams
            .Where(exam => !string.IsNullOrWhiteSpace(exam.Subject) && exam.EndAt < cutoff)
            .Select(exam => exam.Id)
            .ToList();
    }

    private static string? NormalizeNote(string? note)
    {
        var text = note?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        return text.Length <= MaxNoteLength
            ? text
            : string.Concat(text.AsSpan(0, MaxNoteLength), "…");
    }
}