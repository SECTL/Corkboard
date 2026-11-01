using Corkboard.Core.Exam;
using Corkboard.Core.Langs.Common;
using Xunit;

namespace Corkboard.Core.Tests;

/// <summary>
///     考试导出文件（<c>.cbexam</c>）的读写：导出成一份文件、再读回来必须一模一样，
///     手改过 / 别人拼的文件也要尽量认出来。
///     <para>
///         时刻全部钉死（<c>ExportedAt</c> 由调用方给），不读本机时钟。
///     </para>
/// </summary>
public class ExamEntryFileTests
{
    private static readonly DateTime ExportedAt = new(2026, 6, 7, 21, 30, 0);

    private static ExamEntry Make(string subject, DateOnly date, int startHour, int endHour, string? note = null) => new()
    {
        Subject = subject,
        Date = date,
        StartTime = new TimeOnly(startHour, 0),
        EndTime = new TimeOnly(endHour, 0),
        PaperCount = 2,
        PageCount = 8,
        QuestionCount = 23,
        Note = note
    };

    private static readonly ExamEntry Chinese =
        Make("语文", new DateOnly(2026, 6, 7), 9, 11, "考场 302");

    private static readonly ExamEntry Math =
        Make("数学", new DateOnly(2026, 6, 8), 9, 10);

    [Fact]
    public void RoundTripKeepsEveryField()
    {
        var file = ExamEntryFile.Create([Chinese, Math], "期末考试", ExportedAt);
        var json = file.ToJson();

        Assert.True(ExamEntryFile.TryParse(json, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(ExamEntryFile.FormatName, parsed.Format);
        Assert.Equal(ExamEntryFile.CurrentVersion, parsed.Version);
        Assert.Equal(ExportedAt, parsed.ExportedAt);
        Assert.Equal("期末考试", parsed.BoardTitle);
        Assert.Equal(2, parsed.Exams.Count);

        var first = parsed.Exams[0];
        Assert.Equal("语文", first.Subject);
        Assert.Equal(new DateOnly(2026, 6, 7), first.Date);
        Assert.Equal(new TimeOnly(9, 0), first.StartTime);
        Assert.Equal(new TimeOnly(11, 0), first.EndTime);
        Assert.Equal(2, first.PaperCount);
        Assert.Equal(8, first.PageCount);
        Assert.Equal(23, first.QuestionCount);
        Assert.Equal("考场 302", first.Note);
        Assert.Equal(Chinese.Id, first.Id);
    }

    [Fact]
    public void CreateSortsByDateAndStart()
    {
        var file = ExamEntryFile.Create([Math, Chinese], "期末考试", ExportedAt);

        Assert.Equal(["语文", "数学"], file.Exams.Select(exam => exam.Subject).ToArray());
    }

    [Fact]
    public void CreateClonesExams()
    {
        var file = ExamEntryFile.Create([Chinese], null, ExportedAt);

        // 导出的是一份副本：导出之后再改看板里的那一场，文件内容不该跟着变。
        Chinese.Note = "改过了";
        Assert.Equal("考场 302", file.Exams[0].Note);
        Chinese.Note = "考场 302";
    }

    [Fact]
    public void CreateSkipsNullEntries()
    {
        var file = ExamEntryFile.Create([Chinese, null!, Math], null, ExportedAt);

        Assert.Equal(2, file.Exams.Count);
    }

    [Fact]
    public void EmptyBoardTitleBecomesNull()
    {
        var file = ExamEntryFile.Create([Chinese], "   ", ExportedAt);

        Assert.Null(file.BoardTitle);
    }

    [Fact]
    public void TryParseRejectsGarbage()
    {
        Assert.False(ExamEntryFile.TryParse(null, out _));
        Assert.False(ExamEntryFile.TryParse(string.Empty, out _));
        Assert.False(ExamEntryFile.TryParse("这不是 JSON", out _));
        Assert.False(ExamEntryFile.TryParse("42", out _));
        Assert.False(ExamEntryFile.TryParse("\"文字\"", out _));
        Assert.False(ExamEntryFile.TryParse("{ \"exams\": [] }", out _));
        Assert.False(ExamEntryFile.TryParse("{ \"subject\": \"\" }", out _));
    }

    [Fact]
    public void TryParseAcceptsBareArray()
    {
        var json = """
                   [
                     { "subject": "物理", "date": "2026-06-09", "start_time": "08:00", "end_time": "09:15" }
                   ]
                   """;

        Assert.True(ExamEntryFile.TryParse(json, out var file));
        Assert.NotNull(file);
        Assert.Single(file.Exams);
        Assert.Equal("物理", file.Exams[0].Subject);
    }

    [Fact]
    public void TryParseAcceptsSingleBareExam()
    {
        var json = """
                   { "subject": "化学", "date": "2026-06-10", "start_time": "10:00", "end_time": "11:15" }
                   """;

        Assert.True(ExamEntryFile.TryParse(json, out var file));
        Assert.NotNull(file);
        Assert.Single(file.Exams);
        Assert.Equal("化学", file.Exams[0].Subject);
    }

    [Fact]
    public void TryParseAcceptsShellWithoutFormatMarker()
    {
        // 别人手拼的外壳：没写 format / version，只要带了 exams 就认。
        var json = """
                   { "exams": [ { "subject": "英语", "date": "2026-06-11", "start_time": "15:00", "end_time": "17:00" } ] }
                   """;

        Assert.True(ExamEntryFile.TryParse(json, out var file));
        Assert.NotNull(file);
        Assert.Equal("英语", file.Exams[0].Subject);
    }

    [Fact]
    public void ResolveImportableExamsDropsBlankSubjectsAndNormalizes()
    {
        var file = new ExamEntryFile
        {
            Exams =
            [
                new ExamEntry { Subject = "  历史  ", Date = new DateOnly(2026, 6, 12), PaperCount = 0 },
                new ExamEntry { Subject = "   ", Date = new DateOnly(2026, 6, 12) },
                null!
            ]
        };

        var exams = file.ResolveImportableExams();

        Assert.Single(exams);
        Assert.Equal("历史", exams[0].Subject);

        // 0 张卷子当「没填」，与 ExamEntry.Normalize 同一套规则。
        Assert.Null(exams[0].PaperCount);
    }

    [Fact]
    public void BuildSuggestedFileNameUsesSubjectAndDate()
    {
        Assert.Equal("语文_2026-06-07.cbexam", ExamEntryFile.BuildSuggestedFileName(Chinese));
    }

    [Fact]
    public void BuildSuggestedFileNameReplacesIllegalCharacters()
    {
        var exam = Make("物理/化学", new DateOnly(2026, 6, 9), 8, 9);

        Assert.Equal("物理_化学_2026-06-09.cbexam", ExamEntryFile.BuildSuggestedFileName(exam));
    }

    [Fact]
    public void BuildSuggestedFileNameFallsBackWhenSubjectIsBlank()
    {
        var exam = Make("   ", new DateOnly(2026, 6, 9), 8, 9);

        Assert.Equal("exam_2026-06-09.cbexam", ExamEntryFile.BuildSuggestedFileName(exam));
    }

    [Fact]
    public void BuildSuggestedFileNameForBoardUsesTitleAndFirstExamDate()
    {
        var board = new ExamBoardDocument
        {
            Title = "期末考试",
            Exams =
            [
                Make("数学", new DateOnly(2026, 6, 8), 9, 10),
                Make("语文", new DateOnly(2026, 6, 7), 9, 11)
            ]
        };

        // 日子取**第一场**（按日期 + 开考时刻排最靠前的那场），不是列表顺序里的第一个。
        Assert.Equal("期末考试_2026-06-07.cbexam", ExamEntryFile.BuildSuggestedFileName(board));
    }

    [Fact]
    public void BuildSuggestedFileNameForBoardReplacesIllegalCharacters()
    {
        var board = new ExamBoardDocument
        {
            Title = "期末/考试",
            Exams = [Make("语文", new DateOnly(2026, 6, 7), 9, 11)]
        };

        Assert.Equal("期末_考试_2026-06-07.cbexam", ExamEntryFile.BuildSuggestedFileName(board));
    }

    [Fact]
    public void BuildSuggestedFileNameForEmptyBoardFallsBackToDefaultTitleAndToday()
    {
        var board = new ExamBoardDocument { Title = "  " };

        var name = ExamEntryFile.BuildSuggestedFileName(board);

        // 标题留空 → 走 ExamBoardDocument.ResolveTitle() 的默认名；没考试 → 用今天。
        Assert.EndsWith(".cbexam", name, StringComparison.Ordinal);
        Assert.Contains(Resources.Exam_Board_DefaultTitle, name, StringComparison.Ordinal);
    }
}
