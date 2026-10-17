using Corkboard.Core.Enums.Configs;

using Corkboard.Core.Board;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Tests;

/// <summary>同一科目的作业必须合并成一个区块，区块顺序由排序方式决定。</summary>
public class NoteGroupingTests
{
    private static Note Note(string subject, string content, int createdOffsetMinutes = 0)
    {
        return new Note
        {
            Subject = subject,
            Content = content,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                .AddMinutes(createdOffsetMinutes)
        };
    }

    [Fact]
    public void Group_MergesNotesOfTheSameSubjectIntoSingleGroup()
    {
        var notes = new[] { Note("数学", "一"), Note("语文", "二"), Note("数学", "三") };

        var groups = NoteGrouping.Group(notes, BoardSortMode.CreatedDescending);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups.Single(group => group.Key == "数学").Count());
        Assert.Single(groups.Single(group => group.Key == "语文"));
    }

    [Fact]
    public void Group_KeepsOrderInsideGroupAccordingToSortMode()
    {
        var notes = new[] { Note("数学", "旧", 0), Note("数学", "新", 10) };

        var ascending = NoteGrouping.Group(notes, BoardSortMode.CreatedAscending).Single();
        var descending = NoteGrouping.Group(notes, BoardSortMode.CreatedDescending).Single();

        Assert.Equal(["旧", "新"], ascending.Select(note => note.Content));
        Assert.Equal(["新", "旧"], descending.Select(note => note.Content));
    }

    [Fact]
    public void Group_TrimsSubjectAndLumpsBlankSubjectTogether()
    {
        var notes = new[] { Note(" 数学 ", "一"), Note("数学", "二"), Note("   ", "三") };

        var groups = NoteGrouping.Group(notes, BoardSortMode.CreatedDescending);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups.Single(group => group.Key == "数学").Count());
        Assert.Single(groups.Single(group => group.Key.Length == 0));
    }

    [Fact]
    public void Group_OrdersGroupsBySubjectWhenSortModeIsSubject()
    {
        var notes = new[] { Note("语文", "一"), Note("数学", "二") };

        var groups = NoteGrouping.Group(notes, BoardSortMode.Subject);

        Assert.Equal(["数学", "语文"], groups.Select(group => group.Key));
    }

    [Fact]
    public void Group_SubjectModeFollowsConfiguredSubjectOrder()
    {
        var notes = new[] { Note("英语", "一"), Note("数学", "二"), Note("语文", "三") };

        // 「按科目排序」用的是设置里那份科目清单的先后，不是按名字的字典序。
        var groups = NoteGrouping.Group(notes, BoardSortMode.Subject, null, ["语文", "数学", "英语"]);

        Assert.Equal(["语文", "数学", "英语"], groups.Select(group => group.Key));
    }

    [Fact]
    public void Group_SubjectModePutsSubjectsMissingFromSettingsAtTheEnd()
    {
        var notes = new[] { Note("体育", "一"), Note("数学", "二"), Note("语文", "三") };

        var groups = NoteGrouping.Group(notes, BoardSortMode.Subject, null, ["语文", "数学"]);

        Assert.Equal(["语文", "数学", "体育"], groups.Select(group => group.Key));
    }

    [Fact]
    public void Group_ManualModeOrdersGroupsByStoredSubjectOrder()
    {
        var notes = new[] { Note("语文", "一"), Note("数学", "二"), Note("英语", "三") };

        var groups = NoteGrouping.Group(notes, BoardSortMode.Manual, ["英语", "语文", "数学"]);

        Assert.Equal(["英语", "语文", "数学"], groups.Select(group => group.Key));
    }

    [Fact]
    public void Group_ManualModePutsSubjectsMissingFromOrderAtTheEnd()
    {
        var notes = new[] { Note("语文", "一"), Note("数学", "二"), Note("体育", "三") };

        // 「体育」没记进顺序表：整块落到后面，而且不能因此丢作业。
        var groups = NoteGrouping.Group(notes, BoardSortMode.Manual, ["数学", "语文"]);

        Assert.Equal(["数学", "语文", "体育"], groups.Select(group => group.Key));
        Assert.Equal(3, groups.Sum(group => group.Count()));
    }

    [Fact]
    public void Group_ManualModeKeepsInsertionOrderInsideGroup()
    {
        var older = Note("数学", "先加的");
        var newer = Note("数学", "后加的", 10);
        older.Order = 0;
        newer.Order = 1;

        var groups = NoteGrouping.Group([newer, older], BoardSortMode.Manual, ["数学"]);

        // 手动模式只管区块先后，区块内仍按加入顺序，不会因为创建时间新就窜到前面。
        Assert.Equal(["先加的", "后加的"], groups.Single().Select(note => note.Content));
    }

    [Fact]
    public void Group_IgnoresSubjectOrderOutsideManualMode()
    {
        var notes = new[] { Note("语文", "一"), Note("数学", "二") };

        var groups = NoteGrouping.Group(notes, BoardSortMode.CreatedDescending, ["数学", "语文"]);

        // 非手动模式下顺序表不参与，否则设置页选「按科目」还会被手动顺序牵着走。
        Assert.Equal(["语文", "数学"], groups.Select(group => group.Key));
    }

    private static Note SectionedNote(string subject, string section, string content, int createdOffsetMinutes = 0)
    {
        var note = Note(subject, content, createdOffsetMinutes);
        note.Section = section;
        return note;
    }

    [Fact]
    public void GroupWithSections_AllNotesWithoutSectionBecomeSingleUncategorizedGroup()
    {
        var notes = new[] { Note("数学", "一"), Note("数学", "二") };

        var groups = NoteGrouping.GroupWithSections(notes, BoardSortMode.CreatedDescending);

        var subject = Assert.Single(groups);
        var section = Assert.Single(subject.Sections);
        Assert.True(section.IsUncategorized);
        Assert.Equal(2, section.Count);
        Assert.Equal(2, subject.Count);
    }

    [Fact]
    public void GroupWithSections_SplitsIntoSectionsInConfiguredOrder()
    {
        var notes = new[] { SectionedNote("数学", "Alpha", "一"), SectionedNote("数学", "Beta", "二") };

        var groups = NoteGrouping.GroupWithSections(
            notes, BoardSortMode.CreatedDescending, sectionsOf: _ => ["Beta", "Alpha"]);

        // 分类顺序听科目自己的定义（Beta 在前），不看作业先后。
        Assert.Equal(["Beta", "Alpha"], groups.Single().Sections.Select(section => section.Name));
    }

    [Fact]
    public void GroupWithSections_PutsUncategorizedNotesFirst()
    {
        var notes = new[] { SectionedNote("数学", "Alpha", "有分类"), Note("数学", "没分类") };

        var groups = NoteGrouping.GroupWithSections(
            notes, BoardSortMode.CreatedDescending, sectionsOf: _ => ["Alpha"]);

        var sections = groups.Single().Sections;
        Assert.Equal(["", "Alpha"], sections.Select(section => section.Name));
        Assert.Equal("没分类", sections[0].Notes.Single().Content);
    }

    [Fact]
    public void GroupWithSections_PutsRemovedSectionsAfterDefinedOnesByName()
    {
        var notes = new[]
        {
            SectionedNote("数学", "Zeta", "一"),
            SectionedNote("数学", "Beta", "二"),
            SectionedNote("数学", "Alpha", "三")
        };

        var groups = NoteGrouping.GroupWithSections(
            notes, BoardSortMode.CreatedDescending, sectionsOf: _ => ["Alpha"]);

        // 已定义分类在前，已删分类（历史数据）按名称排在后面。
        Assert.Equal(["Alpha", "Beta", "Zeta"], groups.Single().Sections.Select(section => section.Name));
    }

    [Fact]
    public void GroupWithSections_SkipsConfiguredSectionsWithoutNotes()
    {
        var notes = new[] { SectionedNote("数学", "Beta", "一") };

        var groups = NoteGrouping.GroupWithSections(
            notes, BoardSortMode.CreatedDescending, sectionsOf: _ => ["Alpha", "Beta", "Gamma"]);

        // 没有作业的分类不留空组。
        Assert.Equal(["Beta"], groups.Single().Sections.Select(section => section.Name));
    }

    [Fact]
    public void GroupWithSections_TrimsSectionAndMergesBlankNames()
    {
        var notes = new[]
        {
            SectionedNote("数学", " Alpha ", "一"),
            SectionedNote("数学", "Alpha", "二"),
            SectionedNote("数学", "   ", "三")
        };

        var groups = NoteGrouping.GroupWithSections(
            notes, BoardSortMode.CreatedDescending, sectionsOf: _ => ["Alpha"]);

        var sections = groups.Single().Sections;
        Assert.Equal(["", "Alpha"], sections.Select(section => section.Name));
        Assert.Equal(2, sections[1].Count);
        Assert.Single(sections[0].Notes);
    }

    [Fact]
    public void GroupWithSections_KeepsSubjectGroupingAndSortMode()
    {
        var notes = new[]
        {
            SectionedNote("语文", "Alpha", "旧", 0),
            SectionedNote("语文", "Alpha", "新", 10),
            SectionedNote("数学", "Alpha", "别的", 20)
        };

        var groups = NoteGrouping.GroupWithSections(notes, BoardSortMode.CreatedDescending);

        Assert.Equal(["数学", "语文"], groups.Select(group => group.Subject));
        Assert.Equal(["新", "旧"], groups[1].Sections.Single().Notes.Select(note => note.Content));
    }

    [Fact]
    public void GroupWithSections_EmptyInputHasNoGroups()
    {
        Assert.Empty(NoteGrouping.GroupWithSections([], BoardSortMode.CreatedDescending));
    }
}
