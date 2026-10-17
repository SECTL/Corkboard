using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Homework;
using Corkboard.Core.Notes;
using Corkboard.Core.Notes.Legacy;

namespace Corkboard.Core.Board;

/// <summary>
///     作业板领域的实现：内存里维护作业与科目两份集合。
///     <para>
///         落盘分两处，都是业务数据、都在 <c>data/board/</c>（与设置文件 <c>data/config/</c> 分开）：
///         科目写单份 <c>board.json</c>（由 <see cref="HomeworkSectionCatalog" /> 管）；
///         **作业本身按创建日期分文件**，落在 <c>&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/&lt;Id&gt;.cbnote</c>，
///         由 <see cref="INoteStore" /> 负责。
///     </para>
/// </summary>
public class BoardService : IBoardService
{
    private readonly INoteStore _noteStore;
    private readonly HomeworkSectionCatalog _sections;
    private readonly ILogger<BoardService> _logger;
    private readonly ObservableCollection<Note> _notes;

    public BoardService(INoteStore noteStore, HomeworkSectionCatalog sections, ILogger<BoardService> logger)
    {
        _noteStore = noteStore;
        _sections = sections;
        _logger = logger;

        var notes = _noteStore.LoadAll();
        if (notes.Count == 0 && _noteStore.TryLoadLegacy() is { } legacy)
        {
            // 归档目录还空着、旧版单文件有货：作业摊到新的日期目录下，定义按下面的规则并回。
            _logger.LogInformation("Migrating {Count} notes into dated folders", legacy.Notes.Count);
            _sections.ImportLegacy(legacy);

            // 旧文件由存储层负责删：它才知道旧文件到底在哪个路径。
            _noteStore.TryDeleteLegacy();

            notes = legacy.Notes;

            // 旧内容是旧格式（Markdown 原文 + 偏移标注）：跟搬迁一起写下去，
            // 免得先把旧格式写进归档目录、紧接着又改写一遍。
            ConvertLegacyContent(notes);
            if (notes.Count > 0)
                _noteStore.SaveAll(notes);

            // 定义（科目）也跟着搬过一次，立刻落盘，避免下次启动重复走搬迁。
            _sections.Save();
        }
        else
        {
            // 归档目录里已经有旧格式的作业：就地转一次并落盘，之后内存与磁盘都是新格式。
            var converted = ConvertLegacyContent(notes);
            if (converted > 0)
                _noteStore.SaveAll(notes);
        }

        _notes = new ObservableCollection<Note>(notes);
        Notes = new ReadOnlyObservableCollection<Note>(_notes);
    }

    public ReadOnlyObservableCollection<Note> Notes { get; }

    public ObservableCollection<string> Subjects => _sections.Subjects;

    public IReadOnlyList<string> SubjectOrder => _sections.SubjectOrder;

    public IReadOnlyList<HomeworkSection> Sections => _sections.Sections;

    public IReadOnlyList<HomeworkSection> GetSections(string subject) => _sections.GetSections(subject);

    public HomeworkSection? AddSection(string subject, string name)
    {
        var section = _sections.AddSection(subject, name);
        if (section is null)
            return null;

        Persist();
        return section;
    }

    public int RenameSection(string subject, string oldName, string newName)
    {
        if (!_sections.RenameSection(subject, oldName, newName))
            return 0;

        var subjectKey = subject.Trim();
        var oldKey = oldName.Trim();
        var newKey = newName.Trim();

        var updated = 0;
        foreach (var note in _notes)
        {
            if (!BelongsTo(note, subjectKey, oldKey))
                continue;

            note.Section = newKey;
            updated++;
        }

        Persist();
        return updated;
    }

    public int RemoveSection(string subject, string name)
    {
        if (!_sections.RemoveSection(subject, name))
            return 0;

        var subjectKey = subject.Trim();
        var nameKey = name.Trim();

        // 分类没了，里面的作业退回科目的「未分类」：删的是分类，不是作业。
        var updated = 0;
        foreach (var note in _notes)
        {
            if (!BelongsTo(note, subjectKey, nameKey))
                continue;

            note.Section = null;
            updated++;
        }

        Persist();
        return updated;
    }

    public event EventHandler? Changed;

    public Note Add(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        note.Order = _notes.Count == 0 ? 0 : _notes.Max(existing => existing.Order) + 1;
        _notes.Add(note);
        Persist();
        return note;
    }

    public bool Update(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        var index = IndexOf(note.Id);
        if (index < 0)
        {
            // 编辑表单拿的是界面上的副本，理论上不会走到这里；真走到就什么也别改。
            _logger.LogWarning("Skipped updating unknown note {Id}", note.Id);
            return false;
        }

        var target = _notes[index];
        target.ContentKind = note.ContentKind;
        target.Content = note.Content;
        target.Handwriting = note.Handwriting;
        target.Subject = note.Subject;
        target.Section = note.Section;
        target.DueDate = note.DueDate;

        // 标签是一份清单，按副本传（别让两条作业共用一个 List 实例）；null 表示这颗标签被取消了。
        target.Tags = Note.NormalizeTags(note.Tags);

        // 「已清理」是板子上的可见性标记，跟着表单里的副本走：编辑一条作业不会把它
        // 从已清理状态里放出来，那是「还原」该干的事。
        target.CleanedAt = note.CleanedAt;

        // 遗留字段是旧数据的只读快照：内容换了格式（编辑表单给的一定是富文本）就该丢掉，
        // 留着只会让下一个版本的读法继续有歧义。
        target.ContentFontSize = note.ContentFontSize;
        target.Formats = note.Formats;

        // CreatedAt 决定作业归档在哪个日期文件里、Order 决定区块内顺序，两者都不参与编辑。
        target.UpdatedAt = DateTimeOffset.Now;

        Persist();
        return true;
    }

    public bool Remove(Guid id)
    {
        var index = IndexOf(id);
        if (index < 0)
            return false;

        _notes.RemoveAt(index);
        Persist();
        return true;
    }

    /// <summary>
    ///     一次把多条标成「已清理」：作业**留在集合里**（下次 <see cref="Persist" /> 才会原样写回
    ///     它原来那个日期文件），只是主页面不再显示它。已经清过的不再重算，因此重复调用是安全的。
    /// </summary>
    public int CleanMany(IEnumerable<Guid> ids, DateTimeOffset cleanedAt)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var targets = new HashSet<Guid>(ids);
        if (targets.Count == 0)
            return 0;

        var cleaned = 0;
        foreach (var note in _notes)
        {
            if (note.CleanedAt is not null || !targets.Contains(note.Id))
                continue;

            note.CleanedAt = cleanedAt;
            cleaned++;
        }

        if (cleaned == 0)
            return 0;

        Persist();
        return cleaned;
    }

    public void Save()
    {
        Persist();
    }

    /// <summary>
    ///     把板子上现在还看得见的作业**全部收起来**：逐条打上「已清理」标记后落盘一次。
    ///     作业不从集合里拿掉、文件也不删——它只是从当前页面消失，数据和回放历史都还在，
    ///     之后可以在时空回放里按那一刻的快照还原回来。
    ///     <para>
    ///         科目与二级分类这些**定义**保留（用户清的是板子上的内容，不是自己的科目表）。
    ///     </para>
    /// </summary>
    public void ClearAll() => CleanMany(_notes.Where(note => note.CleanedAt is null).Select(note => note.Id),
        DateTimeOffset.Now);

    /// <summary>
    ///     一次把多条已经收起来的作业**放回板子**（清掉「已清理」标记），只落盘一次、只通知一次。
    ///     返回这次真正放回来的条数；一条都没命中的不落盘。
    ///     <para>
    ///         时空回放的「还原到此刻」走这里：回放只看得到那一刻仍然存在的作业，
    ///     已经单独删掉的作业不在集合里，还原不回来。
    ///     </para>
    /// </summary>
    public int RestoreMany(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var targets = new HashSet<Guid>(ids);
        if (targets.Count == 0)
            return 0;

        var restored = 0;
        foreach (var note in _notes)
        {
            if (note.CleanedAt is null || !targets.Contains(note.Id))
                continue;

            note.CleanedAt = null;
            restored++;
        }

        if (restored == 0)
            return 0;

        Persist();
        return restored;
    }

    public void SetSubjectOrder(IEnumerable<string> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        _sections.SetSubjectOrder(subjects);
        Persist();
    }

    public void Reload()
    {
        _sections.Reload();

        var notes = _noteStore.LoadAll();
        var converted = ConvertLegacyContent(notes);
        if (converted > 0)
            _noteStore.SaveAll(notes);

        _notes.Clear();
        foreach (var note in notes)
            _notes.Add(note);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     旧作业的内容迁移：从「Markdown 原文 + 偏移标注」转成富文本片段（HTML）。只改内存，
    ///     落盘交给调用方（从旧单文件搬过来的那一批正好跟搬迁一起写，不必写两遍）。
    ///     磁盘上转过了就不会再转第二次——新数据的内容格式已经是富文本，转换是幂等的。
    /// </summary>
    private int ConvertLegacyContent(IReadOnlyCollection<Note> notes)
    {
        var converted = LegacyNoteContentConverter.ConvertAll(notes);
        if (converted > 0)
            _logger.LogInformation("Converted {Count} legacy notes to rich text", converted);

        return converted;
    }

    private void Persist()
    {
        _sections.Save();

        _noteStore.SaveAll([.. _notes]);
        _logger.LogInformation("Board persisted: {Notes} notes in dated files", _notes.Count);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>这条作业是不是落在「某科目 + 某分类」里（改名/删分类时按它筛）。</summary>
    private static bool BelongsTo(Note note, string subject, string sectionName)
    {
        return string.Equals(note.Subject.Trim(), subject, StringComparison.Ordinal) &&
               string.Equals((note.Section ?? string.Empty).Trim(), sectionName, StringComparison.Ordinal);
    }

    private int IndexOf(Guid id)    {
        for (var i = 0; i < _notes.Count; i++)
            if (_notes[i].Id == id)
                return i;

        return -1;
    }
}
