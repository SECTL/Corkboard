using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Abstraction.Services;
using Corkboard.Core.Models.Board;

namespace Corkboard.Core.Services.Board;

/// <summary>
///     作业板领域的实现：内存里维护作业、类型、科目三份集合。
///     <para>
///         落盘分两处，都是业务数据、都在 <c>data/board/</c>（与设置文件 <c>data/config/</c> 分开）：
///         类型与科目写单份 <c>board.json</c>；**作业本身按创建日期分文件**，
///         落在 <c>&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/notes.json</c>，由 <see cref="IBoardNoteStore" /> 负责。
///     </para>
/// </summary>
public class BoardService : IBoardService
{
    private readonly ConfigServiceBase _configService;
    private readonly IBoardNoteStore _noteStore;
    private readonly ILogger<BoardService> _logger;
    private readonly ObservableCollection<BoardNote> _notes;
    private readonly List<string> _subjectOrder;
    private BoardConfig _config;

    public BoardService(ConfigServiceBase configService, IBoardNoteStore noteStore, ILogger<BoardService> logger)
    {
        _configService = configService;
        _noteStore = noteStore;
        _logger = logger;

        // 定义文件在加载前是否已存在，决定了当前定义是「用户的」还是「刚打底的默认值」。
        var definitionExists = _configService.IsConfigExists(new BoardConfig());
        _config = _configService.LoadConfig(new BoardConfig());

        var notes = _noteStore.LoadAll();
        if (notes.Count == 0 && _noteStore.TryLoadLegacy() is { } legacy)
        {
            // 归档目录还空着、旧版单文件有货：作业摊到新的日期目录下，定义按下面的规则并回。
            _logger.LogInformation("Migrating {Count} notes into dated folders", legacy.Notes.Count);
            if (legacy.Notes.Count > 0)
                _noteStore.SaveAll(legacy.Notes);
            MergeLegacyDefinition(legacy, definitionExists);

            notes = legacy.Notes;
            // 定义（类型/科目）也跟着搬过一次，立刻落盘，避免下次启动重复走搬迁。
            _configService.SaveConfig(_config);
        }

        _notes = new ObservableCollection<BoardNote>(notes);
        Types = new ObservableCollection<BoardTypeDef>(_config.Types);
        Subjects = new ObservableCollection<string>(_config.Subjects);
        _subjectOrder = [.. _config.SubjectOrder];
        Notes = new ReadOnlyObservableCollection<BoardNote>(_notes);
    }

    public ReadOnlyObservableCollection<BoardNote> Notes { get; }

    public ObservableCollection<BoardTypeDef> Types { get; }

    public ObservableCollection<string> Subjects { get; }

    public IReadOnlyList<string> SubjectOrder => _subjectOrder;

    public event EventHandler? Changed;

    public BoardNote Add(BoardNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        note.Order = _notes.Count == 0 ? 0 : _notes.Max(existing => existing.Order) + 1;
        _notes.Add(note);
        Persist();
        return note;
    }

    public bool Update(BoardNote note)
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
        target.TypeId = note.TypeId;
        target.Content = note.Content;
        target.ContentFontSize = note.ContentFontSize;
        target.Subject = note.Subject;
        target.Values = new Dictionary<string, string>(note.Values);

        // 格式标注按引用并入会让编辑表单的草稿继续牵着落盘数据，所以整份克隆一份。
        target.Formats = [.. note.Formats.Select(range => range.Clone())];

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

    public BoardTypeDef? FindType(Guid? typeId)
    {
        return typeId is { } value ? Types.FirstOrDefault(type => type.Id == value) : null;
    }

    public void Save()
    {
        Persist();
    }

    public void SetSubjectOrder(IEnumerable<string> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        _subjectOrder.Clear();
        foreach (var subject in subjects)
        {
            var name = subject.Trim();

            // 空科目（界面上是「未分类」区块）、重复项都只留第一次出现的那个。
            if (name.Length > 0 && !_subjectOrder.Contains(name, StringComparer.Ordinal))
            {
                _subjectOrder.Add(name);
            }
        }

        // 已经从科目清单里删掉的科目没必要占着位置；留着也不会显示，只是脏数据。
        _subjectOrder.RemoveAll(name => !Subjects.Any(s => string.Equals(s.Trim(), name, StringComparison.Ordinal)));

        Persist();
    }

    public void Reload()
    {
        _config = _configService.LoadConfig(new BoardConfig());

        _notes.Clear();
        foreach (var note in _noteStore.LoadAll())
            _notes.Add(note);

        Types.Clear();
        foreach (var type in _config.Types)
            Types.Add(type);

        Subjects.Clear();
        foreach (var subject in _config.Subjects)
            Subjects.Add(subject);

        _subjectOrder.Clear();
        _subjectOrder.AddRange(_config.SubjectOrder);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     把旧版单文件里的类型与科目并回当前定义。**只在定义文件本来不存在时**才接管：
    ///     那说明这是我们第一次为这个用户建定义，旧文件里的就是他原来的类型与科目。
    ///     文件已存在则表示用户已经在设置里管过自己的定义（哪怕他故意清空了），一律不覆盖。
    /// </summary>
    private void MergeLegacyDefinition(LegacyBoardFile legacy, bool definitionExists)
    {
        if (!definitionExists)
        {
            if (legacy.Types.Count > 0)
                _config.Types = legacy.Types;

            if (legacy.Subjects.Count > 0)
                _config.Subjects = legacy.Subjects;
        }

        // 旧文件由存储层负责删：它才知道旧文件到底在哪个路径。
        _noteStore.TryDeleteLegacy();
    }

    private void Persist()
    {
        _config.Types = [.. Types];
        _config.Subjects = [.. Subjects];
        _config.SubjectOrder = [.. _subjectOrder];
        _configService.SaveConfig(_config);

        _noteStore.SaveAll([.. _notes]);
        _logger.LogInformation("Board persisted: {Notes} notes in dated files, {Types} types",
            _notes.Count, Types.Count);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private int IndexOf(Guid id)
    {
        for (var i = 0; i < _notes.Count; i++)
            if (_notes[i].Id == id)
                return i;

        return -1;
    }
}
