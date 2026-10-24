using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;

namespace Corkboard.Core.Exam;

/// <summary>
///     考试模板的读写契约：一份 <c>data/exam/templates.json</c> 装全部模板。
/// </summary>
public interface IExamTemplateService
{
    /// <summary>全部模板（新的在前）。</summary>
    ReadOnlyObservableCollection<ExamPlanTemplate> Templates { get; }

    /// <summary>模板增删改之后发一次。</summary>
    event EventHandler? Changed;

    /// <summary>
    ///     存一份模板：同名的就地覆盖（同一块看板反复「另存为模板」不该越存越多），
    ///     否则插到最前面。
    /// </summary>
    ExamPlanTemplate Save(ExamPlanTemplate template);

    /// <summary>删掉一份模板。</summary>
    bool Remove(Guid id);

    /// <summary>按 Id 找一份。</summary>
    ExamPlanTemplate? Find(Guid id);
}

/// <summary>
///     考试模板服务：内存一份清单，每次变更整份写回 <c>data/exam/templates.json</c>。
///     <para>
///         与看板文件分开：模板是「跨届复用」的东西，删掉一块看板不该把模板一起带走，
///         反过来也一样。份数少、单份小，所以一个文件装完全部，不另做索引。
///     </para>
/// </summary>
public sealed class ExamTemplateService : IExamTemplateService
{
    private readonly ConfigServiceBase _configService;
    private readonly ILogger<ExamTemplateService>? _logger;

    private readonly ObservableCollection<ExamPlanTemplate> _templates = [];
    private readonly ExamTemplateConfig _config;

    public ExamTemplateService(ConfigServiceBase configService, ILogger<ExamTemplateService>? logger = null)
    {
        _configService = configService;
        _logger = logger;

        Templates = new ReadOnlyObservableCollection<ExamPlanTemplate>(_templates);

        _config = _configService.LoadConfig(new ExamTemplateConfig());
        LoadAll();
    }

    public ReadOnlyObservableCollection<ExamPlanTemplate> Templates { get; }

    public event EventHandler? Changed;

    public ExamPlanTemplate Save(ExamPlanTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        template.Normalize();
        if (template.Id == Guid.Empty)
            template.Id = Guid.NewGuid();

        var existing = _templates.FirstOrDefault(
            item => string.Equals(item.ResolveName(), template.ResolveName(), StringComparison.OrdinalIgnoreCase));

        if (existing is not null && existing.Id != template.Id)
        {
            // 同名即覆盖：把旧那份的内容换掉，Id 留着——外面可能正拿着它的引用。
            existing.Title = template.Title;
            existing.Subtitle = template.Subtitle;
            existing.SubtitleColor = template.SubtitleColor;
            existing.Entries = [.. template.Entries.Select(entry => entry.Clone())];
            Persist();
            return existing;
        }

        if (existing is not null)
        {
            Persist();
            return existing;
        }

        _templates.Insert(0, template);
        Persist();
        return template;
    }

    public bool Remove(Guid id)
    {
        var target = _templates.FirstOrDefault(item => item.Id == id);
        if (target is null)
            return false;

        _templates.Remove(target);
        Persist();
        return true;
    }

    public ExamPlanTemplate? Find(Guid id) => _templates.FirstOrDefault(item => item.Id == id);

    /// <summary>读盘：坏掉的（没有场次、名字空）也留着，用户可能刚建了一半；只做规范化。</summary>
    private void LoadAll()
    {
        var loaded = _config.Templates ?? [];
        foreach (var template in loaded)
        {
            if (template is null)
                continue;

            template.Normalize();
            if (template.Id == Guid.Empty)
                template.Id = Guid.NewGuid();

            _templates.Add(template);
        }

        if (_templates.Count != loaded.Count)
            _logger?.LogDebug("Dropped {Count} invalid exam templates while loading",
                loaded.Count - _templates.Count);
    }

    private void Persist()
    {
        _config.Templates = [.. _templates];
        _configService.SaveConfig(_config);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
