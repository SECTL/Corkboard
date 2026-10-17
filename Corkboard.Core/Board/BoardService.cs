using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Board.History;
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
///     <para>
///         每次变更还会往历史日志里记一条（<see cref="INoteHistoryStore" />，在 <c>data/history/board/</c>）：
///         那是时空回放的数据源。历史只记录「什么时候对哪条作业做了什么」，当前文件才是事实本身；
///         删掉的作业当前文件会没，但历史里的整份快照还在，所以回放与「还原到此刻」都能把它找回来。
///     </para>
/// </summary>
public class BoardService : IBoardService
{
    private readonly INoteStore _noteStore;
    private readonly HomeworkSectionCatalog _sections;
    private readonly ILogger<BoardService> _logger;
    private readonly INoteHistoryStore? _history;
    private readonly ObservableCollection<Note> _notes;

    // 连续保存合并用：上一条 Update 事件针对谁、发生在什么时候。别的作业一插进来就自动断开。
    private Guid _lastUpdateNote = Guid.Empty;
    private DateTimeOffset _lastUpdateAt;

    /// <param name="history">
    ///     历史日志。为 <c>null</c> 表示纯内存里跑（Core 的单测一直这么构造），此时不记任何历史。
    ///     DI 里永远给得上（<see cref="Core.Services.CoreRuntimeServiceCollectionExtensions" /> 注册了它）。
    /// </param>
    public BoardService(INoteStore noteStore, HomeworkSectionCatalog sections, ILogger<BoardService> logger,
        INoteHistoryStore? history = null)
    {
        _noteStore = noteStore;
        _sections = sections;
        _logger = logger;
        _history = history;

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

        SeedHistory();
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

        // 新增事件记在作业自己的创建时刻上：回放里它「从那一刻起就在板子上」。
        RecordCreate(note, note.CreatedAt);

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

        RecordUpdate(target);

        Persist();
        return true;
    }

    public bool Remove(Guid id)
    {
        var index = IndexOf(id);
        if (index < 0)
            return false;

        // 先留一份完整快照再摘：删掉之后当前文件就没了，回放里要按这一份把它画回来。
        RecordDelete(_notes[index]);

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
            RecordClean(note, cleanedAt);
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
            RecordRestore(note);
            restored++;
        }

        if (restored == 0)
            return 0;

        Persist();
        return restored;
    }

    /// <summary>
    ///     把板子上的「看得见的作业」整份换成 <paramref name="notes" />（时空回放的「还原到此刻」走这里）：
    ///     快照里有、板子上没有的**重新插回来**（连 <see cref="Note.CreatedAt" /> 一起，归档回它原来那天）；
    ///     快照里没有、板子上还看得见的**收起来**；内容不一样的按快照覆盖。
    ///     <para>
    ///         与 <see cref="RestoreMany" /> 的区别：那条只会把「已经收起来的作业」放出来，
    ///     被真正删掉的作业它救不回来；这里认的是历史快照，所以逐条删掉的也能放回板子。
    ///     </para>
    /// </summary>
    public int RestoreSnapshot(IReadOnlyList<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var wanted = new Dictionary<Guid, Note>();
        foreach (var note in notes)
            wanted[note.Id] = note;

        var changed = 0;
        var now = DateTimeOffset.Now;

        // 1) 快照里没有、当前还看得见的：收起来（作业与历史都还在，只是不在这一刻的板面上）。
        foreach (var current in _notes.Where(note => note.CleanedAt is null && !wanted.ContainsKey(note.Id)).ToList())
        {
            current.CleanedAt = now;
            RecordClean(current, now);
            changed++;
        }

        // 2) 快照里有的：该插的插、该改的改、该放出来的放出来。
        foreach (var wantedNote in notes)
        {
            var current = _notes.FirstOrDefault(note => note.Id == wantedNote.Id);
            if (current is null)
            {
                var inserted = CopyForRestore(wantedNote);
                inserted.Order = _notes.Count == 0 ? 0 : _notes.Max(note => note.Order) + 1;
                _notes.Add(inserted);
                RecordCreate(inserted, now);
                changed++;
                continue;
            }

            var contentChanged = CopyForRestore(wantedNote, current);
            if (contentChanged)
            {
                current.CleanedAt = null;
                current.UpdatedAt = now;
                RecordUpdate(current);
                changed++;
            }
            else if (current.CleanedAt is not null)
            {
                current.CleanedAt = null;
                RecordRestore(current);
                changed++;
            }
        }

        if (changed == 0)
            return 0;

        Persist();
        return changed;
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

        // 云同步恢复会整份换掉内存里的作业：历史里没有的（从别的设备新带进来的）要补一条基线，
        // 内容被换过的要补一条修改，否则回放重放出来的还是恢复之前的板面。
        SeedHistory();

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

    /// <summary>
    ///     给历史补基线：第一次跑这套历史（日志还是空的）时，把现有作业按创建时刻各记一条「新增」；
    ///     之后只在内存里的作业跟历史对不上时补——云同步整份恢复会带进历史里没有的作业、也会换掉某几条的内容，
    ///     不补的话回放重放出来的还是恢复之前的板面。更早的增删改没有记录，回放不出来
    ///     ——用户已确认「历史从本版开始」。
    /// </summary>
    private void SeedHistory()
    {
        if (_history is null || _notes.Count == 0)
            return;

        try
        {
            var entries = _history.LoadAll();
            if (entries.Count == 0)
            {
                foreach (var note in _notes.OrderBy(note => note.CreatedAt))
                    RecordCreate(note, note.CreatedAt);

                _logger.LogInformation("Seeded note history with {Count} baseline entries", _notes.Count);
                return;
            }

            var knownIds = entries.Select(entry => entry.NoteId).ToHashSet();
            var visible = BoardHistory
                .StateAt(entries, DateTimeOffset.Now, _history.LoadSnapshot)
                .ToDictionary(note => note.Id);

            var seeded = 0;
            foreach (var note in _notes.OrderBy(note => note.CreatedAt))
            {
                if (!knownIds.Contains(note.Id))
                {
                    RecordCreate(note, note.CreatedAt);
                    seeded++;
                }
                else if (visible.TryGetValue(note.Id, out var current) && !HasSameContent(note, current))
                {
                    RecordUpdate(note, DateTimeOffset.Now);
                    seeded++;
                }
            }

            if (seeded > 0)
                _logger.LogInformation("Seeded note history with {Count} missing entries", seeded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not seed the note history");
        }
    }

    /// <summary>
    ///     判断两条作业的内容是否一致（不比 <see cref="Note.UpdatedAt" /> / <see cref="Note.Order" /> 这类簿记字段）。
    ///     历史播种与云同步恢复之后靠它决定「要不要补一条修改」。
    /// </summary>
    private static bool HasSameContent(Note left, Note right)
    {
        return left.ContentKind == right.ContentKind
            && string.Equals(left.Content, right.Content, StringComparison.Ordinal)
            && string.Equals(left.Handwriting, right.Handwriting, StringComparison.Ordinal)
            && string.Equals(left.Subject, right.Subject, StringComparison.Ordinal)
            && string.Equals(left.Section, right.Section, StringComparison.Ordinal)
            && left.DueDate == right.DueDate
            && left.CleanedAt == right.CleanedAt
            && left.ContentFontSize == right.ContentFontSize
            && SameTags(left.Tags, right.Tags);
    }

    /// <summary>记一条「新增」。事件时刻用作业自己的创建时刻，回放里它从那一刻起就看得见。</summary>
    private void RecordCreate(Note note, DateTimeOffset at)
    {
        if (_history is not { } history)
            return;

        history.Append(new NoteHistoryEntry(at, note.Id, NoteHistoryAction.Create, history.SaveSnapshot(note)));
    }

    /// <summary>
    ///     记一条「修改」。同一条作业连续保存（间隔不超过设置的合并窗口）只留最后一版：
    ///     先把上一行追回去改掉，改不动（中间夹了别的作业的变更）才老实追加一条。
    /// </summary>
    private void RecordUpdate(Note note, DateTimeOffset? at = null)
    {
        if (_history is not { } history)
            return;

        var moment = at ?? DateTimeOffset.Now;
        var entry = new NoteHistoryEntry(moment, note.Id, NoteHistoryAction.Update, history.SaveSnapshot(note));

        var merged = _lastUpdateNote == note.Id
                     && moment - _lastUpdateAt <= history.CoalesceWindow
                     && moment - _lastUpdateAt >= TimeSpan.Zero
                     && history.ReplaceLast(entry);

        if (!merged)
            history.Append(entry);

        _lastUpdateNote = note.Id;
        _lastUpdateAt = moment;
    }

    /// <summary>记一条「删除」：删之前快照必须已经存好，否则回放时这条作业只剩一个 Id。</summary>
    private void RecordDelete(Note note)
    {
        if (_history is not { } history)
            return;

        history.Append(new NoteHistoryEntry(DateTimeOffset.Now, note.Id, NoteHistoryAction.Delete,
            history.SaveSnapshot(note)));
    }

    private void RecordClean(Note note, DateTimeOffset at)
    {
        _history?.Append(new NoteHistoryEntry(at, note.Id, NoteHistoryAction.Clean));
    }

    private void RecordRestore(Note note, DateTimeOffset? at = null)
    {
        _history?.Append(new NoteHistoryEntry(at ?? DateTimeOffset.Now, note.Id, NoteHistoryAction.Restore));
    }

    /// <summary>按快照插一条作业回来（新对象，连 Id 与 CreatedAt 都照抄，归档回它原来那天）。</summary>
    private static Note CopyForRestore(Note source)
    {
        var target = new Note { Id = source.Id, CreatedAt = source.CreatedAt };
        CopyForRestore(source, target);
        return target;
    }

    /// <summary>把快照的内容盖到现有作业上，返回内容有没有真的变（只有变了才值得记一条修改）。</summary>
    private static bool CopyForRestore(Note source, Note target)
    {
        var changed = target.ContentKind != source.ContentKind
                      || !string.Equals(target.Content, source.Content, StringComparison.Ordinal)
                      || !string.Equals(target.Handwriting, source.Handwriting, StringComparison.Ordinal)
                      || !string.Equals(target.Subject, source.Subject, StringComparison.Ordinal)
                      || !string.Equals(target.Section, source.Section, StringComparison.Ordinal)
                      || target.DueDate != source.DueDate
                      || !SameTags(target.Tags, source.Tags)
                      || target.ContentFontSize != source.ContentFontSize;

        target.ContentKind = source.ContentKind;
        target.Content = source.Content;
        target.Handwriting = source.Handwriting;
        target.Subject = source.Subject;
        target.Section = source.Section;
        target.DueDate = source.DueDate;
        target.Tags = Note.NormalizeTags(source.Tags);
        target.CleanedAt = source.CleanedAt;
        target.ContentFontSize = source.ContentFontSize;

        return changed;
    }

    private static bool SameTags(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return left.SequenceEqual(right, StringComparer.Ordinal);
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
