using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Services.Config;

namespace Corkboard.Core.Exam;

/// <summary>
///     考试看板的服务实现：内存里一份看板清单 + 当前看板的考试集合，
///     每次变更后把**当前看板整份**写回它自己的文件（<c>data/exam/boards/&lt;Id&gt;.json</c>），
///     并把看板清单写回索引（<c>data/exam/boards.json</c>）。
///     <para>
///         每次改动都整份重写（而不是按条分文件）：一块看板是一份几十条的小表，
///         一次考试季结束就整批换掉，没有作业那种「一条一个文件、按日期归档」的必要。
///     </para>
///     <para>
///         读盘顺序：先读索引拿到看板 Id 列表，再逐块读文件。索引里有、文件不在的 Id 直接丢掉
///         （用户手工删过文件），一块都不剩时现造一块空的——界面上任何时候都要有当前看板可用。
///     </para>
/// </summary>
public sealed class ExamService : IExamService
{
    private readonly ConfigServiceBase _configService;
    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<ExamService>? _logger;

    private readonly ObservableCollection<ExamBoardDocument> _boards = [];
    private readonly ObservableCollection<ExamEntry> _exams = [];

    private ExamBoardIndexConfig _index;
    private ExamBoardDocument _activeBoard = null!;

    public ExamService(ConfigServiceBase configService, MainConfigHandler configHandler,
        ILogger<ExamService>? logger = null)
    {
        _configService = configService;
        _configHandler = configHandler;
        _logger = logger;

        Boards = new ReadOnlyObservableCollection<ExamBoardDocument>(_boards);
        Exams = new ReadOnlyObservableCollection<ExamEntry>(_exams);

        _index = _configService.LoadConfig(new ExamBoardIndexConfig());
        LoadAll();
    }

    public ReadOnlyObservableCollection<ExamBoardDocument> Boards { get; }

    public ExamBoardDocument ActiveBoard => _activeBoard;

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
        _index = _configService.LoadConfig(new ExamBoardIndexConfig());
        LoadAll();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ExamBoardDocument CreateBoard(string? title = null)
    {
        var board = new ExamBoardDocument
        {
            Id = Guid.NewGuid(),
            Title = title?.Trim() ?? string.Empty
        };

        // 先把文件落下来，再把当前看板的编辑存住——顺序反了的话切过去就找不到新文件。
        _configService.SaveConfig(board);
        _boards.Add(board);

        SaveCurrentBoard();
        _activeBoard = board;
        LoadExams(board);
        SaveIndex();

        Changed?.Invoke(this, EventArgs.Empty);
        return board;
    }

    public bool DeleteBoard(Guid id)
    {
        // 最后一块不许删：界面上永远要有一块可看的看板。
        if (_boards.Count <= 1)
            return false;

        var target = _boards.FirstOrDefault(board => board.Id == id);
        if (target is null)
            return false;

        var wasActive = ReferenceEquals(target, _activeBoard);

        // 删的是别的看板时，当前看板里还没落盘的改动先存住，别跟着一起丢。
        if (!wasActive)
            SaveCurrentBoard();

        _configService.DeleteConfig(target);
        _boards.Remove(target);

        if (wasActive)
        {
            _activeBoard = _boards[0];
            LoadExams(_activeBoard);
        }

        SaveIndex();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool SelectBoard(Guid id)
    {
        var target = _boards.FirstOrDefault(board => board.Id == id);
        if (target is null)
            return false;

        if (ReferenceEquals(target, _activeBoard))
            return true;

        SaveCurrentBoard();
        _activeBoard = target;
        LoadExams(target);
        SaveIndex();

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    ///     读盘：按索引逐块读，读不到的丢掉；一块都不剩时现造一块
    ///     （第一次启动、或从只有 <c>exams.json</c> 的老版本升级上来）。
    /// </summary>
    private void LoadAll()
    {
        _boards.Clear();

        var documents = new List<ExamBoardDocument>();
        foreach (var id in _index.BoardIds ?? [])
        {
            if (id == Guid.Empty || documents.Any(board => board.Id == id))
                continue;

            var probe = new ExamBoardDocument { Id = id };
            if (!_configService.IsConfigExists(probe))
            {
                _logger?.LogDebug("Dropped a board whose file is missing ({Id})", id);
                continue;
            }

            var document = _configService.LoadConfig(probe);

            // 索引说了算：手改 JSON 把 id 删了也不能长出第二个文件。
            document.Id = id;
            documents.Add(NormalizeDocument(document));
        }

        if (documents.Count == 0)
            documents.Add(BuildDefaultDocument());

        foreach (var document in documents)
            _boards.Add(document);

        _activeBoard = _boards.FirstOrDefault(board => board.Id == _index.ActiveBoardId) ?? _boards[0];
        LoadExams(_activeBoard);

        _index.ActiveBoardId = _activeBoard.Id;
        SaveIndex();
    }

    /// <summary>
    ///     一块看板都没有时用的那第一块：把老版本单份的 <c>data/exam/exams.json</c>
    ///     和 <c>settings.json</c> 里全局的标题 / 副标题一起搬进来，搬完把全局那三个字段清空
    ///     （它们从此归看板文件管）。
    /// </summary>
    private ExamBoardDocument BuildDefaultDocument()
    {
        var settings = _configHandler.Data.ExamBoard;
        var legacyTitle = settings.Title ?? string.Empty;
        var legacySubtitle = settings.Subtitle ?? string.Empty;
        var legacyColor = settings.SubtitleColor;

        var legacyExams = new List<ExamEntry>();
        var legacySchedule = new ExamScheduleConfig();
        if (_configService.IsConfigExists(legacySchedule))
        {
            legacyExams = _configService.LoadConfig(legacySchedule).Exams ?? [];
            _logger?.LogInformation("Migrating {Count} exams from the legacy single schedule", legacyExams.Count);
        }

        var board = new ExamBoardDocument
        {
            Id = Guid.NewGuid(),
            Title = legacyTitle,
            Subtitle = legacySubtitle,
            SubtitleColor = legacyColor
        };
        board = NormalizeDocument(board);
        board.Exams = NormalizeExams(legacyExams);

        _configService.SaveConfig(board);

        // 老数据已经搬进看板文件了，全局那份留着只会让下次读盘又把旧标题盖回来。
        if (legacyTitle.Length > 0 || legacySubtitle.Length > 0 || legacyColor is not null)
        {
            settings.Title = string.Empty;
            settings.Subtitle = string.Empty;
            settings.SubtitleColor = null;
        }

        return board;
    }

    /// <summary>
    ///     读盘时收拾一遍：科目空白的条目丢掉（看板上没法显示），其余把时间与卷面信息夹进合法区间。
    ///     手改过的看板文件不会把脏数据带进界面。
    /// </summary>
    private List<ExamEntry> NormalizeExams(IEnumerable<ExamEntry>? exams)
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

    private ExamBoardDocument NormalizeDocument(ExamBoardDocument document)
    {
        document.Normalize();
        document.Exams = NormalizeExams(document.Exams);
        return document;
    }

    /// <summary>把某块看板的考试装进界面用的活集合（元素是同一批实例，改字段两边都看得见）。</summary>
    private void LoadExams(ExamBoardDocument board)
    {
        _exams.Clear();
        foreach (var exam in board.Exams)
            _exams.Add(exam);
    }

    /// <summary>把当前看板的考试集合写回它自己的文件（不通知界面）。</summary>
    private void SaveCurrentBoard()
    {
        _activeBoard.Exams = [.. _exams];
        _configService.SaveConfig(_activeBoard);
    }

    /// <summary>把看板清单与「当前是哪一块」写回索引。</summary>
    private void SaveIndex()
    {
        _index.BoardIds = [.. _boards.Select(board => board.Id)];
        _index.ActiveBoardId = _activeBoard.Id;
        _configService.SaveConfig(_index);
    }

    private void Persist()
    {
        SaveCurrentBoard();
        SaveIndex();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
