using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;
using Corkboard.Shared;
using Corkboard.Core.Notes.Legacy;

namespace Corkboard.Core.Notes;

/// <summary>
///     按**创建日期**归档作业：<c>data/board/&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/&lt;作业 Id&gt;.cbnote</c>。
///     <para>
///         一条作业一个专有格式文件（见 <see cref="NoteFileFormat"/>）：元数据、富文本内容、手写笔迹
///         全在同一个文件里，不再挤在 <c>notes.json</c> 那种「一天一个大文件」的容器里。
///         好处是写一条作业只碰一个文件（不怕互相截断）、删一条就删一个文件、手写笔迹也不再是
///         JSON 里被转义的一大段字符串。
///     </para>
///     <para>
///         兼容旧数据：旧版本的 <c>&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/notes.json</c> 仍然会被读进来
///         （同 Id 以新文件为准），下次落盘时自动改写成 <c>.cbnote</c> 并把旧文件删掉；
///         解析失败、要人看的那种旧文件原地保留、不删。
///     </para>
/// </summary>
public sealed class NoteStore : INoteStore
{
    /// <summary>作业数据根目录名，位于 <c>data/</c> 之下、与 <c>config/</c> 平级。</summary>
    public const string RootDirectoryName = "board";

    /// <summary>旧版本的「一天一个文件」名字：只用于读取兼容与迁移后清理，不再写入。</summary>
    public const string NotesFileName = "notes.json";

    private readonly ILogger<NoteStore> _logger;
    private readonly string _rootDirectory;
    private readonly string _legacyFilePath;

    /// <summary>解析失败、要留着给人看的旧 <c>notes.json</c>：迁移时绝不删它们。</summary>
    private readonly HashSet<string> _unreadableLegacyFiles = new(StringComparer.OrdinalIgnoreCase);

    public NoteStore(ILogger<NoteStore> logger)
        : this(logger, RootDirectory, LegacyNoteFile.FilePath)
    {
    }

    /// <summary>测试用重载：归档根目录与旧文件位置都可以换掉，避免碰真实数据根。</summary>
    public NoteStore(ILogger<NoteStore> logger, string rootDirectory, string? legacyFilePath = null)
    {
        _logger = logger;
        _rootDirectory = rootDirectory;
        _legacyFilePath = legacyFilePath ?? LegacyNoteFile.FilePath;
    }

    /// <summary>生产环境的归档根目录：<c>data/board/</c>。</summary>
    public static string RootDirectory => Utils.GetDirectoryPath(RootDirectoryName);

    public IReadOnlyList<Note> LoadAll()
    {
        if (!Directory.Exists(_rootDirectory))
            return [];

        // 同 Id 可能同时存在新文件与旧 notes.json（旧文件还没被迁移）：留更新的一份。
        var byId = new Dictionary<Guid, Note>();

        foreach (var file in EnumerateNoteFiles(_rootDirectory, "*" + NoteFileFormat.FileExtension))
        {
            try
            {
                var text = File.ReadAllText(file);
                if (!NoteFileFormat.TryDecode(text, out var note))
                {
                    _logger.LogWarning("Unable to read board note file: {Path}", file);
                    continue;
                }

                AddOrReplace(byId, note);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 单条作业坏掉只跳过它：把坏文件留在原地，其余作业照常可用。
                _logger.LogWarning(ex, "Unable to read board note file: {Path}", file);
            }
        }

        foreach (var file in EnumerateNoteFiles(_rootDirectory, NotesFileName))
        {
            try
            {
                var json = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                var day = JsonSerializer.Deserialize<NoteDayFile>(json, ConfigServiceBase.JsonOptions);
                if (day is null)
                    continue;

                foreach (var note in day.Notes)
                    AddOrReplace(byId, note);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // 旧天文件坏掉只跳过这一天，并且记住它：迁移时别把它删掉，留给人看。
                _unreadableLegacyFiles.Add(Path.GetFullPath(file));
                _logger.LogWarning(ex, "Unable to read legacy board notes file: {Path}", file);
            }
        }

        return [.. byId.Values.OrderBy(note => note.CreatedAt)];
    }

    public void SaveAll(IReadOnlyCollection<Note> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in notes.GroupBy(GetNoteDirectory))
        {
            var directory = Path.Combine(_rootDirectory, group.Key);
            Directory.CreateDirectory(directory);

            foreach (var note in group)
            {
                var file = Path.Combine(directory, NoteFileFormat.GetFileName(note));

                // 写盘前先挡一道：内存里这条作业是「空内容 + 空笔迹」，磁盘上却有笔迹——
                // 说明加载链路丢了笔迹（或编辑表单以打字态存了一次空的），这时候覆盖下去
                // 会把用户的手写整段抹掉，而且不可恢复。宁可保留磁盘上的旧笔迹。
                PreserveInkInsteadOfEmptyOverwrite(note, file);

                WriteAtomically(file, NoteFileFormat.Encode(note));
                written.Add(Path.GetFullPath(file));
            }
        }

        RemoveStaleNoteFiles(written);
        RemoveMigratedLegacyFiles();

        _logger.LogInformation("Board persisted: {Notes} note files across {Days} day directories",
            notes.Count, written.Count);
    }

    /// <summary>
    ///     防止「空内容覆盖有笔迹的旧文件」：只在内存里的作业既没有正文也没有笔迹时才检查磁盘，
    ///     磁盘上有笔迹就把旧笔迹（以及对应的 kind / 正文）接回来，日志里留一条警告。
    ///     <para>
    ///         踩过的坑：编辑一条手写作业时表单落在「打字」页签，保存时按空 HTML 写回去，
    ///         `.cbnote` 里的 ink 块被抹掉，重启后作业就剩一张白卡片。内容丢失不可恢复，
    ///         所以这一层兜底放在存储层，不管调用方走哪条路都拦得住。
    ///     </para>
    /// </summary>
    private void PreserveInkInsteadOfEmptyOverwrite(Note note, string file)
    {
        if (!string.IsNullOrWhiteSpace(note.Content) || !string.IsNullOrWhiteSpace(note.Handwriting))
            return;

        if (!File.Exists(file))
            return;

        try
        {
            var text = File.ReadAllText(file);
            if (!NoteFileFormat.TryDecode(text, out var existing)
                || string.IsNullOrWhiteSpace(existing.Handwriting))
            {
                return;
            }

            note.Handwriting = existing.Handwriting;
            note.ContentKind = existing.ContentKind;

            _logger.LogWarning(
                "Refusing to overwrite handwriting with empty content for note {Id}; kept the ink on disk",
                note.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Unable to inspect the existing note file before overwriting: {Path}", file);
        }
    }

    public LegacyNoteFile? TryLoadLegacy() => LegacyNoteFile.TryRead(_logger, _legacyFilePath);

    public void TryDeleteLegacy()
    {
        try
        {
            if (File.Exists(_legacyFilePath))
                File.Delete(_legacyFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Unable to remove the legacy board file: {Path}", _legacyFilePath);
        }
    }

    /// <summary>某个作业该落到哪个日期目录：按创建时间（本地时区）的年/月/日分层，相对归档根目录。</summary>
    public static string GetNoteDirectory(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return Path.Combine(GetDateSegments(note.CreatedAt));
    }

    /// <summary>年、月、日三段目录名，供调用方拼路径或断言。</summary>
    public static string[] GetDateSegments(DateTimeOffset createdAt)
    {
        var local = createdAt.ToLocalTime().DateTime;
        return
        [
            local.Year.ToString("D4", CultureInfo.InvariantCulture),
            local.Month.ToString("D2", CultureInfo.InvariantCulture),
            local.Day.ToString("D2", CultureInfo.InvariantCulture)
        ];
    }

    private static void AddOrReplace(Dictionary<Guid, Note> byId, Note note)
    {
        if (byId.TryGetValue(note.Id, out var existing))
        {
            if (note.UpdatedAt <= existing.UpdatedAt)
                return;
        }

        byId[note.Id] = note;
    }

    /// <summary>
    ///     删掉这次没有写过的 <c>.cbnote</c>：作业被删掉或创建日期变了之后，旧文件不该留在目录里。
    ///     只认 <c>&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/*.cbnote</c> 这个形状，不碰别的文件。
    /// </summary>
    private void RemoveStaleNoteFiles(HashSet<string> written)
    {
        foreach (var file in EnumerateNoteFiles(_rootDirectory, "*" + NoteFileFormat.FileExtension))
        {
            if (written.Contains(Path.GetFullPath(file)))
                continue;

            try
            {
                File.Delete(file);
                RemoveEmptyDateDirectories(Path.GetDirectoryName(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Unable to remove stale board note file: {Path}", file);
            }
        }
    }

    /// <summary>
    ///     迁移收尾：旧 <c>notes.json</c> 里的作业这次已经全部读进内存、并按新格式写回去了，
    ///     所以旧文件可以删掉——**除非**它当年就是解析失败的（留着给人看）。
    /// </summary>
    private void RemoveMigratedLegacyFiles()
    {
        foreach (var file in EnumerateNoteFiles(_rootDirectory, NotesFileName).ToArray())
        {
            // 删之前现读一遍：读不出来的旧文件（损坏 / 半截）原地留着给人看，
            // 不能因为「这次调用没加载过它」就当成已迁移。这样即便调用方是另一个 NoteStore 实例
            // （云同步会临时 new 一个来 SaveAll）也不会误删。
            if (!IsReadableLegacyFile(file))
            {
                _logger.LogWarning("Keeping unreadable legacy notes file: {Path}", file);
                continue;
            }

            try
            {
                File.Delete(file);
                RemoveEmptyDateDirectories(Path.GetDirectoryName(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Unable to remove migrated legacy notes file: {Path}", file);
            }
        }
    }

    private static bool IsReadableLegacyFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return true;

            return JsonSerializer.Deserialize<NoteDayFile>(json, ConfigServiceBase.JsonOptions) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>
    ///     找出作业根目录下所有指定文件名的文件。
    ///     <para>
    ///         <b>旧版回收目录 <c>_trash</c> 整个子树仍然排除。</b>现在的清理只给作业打「已清理」
    ///         标记、不再往里写东西，但用户机器上可能还留着以前归档的批次；排除它既是不让那些作业
    ///         被当成活作业读回界面，也是不让 <see cref="SaveAll" /> 的「删掉这一轮没写过的文件」
    ///         把它清掉。
    ///     </para>
    /// </summary>
    private static IEnumerable<string> EnumerateNoteFiles(string root, string searchPattern)
    {
        if (!Directory.Exists(root))
            return [];

        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);

        // 旧版回收目录名。清理不再往里写，但历史上可能已经存在，仍然要整棵子树排除。
        const string legacyArchiveDirectoryName = "_trash";
        var archivePrefix = Path.Combine(rootFull, legacyArchiveDirectoryName) + Path.DirectorySeparatorChar;

        return Directory
            .EnumerateFiles(root, searchPattern, SearchOption.AllDirectories)
            .Where(file => !Path.GetFullPath(file).StartsWith(archivePrefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>删完文件后把空掉的「日 / 月 / 年」目录逐级回收，避免留一堆空壳。</summary>
    private void RemoveEmptyDateDirectories(string? directory)
    {
        var rootFull = Path.GetFullPath(_rootDirectory).TrimEnd(Path.DirectorySeparatorChar);
        while (!string.IsNullOrWhiteSpace(directory)
               && !string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),
                   rootFull, StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(directory)
               && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    /// <summary>临时文件 + 原子替换，避免进程中途被杀留下截断的作业文件。</summary>
    private static void WriteAtomically(string filePath, string payload)
    {
        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, payload, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, filePath, overwrite: true);
    }
}
