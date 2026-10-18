using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;

namespace Corkboard.Core.Exam;

/// <summary>
///     考试安排的服务实现：内存里一份 <see cref="ObservableCollection{T}" />，
///     每次变更后整份写回 <c>data/exam/exams.json</c>。
///     <para>
///         每次改动都整份重写（而不是按条分文件）：考试安排是一份几十条的小表，
///         一次考试季结束就整批换掉，没有作业那种「一条一个文件、按日期归档」的必要。
///     </para>
/// </summary>
public sealed class ExamService : IExamService
{
    private readonly ConfigServiceBase _configService;
    private readonly ILogger<ExamService>? _logger;
    private readonly ObservableCollection<ExamEntry> _exams;
    private ExamScheduleConfig _config;

    public ExamService(ConfigServiceBase configService, ILogger<ExamService>? logger = null)
    {
        _configService = configService;
        _logger = logger;

        _config = _configService.LoadConfig(new ExamScheduleConfig());
        _exams = new ObservableCollection<ExamEntry>(Normalize(_config.Exams));
        Exams = new ReadOnlyObservableCollection<ExamEntry>(_exams);
    }

    public ReadOnlyObservableCollection<ExamEntry> Exams { get; }

    public event EventHandler? Changed;

    public ExamEntry Add(ExamEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var normalized = ExamEntry.Normalize(entry);
        if (normalized.Subject.Length == 0)
            throw new ArgumentException("考试科目不能为空。", nameof(entry));

        if (normalized.Id == Guid.Empty)
            normalized.Id = Guid.NewGuid();

        // 回填 Id：调用方手里那份也要能用。读盘时空科目的条目会被丢掉，
        // 所以这里也不许存——否则界面上会留一条「重启就消失」的空行。
        entry.Id = normalized.Id;

        _exams.Add(normalized);
        Persist();
        return normalized;
    }

    /// <summary>
    ///     按 <see cref="ExamEntry.Id" /> 覆盖一条考试。
    ///     ⚠️ 这里是**逐字段复制**（与 <c>BoardService.Update</c> 同一个坑）：给 <see cref="ExamEntry" />
    ///     加字段时必须同步加到这里，否则编辑保存会静默丢字段。
    /// </summary>
    public bool Update(ExamEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var target = _exams.FirstOrDefault(exam => exam.Id == entry.Id);
        if (target is null)
        {
            _logger?.LogWarning("Skipped updating unknown exam {Id}", entry.Id);
            return false;
        }

        var normalized = ExamEntry.Normalize(entry);
        if (normalized.Subject.Length == 0)
            throw new ArgumentException("考试科目不能为空。", nameof(entry));

        target.Subject = normalized.Subject;
        target.Date = normalized.Date;
        target.StartTime = normalized.StartTime;
        target.EndTime = normalized.EndTime;
        target.PaperCount = normalized.PaperCount;
        target.PageCount = normalized.PageCount;
        target.QuestionCount = normalized.QuestionCount;
        target.Note = normalized.Note;

        Persist();
        return true;
    }

    public bool Remove(Guid id)
    {
        var target = _exams.FirstOrDefault(exam => exam.Id == id);
        if (target is null)
            return false;

        _exams.Remove(target);
        Persist();
        return true;
    }

    public void Save() => Persist();

    public void Reload()
    {
        _config = _configService.LoadConfig(new ExamScheduleConfig());

        _exams.Clear();
        foreach (var exam in Normalize(_config.Exams))
            _exams.Add(exam);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     读盘时收拾一遍：科目空白的条目丢掉（看板上没法显示），其余把时间与卷面信息夹进合法区间。
    ///     手改过的 <c>exams.json</c> 不会把脏数据带进界面。
    /// </summary>
    private List<ExamEntry> Normalize(IEnumerable<ExamEntry>? exams)
    {
        var result = new List<ExamEntry>();
        if (exams is null)
            return result;

        foreach (var exam in exams)
        {
            if (exam is null)
                continue;

            var normalized = ExamEntry.Normalize(exam);
            if (normalized.Subject.Length == 0)
            {
                _logger?.LogDebug("Dropped an exam entry without a subject ({Id})", exam.Id);
                continue;
            }

            if (normalized.Id == Guid.Empty)
                normalized.Id = Guid.NewGuid();

            result.Add(normalized);
        }

        return result;
    }

    private void Persist()
    {
        _config.Exams = [.. _exams];
        _configService.SaveConfig(_config);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
