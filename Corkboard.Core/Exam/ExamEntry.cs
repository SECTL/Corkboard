using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Exam;

/// <summary>
///     一场考试：科目、日期、起止时刻，外加试卷张数 / 页数 / 大题数这类可选的卷面信息。
///     <para>
///         时间是「日期 + 时刻」两个字段，而不是一个 <see cref="DateTime" />：考试安排本来就是
///         「哪天第几节」，拆开之后表单里就是两个原生控件（日期选择器 + 时间选择器），
///         用户不用记格式。落盘走 <c>DateOnly</c> / <c>TimeOnly</c> 的 ISO 文本，可读性好。
///     </para>
///     <para>
///         卷面信息都是可空的：没填就不显示（看板与列表里都不会出现「0 张」这种噪音）。
///     </para>
/// </summary>
public partial class ExamEntry : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();

    /// <summary>科目名，例如「语文」「数学」。空串在表单里会被拦下来，不会落盘。</summary>
    [ObservableProperty] private string _subject = string.Empty;

    /// <summary>考试日期（本地日期，不含时刻）。</summary>
    [ObservableProperty] private DateOnly _date = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>开考时刻（本地时间）。</summary>
    [ObservableProperty] private TimeOnly _startTime = new(8, 0);

    /// <summary>
    ///     收卷时刻（本地时间）。**必须晚于 <see cref="StartTime" />**：
    ///     表单会拦，历史数据里万一存了反的，<see cref="GetEnd" /> 会把它夹成与开考同一时刻
    ///     （那场考试就不会有「进行中」这一段，也不会把后面的安排顶掉）。
    /// </summary>
    [ObservableProperty] private TimeOnly _endTime = new(10, 0);

    /// <summary>试卷张数。没填（null）就不显示。</summary>
    [ObservableProperty] private int? _paperCount;

    /// <summary>试卷页数。没填（null）就不显示。</summary>
    [ObservableProperty] private int? _pageCount;

    /// <summary>大题数。没填（null）就不显示。</summary>
    [ObservableProperty] private int? _questionCount;

    /// <summary>备注（考场、座位号之类）。没填就不显示。</summary>
    [ObservableProperty] private string? _note;

    /// <summary>
    ///     合卷时绑定的作业板科目（「物理 · 化学」这场绑的就是这两个）。
    ///     <para>
    ///         单科考试留空——<see cref="Subject" /> 自己就是科目名，没必要再存一份。
    ///         合卷时 <see cref="Subject" /> 是拼出来的显示名（<c>物理 · 化学</c>），
    ///         这份清单才是「到底考了哪几科」，看板按科目统计/筛选时读它。
    ///     </para>
    ///     <para>
    ///         ⚠️ 老数据里没有这个字段，读出来是 <see langword="null" /> = 单科，完全兼容。
    ///     </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? SubjectKeys
    {
        get => _subjectKeys;
        set => SetProperty(ref _subjectKeys, NormalizeSubjectKeys(value));
    }

    private List<string>? _subjectKeys;

    /// <summary>收拾科目清单：去空白、去重（忽略大小写）、少于两个就当成单科（返回 <see langword="null" />）。</summary>
    public static List<string>? NormalizeSubjectKeys(IEnumerable<string?>? keys)
    {
        if (keys is null)
            return null;

        var result = new List<string>();
        foreach (var key in keys)
        {
            var name = (key ?? string.Empty).Trim();
            if (name.Length == 0 || result.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            result.Add(name);
        }

        // 一个科目算不上「合卷」：单科就靠 Subject 自己，省得存两份还可能不一致。
        return result.Count > 1 ? result : null;
    }

    /// <summary>绑定的科目清单（单科时返回只含 <see cref="Subject" /> 一项的集合，方便统一处理）。</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ResolveSubjectKeys =>
        SubjectKeys is { Count: > 1 } keys ? keys : [Subject];

    /// <summary>开考时刻（本地、<see cref="DateTimeKind.Unspecified" />，只用来和「现在」比大小）。</summary>
    [JsonIgnore]
    public DateTime StartAt => Date.ToDateTime(StartTime);

    /// <summary>收卷时刻；数据反了就退化成开考时刻，见 <see cref="EndTime" /> 的说明。</summary>
    [JsonIgnore]
    public DateTime EndAt
    {
        get
        {
            var end = Date.ToDateTime(EndTime);
            return end < StartAt ? StartAt : end;
        }
    }

    /// <summary>考试时长（数据反了就是 0）。</summary>
    [JsonIgnore]
    public TimeSpan Duration => EndAt - StartAt;

    /// <summary>卷面信息是否一项都没填（看板据此决定要不要画那一行）。</summary>
    [JsonIgnore]
    public bool HasPaperInfo => PaperCount is not null || PageCount is not null || QuestionCount is not null;

    /// <summary>拷一份（编辑表单拿副本改，取消时不影响原数据；保存时按 <see cref="Id" /> 写回）。</summary>
    public ExamEntry Clone() => new()
    {
        Id = Id,
        Subject = Subject,
        Date = Date,
        StartTime = StartTime,
        EndTime = EndTime,
        PaperCount = PaperCount,
        PageCount = PageCount,
        QuestionCount = QuestionCount,
        Note = Note,
        SubjectKeys = SubjectKeys is null ? null : [.. SubjectKeys]
    };

    /// <summary>把另一份的内容盖到自己身上（只盖内容，不动 <see cref="Id" />）。</summary>
    public void CopyFrom(ExamEntry other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Subject = other.Subject;
        Date = other.Date;
        StartTime = other.StartTime;
        EndTime = other.EndTime;
        PaperCount = other.PaperCount;
        PageCount = other.PageCount;
        QuestionCount = other.QuestionCount;
        Note = other.Note;
        SubjectKeys = other.SubjectKeys is null ? null : [.. other.SubjectKeys];
    }

    /// <summary>
    ///     收拾一条考试数据：科目去首尾空白、卷面信息把 ≤ 0 当成没填、时间反了就把收卷拉到开考。
    ///     返回收拾后的**新对象**（原对象不动），供读取时统一过一遍。
    /// </summary>
    public static ExamEntry Normalize(ExamEntry source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var normalized = source.Clone();
        normalized.Subject = (source.Subject ?? string.Empty).Trim();
        normalized.PaperCount = NormalizeCount(source.PaperCount);
        normalized.PageCount = NormalizeCount(source.PageCount);
        normalized.QuestionCount = NormalizeCount(source.QuestionCount);
        normalized.Note = string.IsNullOrWhiteSpace(source.Note) ? null : source.Note.Trim();
        return normalized;
    }

    /// <summary>卷面信息的取值范围：0 与负数一律当「没填」，上限挡一下手改出来的天文数字。</summary>
    public const int MaxPaperInfoValue = 999;

    public static int? NormalizeCount(int? value) =>
        value is { } count && count > 0 ? Math.Min(count, MaxPaperInfoValue) : null;
}
