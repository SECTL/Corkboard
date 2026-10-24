using Corkboard.Core.Abstraction;
using Corkboard.Core.Exam;
using Corkboard.Shared.Abstraction;
using Xunit;

namespace Corkboard.Core.Tests;

/// <summary>
///     考试模板：**只存「第几天几点考什么」，不存绝对日期**——这条是模板能反复用的前提，
///     所以每一处都要钉住：套到不同第一天排出来的日期跟着走、时长换成收卷时刻、
///     合卷的科目清单不丢、同名保存是覆盖而不是再堆一份。
/// </summary>
public class ExamPlanTemplateTests
{
    private static readonly DateOnly FirstDay = new(2026, 6, 24);

    private sealed class InMemoryConfigService : ConfigServiceBase
    {
        public Dictionary<string, ConfigBase> Saved { get; } = [];

        public int SaveCount { get; private set; }

        public override bool IsConfigExists<T>(T fallback) => Saved.ContainsKey(fallback.ConfigFilePath);

        public override T LoadConfig<T>(T fallback) =>
            Saved.TryGetValue(fallback.ConfigFilePath, out var stored) ? (T)stored : fallback;

        public override void SaveConfig<T>(T config)
        {
            SaveCount++;
            Saved[config.ConfigFilePath] = config;
        }

        public override void DeleteConfig<T>(T config) => Saved.Remove(config.ConfigFilePath);
    }

    private static ExamPlanTemplate BuildSample() => new()
    {
        Name = "期末考试",
        Title = "2026 年期末考试",
        Subtitle = "距离高考还有 200 天",
        Entries =
        [
            new ExamPlanTemplateEntry { DayOffset = 0, Start = new TimeOnly(8, 0), DurationMinutes = 150, Subject = "语文" },
            new ExamPlanTemplateEntry { DayOffset = 0, Start = new TimeOnly(14, 0), DurationMinutes = 120, Subject = "数学" },
            new ExamPlanTemplateEntry
            {
                DayOffset = 1,
                Start = new TimeOnly(8, 0),
                DurationMinutes = 150,
                Subject = "物理 · 化学",
                SubjectKeys = ["物理", "化学"]
            }
        ]
    };

    [Fact]
    public void CreateEntries_UsesTheFirstDayOfThisRun()
    {
        var template = BuildSample();

        var entries = template.CreateEntries(FirstDay);

        Assert.Equal(new DateOnly(2026, 6, 24), entries[0].Date);
        Assert.Equal(new DateOnly(2026, 6, 25), entries[2].Date);
    }

    [Fact]
    public void CreateEntries_ShiftsWithAnotherFirstDay()
    {
        // 模板能一直用下去的关键：换一年、换一个月，日期全跟着第一天走。
        var entries = BuildSample().CreateEntries(new DateOnly(2027, 1, 11));

        Assert.Equal(new DateOnly(2027, 1, 11), entries[0].Date);
        Assert.Equal(new DateOnly(2027, 1, 12), entries[2].Date);
    }

    [Fact]
    public void CreateEntries_ComputesEndTimeFromDuration()
    {
        var entries = BuildSample().CreateEntries(FirstDay);

        Assert.Equal(new TimeOnly(8, 0), entries[0].StartTime);
        Assert.Equal(new TimeOnly(10, 30), entries[0].EndTime);
        Assert.Equal(new TimeOnly(14, 0), entries[1].StartTime);
        Assert.Equal(new TimeOnly(16, 0), entries[1].EndTime);
    }

    [Fact]
    public void CreateEntries_KeepsCombinedSubjectKeys()
    {
        // 合卷的科目清单丢了，看板上就只剩一个显示名，编辑时也没法拆回去。
        var entries = BuildSample().CreateEntries(FirstDay);

        Assert.Equal(["物理", "化学"], entries[2].SubjectKeys);
        Assert.Equal("物理 · 化学", entries[2].Subject);
    }

    [Fact]
    public void CreateEntries_SingleSubjectHasNoSubjectKeys()
    {
        var entries = BuildSample().CreateEntries(FirstDay);

        Assert.Null(entries[0].SubjectKeys);
    }

    [Fact]
    public void CreateEntries_SortsByDayThenStart()
    {
        var template = new ExamPlanTemplate
        {
            Entries =
            [
                new ExamPlanTemplateEntry { DayOffset = 1, Start = new TimeOnly(9, 0), Subject = "历史" },
                new ExamPlanTemplateEntry { DayOffset = 0, Start = new TimeOnly(14, 0), Subject = "数学" },
                new ExamPlanTemplateEntry { DayOffset = 0, Start = new TimeOnly(8, 0), Subject = "语文" }
            ]
        };

        var entries = template.CreateEntries(FirstDay);

        Assert.Equal(["语文", "数学", "历史"], entries.Select(entry => entry.Subject));
    }

    [Fact]
    public void DayCount_CountsInclusiveDays()
    {
        Assert.Equal(2, BuildSample().DayCount);
    }

    [Fact]
    public void DayCount_EmptyTemplateIsZero()
    {
        Assert.Equal(0, new ExamPlanTemplate().DayCount);
    }

    [Fact]
    public void FromExams_ConvertsDatesToDayOffsets()
    {
        // 「另存为模板」走的就是这条路：绝对日期一律换算成相对第一天。
        List<ExamEntry> exams =
        [
            new() { Subject = "语文", Date = new DateOnly(2026, 6, 24), StartTime = new(8, 0), EndTime = new(10, 30) },
            new() { Subject = "数学", Date = new DateOnly(2026, 6, 25), StartTime = new(14, 0), EndTime = new(16, 0) }
        ];

        var template = ExamPlanTemplate.FromExams("期末考试", exams, "标题", "副标题");

        Assert.Equal(2, template.Entries.Count);
        Assert.Equal(0, template.Entries[0].DayOffset);
        Assert.Equal(1, template.Entries[1].DayOffset);
        Assert.Equal(150, template.Entries[0].DurationMinutes);
        Assert.Equal(120, template.Entries[1].DurationMinutes);
        Assert.Equal("标题", template.Title);
        Assert.Equal("副标题", template.Subtitle);
    }

    [Fact]
    public void FromExams_SkipsBlankSubjects()
    {
        List<ExamEntry> exams =
        [
            new() { Subject = "语文", Date = FirstDay, StartTime = new(8, 0), EndTime = new(10, 30) },
            new() { Subject = "   ", Date = FirstDay, StartTime = new(14, 0), EndTime = new(16, 0) }
        ];

        Assert.Single(ExamPlanTemplate.FromExams("x", exams).Entries);
    }

    [Fact]
    public void FromExams_NoExams_LeavesEntriesEmpty()
    {
        var template = ExamPlanTemplate.FromExams("空的", []);

        Assert.Empty(template.Entries);
        Assert.Equal(0, template.DayCount);
    }

    [Fact]
    public void RoundTrip_FromExamsThenCreateEntries_KeepsTheSchedule()
    {
        // 存成模板再套回去，日期、时刻、时长、合卷清单都该原样回来。
        List<ExamEntry> exams =
        [
            new() { Subject = "语文", Date = FirstDay, StartTime = new(8, 0), EndTime = new(10, 30) },
            new()
            {
                Subject = "物理 · 化学",
                SubjectKeys = ["物理", "化学"],
                Date = FirstDay.AddDays(1),
                StartTime = new(14, 0),
                EndTime = new(16, 30)
            }
        ];

        var template = ExamPlanTemplate.FromExams("x", exams);
        var restored = template.CreateEntries(new DateOnly(2027, 6, 7));

        Assert.Equal(new DateOnly(2027, 6, 7), restored[0].Date);
        Assert.Equal(new TimeOnly(8, 0), restored[0].StartTime);
        Assert.Equal(new TimeOnly(10, 30), restored[0].EndTime);
        Assert.Equal(new DateOnly(2027, 6, 8), restored[1].Date);
        Assert.Equal(["物理", "化学"], restored[1].SubjectKeys);
    }

    [Fact]
    public void Normalize_DropsEntriesWithoutSubject()
    {
        var template = new ExamPlanTemplate
        {
            Entries =
            [
                new ExamPlanTemplateEntry { Subject = "语文" },
                new ExamPlanTemplateEntry { Subject = "  " }
            ]
        };

        template.Normalize();

        Assert.Single(template.Entries);
    }

    [Fact]
    public void Normalize_ClampsDurationAndDayOffset()
    {
        var template = new ExamPlanTemplate
        {
            Entries = [new ExamPlanTemplateEntry { Subject = "语文", DurationMinutes = 5, DayOffset = -3 }]
        };

        template.Normalize();

        Assert.Equal(ExamDurationPresets.MinMinutes, template.Entries[0].DurationMinutes);
        Assert.Equal(0, template.Entries[0].DayOffset);
    }

    [Fact]
    public void ResolveName_FallsBackToDefault()
    {
        Assert.Equal(
            Corkboard.Core.Langs.Common.Resources.Settings_Exam_Plan_TemplateDefaultName,
            new ExamPlanTemplate().ResolveName());
    }

    [Fact]
    public void Save_InsertNewTemplateAtTheFront()
    {
        var service = new ExamTemplateService(new InMemoryConfigService());

        service.Save(new ExamPlanTemplate { Name = "甲" });
        service.Save(new ExamPlanTemplate { Name = "乙" });

        Assert.Equal(["乙", "甲"], service.Templates.Select(template => template.Name));
    }

    [Fact]
    public void Save_SameNameOverwritesInsteadOfDuplicating()
    {
        // 同一块看板反复「另存为模板」不该越存越多。
        var service = new ExamTemplateService(new InMemoryConfigService());

        service.Save(new ExamPlanTemplate
        {
            Name = "期末考试",
            Entries = [new ExamPlanTemplateEntry { Subject = "语文", DurationMinutes = 150 }]
        });
        service.Save(new ExamPlanTemplate
        {
            Name = "期末考试",
            Entries = [new ExamPlanTemplateEntry { Subject = "数学", DurationMinutes = 120 }]
        });

        Assert.Single(service.Templates);
        Assert.Equal("数学", service.Templates[0].Entries[0].Subject);
    }

    [Fact]
    public void Save_IsCaseInsensitiveByName()
    {
        var service = new ExamTemplateService(new InMemoryConfigService());

        service.Save(new ExamPlanTemplate { Name = "Final" });
        service.Save(new ExamPlanTemplate { Name = "final" });

        Assert.Single(service.Templates);
    }

    [Fact]
    public void Save_PersistsOnlyOnce()
    {
        var config = new InMemoryConfigService();
        var service = new ExamTemplateService(config);

        service.Save(new ExamPlanTemplate { Name = "甲" });

        Assert.Equal(1, config.SaveCount);
    }

    [Fact]
    public void Save_RaisesChanged()
    {
        var service = new ExamTemplateService(new InMemoryConfigService());
        var raised = 0;
        service.Changed += (_, _) => raised++;

        service.Save(new ExamPlanTemplate { Name = "甲" });

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Remove_DeletesById()
    {
        var service = new ExamTemplateService(new InMemoryConfigService());
        var template = service.Save(new ExamPlanTemplate { Name = "甲" });

        Assert.True(service.Remove(template.Id));
        Assert.Empty(service.Templates);
        Assert.False(service.Remove(template.Id));
    }

    [Fact]
    public void Find_ReturnsSavedTemplate()
    {
        var service = new ExamTemplateService(new InMemoryConfigService());
        var template = service.Save(new ExamPlanTemplate { Name = "甲" });

        Assert.Same(template, service.Find(template.Id));
    }

    [Fact]
    public void Service_ReloadsFromConfig()
    {
        // 模板存在 data/exam/templates.json，下次启动要能读回来。
        var config = new InMemoryConfigService();
        var first = new ExamTemplateService(config);
        first.Save(new ExamPlanTemplate
        {
            Name = "期末考试",
            Entries = [new ExamPlanTemplateEntry { Subject = "语文", DurationMinutes = 150 }]
        });

        var second = new ExamTemplateService(config);

        Assert.Single(second.Templates);
        Assert.Equal("期末考试", second.Templates[0].Name);
        Assert.Equal(150, second.Templates[0].Entries[0].DurationMinutes);
    }
}
