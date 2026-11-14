using Corkboard.Core.ControlCenter;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Tests;

/// <summary>
///     <c>homework.read</c> 回传快照的形状与收敛规则：科目顺序、只回板上的作业、正文压成一行、
///     截止日期排序与格式化、上限截断。字段名与服务端 / 网页共用，改这里等于改协议。
/// </summary>
public class ControlHomeworkSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Build_EmptyBoard_ReturnsNoSubjects()
    {
        var snapshot = ControlHomeworkSnapshot.Build([], [], [], Now);

        Assert.Equal(0, snapshot["noteCount"]!.GetValue<int>());
        Assert.False(snapshot["truncated"]!.GetValue<bool>());
        Assert.Empty(snapshot["subjects"]!.AsArray());
        Assert.Equal(Now.ToString("O"), snapshot["generatedAt"]!.GetValue<string>());
    }

    [Fact]
    public void Build_OrdersSubjectsByManualOrderThenConfigThenNotes()
    {
        var notes = new[]
        {
            NoteIn("英语"), NoteIn("数学"), NoteIn("语文"), NoteIn("临时科目")
        };

        var snapshot = ControlHomeworkSnapshot.Build(
            notes,
            ["语文", "数学", "英语"],
            ["数学", "语文"],
            Now);

        var subjects = snapshot["subjects"]!.AsArray()
            .Select(node => node!["subject"]!.GetValue<string>())
            .ToList();

        Assert.Equal(["数学", "语文", "英语", "临时科目"], subjects);
    }

    [Fact]
    public void Build_ExcludesCleanedNotes()
    {
        var onBoard = NoteIn("语文");
        var cleaned = NoteIn("语文");
        cleaned.CleanedAt = Now.AddDays(-1);

        var snapshot = ControlHomeworkSnapshot.Build([onBoard, cleaned], ["语文"], [], Now);

        Assert.Equal(1, snapshot["noteCount"]!.GetValue<int>());

        var items = snapshot["subjects"]![0]!["items"]!.AsArray();
        Assert.Single(items);
        Assert.Equal(onBoard.Id.ToString("D"), items[0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void Build_SkipsEmptySubjectGroups()
    {
        var snapshot = ControlHomeworkSnapshot.Build([NoteIn("语文")], ["语文", "数学"], ["英语"], Now);

        var subjects = snapshot["subjects"]!.AsArray()
            .Select(node => node!["subject"]!.GetValue<string>())
            .ToList();

        Assert.Equal(["语文"], subjects);
    }

    [Fact]
    public void Build_NormalizesContentToSingleLineAndTruncates()
    {
        var note = NoteIn("语文");
        note.Content = "  第一行\r\n\r\n第二行\t结尾  ";

        var longNote = NoteIn("语文");
        longNote.Content = new string('字', ControlHomeworkSnapshot.MaxContentLength + 20);

        var snapshot = ControlHomeworkSnapshot.Build([note, longNote], ["语文"], [], Now);

        var items = snapshot["subjects"]![0]!["items"]!.AsArray();
        var contents = items.Select(node => node!["content"]!.GetValue<string>()).ToList();

        Assert.Contains("第一行 第二行 结尾", contents);

        var truncatedText = contents.Single(text => text.StartsWith('字'));
        Assert.Equal(ControlHomeworkSnapshot.MaxContentLength + 1, truncatedText.Length);
        Assert.EndsWith("…", truncatedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SortsByDueDateAscendingThenCreatedAtDescending()
    {
        var late = NoteIn("语文");
        late.DueDate = new DateOnly(2026, 11, 20);
        late.CreatedAt = Now.AddHours(-1);

        var early = NoteIn("语文");
        early.DueDate = new DateOnly(2026, 11, 16);
        early.CreatedAt = Now.AddHours(-2);

        var noDueOld = NoteIn("语文");
        noDueOld.CreatedAt = Now.AddHours(-5);

        var noDueNew = NoteIn("语文");
        noDueNew.CreatedAt = Now.AddHours(-3);

        var snapshot = ControlHomeworkSnapshot.Build(
            [late, noDueOld, early, noDueNew], ["语文"], [], Now);

        var items = snapshot["subjects"]![0]!["items"]!.AsArray();
        var order = items.Select(node => node!["id"]!.GetValue<string>()).ToList();

        Assert.Equal(
            [
                early.Id.ToString("D"),
                late.Id.ToString("D"),
                noDueNew.Id.ToString("D"),
                noDueOld.Id.ToString("D")
            ],
            order);
    }

    [Fact]
    public void Build_WritesDueDateAsDateOnlyAndNullWhenAbsent()
    {
        var withDue = NoteIn("语文");
        withDue.DueDate = new DateOnly(2026, 12, 1);
        withDue.Section = "  第一单元  ";

        var withoutDue = NoteIn("语文");
        withoutDue.Section = "   ";

        var snapshot = ControlHomeworkSnapshot.Build([withDue, withoutDue], ["语文"], [], Now);

        var items = snapshot["subjects"]![0]!["items"]!.AsArray();
        var first = items.Single(node => node!["id"]!.GetValue<string>() == withDue.Id.ToString("D"))!;
        var second = items.Single(node => node!["id"]!.GetValue<string>() == withoutDue.Id.ToString("D"))!;

        Assert.Equal("2026-12-01", first["dueDate"]!.GetValue<string>());
        Assert.Equal("第一单元", first["section"]!.GetValue<string>());
        Assert.Null(second["dueDate"]);
        Assert.Null(second["section"]);
    }

    [Fact]
    public void Build_MarksHandwritingAndKeepsContentEmpty()
    {
        var note = NoteIn("语文");
        note.Content = string.Empty;
        note.Handwriting = "some vector payload";

        var snapshot = ControlHomeworkSnapshot.Build([note], ["语文"], [], Now);

        var item = snapshot["subjects"]![0]!["items"]![0]!;
        Assert.True(item["handwriting"]!.GetValue<bool>());
        Assert.Equal(string.Empty, item["content"]!.GetValue<string>());
    }

    [Fact]
    public void Build_HandwritingCarriesInkText()
    {
        var note = NoteIn("语文");
        note.Content = string.Empty;
        note.Handwriting = "  CORKBOARD-INK/2\naspect=1.6000\nref=540.00\n#FF1A1A1A|4.00|0|0.1000,0.2000,1.000\n  ";

        var snapshot = ControlHomeworkSnapshot.Build([note], ["语文"], [], Now);

        var item = snapshot["subjects"]![0]!["items"]![0]!;
        Assert.True(item["handwriting"]!.GetValue<bool>());
        Assert.Equal(note.Handwriting.Trim(), item["ink"]!.GetValue<string>());
        Assert.False(snapshot["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public void Build_NonHandwritingNoteHasNullInk()
    {
        var note = NoteIn("语文");

        var snapshot = ControlHomeworkSnapshot.Build([note], ["语文"], [], Now);

        var item = snapshot["subjects"]![0]!["items"]![0]!;
        Assert.False(item["handwriting"]!.GetValue<bool>());
        Assert.Null(item["ink"]);
    }

    [Fact]
    public void Build_DropsInkOverPerItemLimit()
    {
        var note = NoteIn("语文");
        note.Content = string.Empty;
        note.Handwriting = new string('x', ControlHomeworkSnapshot.MaxInkLength + 1);

        var snapshot = ControlHomeworkSnapshot.Build([note], ["语文"], [], Now);

        var item = snapshot["subjects"]![0]!["items"]![0]!;
        Assert.True(item["handwriting"]!.GetValue<bool>());
        Assert.Null(item["ink"]);
        Assert.True(snapshot["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public void Build_DropsInkOverTotalBudget()
    {
        var first = NoteIn("语文");
        first.Content = string.Empty;
        first.Handwriting = new string('a', 40);

        var second = NoteIn("语文");
        second.Content = string.Empty;
        second.Handwriting = new string('b', 40);

        // 预算只够第一页（40），第二页被挤掉；正文与条数不受影响。
        var snapshot = ControlHomeworkSnapshot.Build([first, second], ["语文"], [], Now, inkBudget: 50);

        var items = snapshot["subjects"]![0]!["items"]!.AsArray();
        Assert.Equal(2, items.Count);
        Assert.Equal(2, snapshot["noteCount"]!.GetValue<int>());
        Assert.True(snapshot["truncated"]!.GetValue<bool>());
        Assert.Single(items, node => node!["ink"] is not null);
    }

    [Fact]
    public void Build_TruncatesSubjectsOverLimit()
    {
        var notes = Enumerable.Range(0, ControlHomeworkSnapshot.MaxSubjects + 5)
            .Select(index => NoteIn($"科目{index:D2}"))
            .ToList();

        var snapshot = ControlHomeworkSnapshot.Build(notes, [], [], Now);

        Assert.True(snapshot["truncated"]!.GetValue<bool>());
        Assert.Equal(ControlHomeworkSnapshot.MaxSubjects, snapshot["subjects"]!.AsArray().Count);
    }

    [Fact]
    public void Build_TruncatesItemsOverTotalLimit()
    {
        // 5 个科目 × 30 条 = 150 > 120：按科目顺序装到 120 条为止，剩下的整组不再出现。
        var notes = new List<Note>();
        for (var subject = 0; subject < 5; subject++)
            for (var item = 0; item < ControlHomeworkSnapshot.MaxItemsPerSubject; item++)
                notes.Add(NoteIn($"科目{subject:D2}"));

        var snapshot = ControlHomeworkSnapshot.Build(
            notes,
            Enumerable.Range(0, 5).Select(index => $"科目{index:D2}").ToArray(),
            [],
            Now);

        Assert.True(snapshot["truncated"]!.GetValue<bool>());
        Assert.Equal(ControlHomeworkSnapshot.MaxItemsTotal, snapshot["noteCount"]!.GetValue<int>());
        Assert.Equal(4, snapshot["subjects"]!.AsArray().Count);
    }

    [Fact]
    public void Build_GroupsUncategorizedNotesUnderEmptySubject()
    {
        var note = NoteIn(string.Empty);

        var snapshot = ControlHomeworkSnapshot.Build([note], ["语文"], [], Now);

        Assert.Equal(string.Empty, snapshot["subjects"]![0]!["subject"]!.GetValue<string>());
        Assert.Equal(1, snapshot["noteCount"]!.GetValue<int>());
    }

    private static Note NoteIn(string subject) => new()
    {
        Content = "作业内容",
        Subject = subject,
        CreatedAt = Now.AddHours(-1),
        UpdatedAt = Now.AddHours(-1)
    };
}