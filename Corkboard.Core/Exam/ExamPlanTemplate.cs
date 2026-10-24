using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Shared;
using Corkboard.Shared.Abstraction;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Exam;

/// <summary>
///     考试模板里的一场：**只记「第几天」，不记绝对日期**。
///     <para>
///         ⚠️ 这是模板能反复套用的关键：模板里写「第 1 天上午 08:00 考语文 150 分钟」，
///         套用时由用户挑这次的第一天，日期现算。存绝对日期的话，模板过一个月就作废，
///         跨月跨年还要用户自己改日期——用户明确说过「只用把开始日期和结束日期改一下」，
///         那就干脆让日期根本不进模板。
///     </para>
/// </summary>
public sealed class ExamPlanTemplateEntry
{
    /// <summary>相对第一天的偏移（0 = 第一天）。</summary>
    public int DayOffset { get; set; }

    /// <summary>这一天的开考时刻。</summary>
    public TimeOnly Start { get; set; } = new(8, 0);

    /// <summary>考多久（分钟）。收卷时刻由它算出来，与 <see cref="ExamPlanEntry" /> 一致。</summary>
    public int DurationMinutes { get; set; } = ExamDurationPresets.FallbackMinutes;

    /// <summary>显示用的科目名（合卷时是「物理 · 化学」）。</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>合卷绑了哪几门作业板科目；单科时为空（与 <see cref="ExamEntry.SubjectKeys" /> 同一套约定）。</summary>
    public List<string>? SubjectKeys { get; set; }

    /// <summary>拷一份。</summary>
    public ExamPlanTemplateEntry Clone() => new()
    {
        DayOffset = DayOffset,
        Start = Start,
        DurationMinutes = ExamDurationPresets.Clamp(DurationMinutes),
        Subject = Subject,
        SubjectKeys = SubjectKeys is null ? null : [.. SubjectKeys]
    };

    /// <summary>套到某一天上：变成一条能直接存进看板的考试。</summary>
    public ExamEntry ToEntry(DateOnly firstDay) => new()
    {
        Id = Guid.NewGuid(),
        Subject = Subject.Trim(),
        Date = firstDay.AddDays(Math.Max(0, DayOffset)),
        StartTime = Start,
        EndTime = ExamPlanEntry.ResolveEnd(Start, DurationMinutes),
        SubjectKeys = ExamEntry.NormalizeSubjectKeys(SubjectKeys)
    };
}

/// <summary>
///     一份考试模板：标题 / 副标题 + 一串「第几天几点考什么」。
///     <para>
///         模板**不带日期**（见 <see cref="ExamPlanTemplateEntry" />），也不带卷面信息——
///         每届考试的卷子张数不一样，那些字段留给编辑窗口逐条改。
///     </para>
/// </summary>
public partial class ExamPlanTemplate : ObservableObject
{
    /// <summary>名字最多这么长（列表里一行放得下）。</summary>
    public const int MaxNameLength = 40;

    /// <summary>模板标识。删 / 覆盖都按它认，定下后不改。</summary>
    [ObservableProperty] private Guid _id = Guid.NewGuid();

    /// <summary>模板名（列表里显示的那个，也是「同名即覆盖」的判据）。</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>套用后写进看板的标题（空 = 用默认名）。</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>套用后写进看板的副标题（空 = 不显示）。</summary>
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>副标题颜色（空 = 跟随主题）。</summary>
    [ObservableProperty] private Color? _subtitleColor;

    /// <summary>全部场次，按「第几天 + 开考时刻」自己排，不依赖这里的顺序。</summary>
    [ObservableProperty] private List<ExamPlanTemplateEntry> _entries = [];

    /// <summary>列表里显示的名字：没填就给一个能认出来的默认名。</summary>
    public string ResolveName() =>
        string.IsNullOrWhiteSpace(Name) ? CR.Settings_Exam_Plan_TemplateDefaultName : Name.Trim();

    /// <summary>这份模板跨几天（没有场次时是 0）。</summary>
    public int DayCount => Entries.Count == 0 ? 0 : Entries.Max(entry => entry.DayOffset) + 1;

    /// <summary>「共 12 场 · 跨 3 天」——列表里那行说明。</summary>
    public string Describe() => string.Format(CR.Settings_Exam_Plan_TemplateSummary, Entries.Count, DayCount);

    /// <summary>套到「第一天是哪天」上，排出可以直接存进看板的考试。</summary>
    public List<ExamEntry> CreateEntries(DateOnly firstDay) =>
    [
        .. Entries
            .OrderBy(entry => entry.DayOffset)
            .ThenBy(entry => entry.Start)
            .Select(entry => entry.ToEntry(firstDay))
    ];

    /// <summary>拷一份（模板管理里「以某份为底改一份」用）。</summary>
    public ExamPlanTemplate Clone() => new()
    {
        Id = Id,
        Name = Name,
        Title = Title,
        Subtitle = Subtitle,
        SubtitleColor = SubtitleColor,
        Entries = [.. Entries.Select(entry => entry.Clone())]
    };

    /// <summary>读盘后收拾一遍：名字与标题去空白、时长夹进合法区间、科目空的场次丢掉。</summary>
    public void Normalize()
    {
        Name = Trim(Name, MaxNameLength);
        Title = Title?.Trim() ?? string.Empty;
        Subtitle = Subtitle?.Trim() ?? string.Empty;

        Entries = [.. (Entries ?? [])
            .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Subject))
            .Select(entry =>
            {
                var clone = entry.Clone();
                clone.DayOffset = Math.Max(0, clone.DayOffset);
                clone.DurationMinutes = ExamDurationPresets.Clamp(clone.DurationMinutes);
                clone.Subject = clone.Subject.Trim();
                return clone;
            })
            .OrderBy(entry => entry.DayOffset)
            .ThenBy(entry => entry.Start)];
    }

    /// <summary>从一批考试里抽出模板：日期换算成「相对最早那天的第几天」。</summary>
    public static ExamPlanTemplate FromExams(
        string name,
        IEnumerable<ExamEntry>? exams,
        string? title = null,
        string? subtitle = null,
        Color? subtitleColor = null)
    {
        var list = (exams ?? []).Where(exam => exam is not null && !string.IsNullOrWhiteSpace(exam.Subject)).ToList();

        var template = new ExamPlanTemplate
        {
            Name = Trim(name, MaxNameLength),
            Title = title?.Trim() ?? string.Empty,
            Subtitle = subtitle?.Trim() ?? string.Empty,
            SubtitleColor = subtitleColor
        };

        if (list.Count == 0)
            return template;

        var firstDay = list.Min(exam => exam.Date);

        template.Entries =
        [
            .. list
                .OrderBy(exam => exam.Date)
                .ThenBy(exam => exam.StartTime)
                .Select(exam => new ExamPlanTemplateEntry
                {
                    DayOffset = exam.Date.DayNumber - firstDay.DayNumber,
                    Start = exam.StartTime,
                    DurationMinutes = ExamDurationPresets.Clamp((int)exam.Duration.TotalMinutes),
                    Subject = exam.Subject.Trim(),
                    SubjectKeys = ExamEntry.NormalizeSubjectKeys(exam.SubjectKeys)
                })
        ];

        return template;
    }

    private static string Trim(string? value, int maxLength)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}

/// <summary>
///     全部模板装一个文件：<c>data/exam/templates.json</c>。
///     <para>
///         不像看板那样一块一个文件——模板是小对象，份数也就几个到十几个，
///         一个文件装完省掉索引，读写都只有一次。
///     </para>
/// </summary>
public partial class ExamTemplateConfig : ConfigBase
{
    public const string StorageFileName = "templates.json";

    /// <summary>全部模板，顺序即界面上的顺序（新的在前）。</summary>
    [ObservableProperty] private List<ExamPlanTemplate> _templates = [];

    [JsonIgnore]
    public override string ConfigFilePath =>
        Utils.GetFilePath(ExamBoardDocument.StorageDirectoryName, StorageFileName);
}
