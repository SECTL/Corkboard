using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Abstraction.Services;
using Corkboard.Core.Models.Board;
using Corkboard.Shared;

namespace Corkboard.Core.Services.Board;

/// <summary>
///     按**创建日期**归档作业：<c>data/board/&lt;年&gt;/&lt;月&gt;/&lt;日&gt;/notes.json</c>。
///     <para>
///         作业是业务数据，既不放 <c>settings.json</c>，也不和设置文件同处 <c>data/config/</c>。
///         一天一个文件，翻当天的作业不用先读全量。
///     </para>
/// </summary>
public sealed class BoardNoteStore : IBoardNoteStore
{
    /// <summary>作业数据根目录名，位于 <c>data/</c> 之下、与 <c>config/</c> 平级。</summary>
    public const string RootDirectoryName = "board";

    /// <summary>单个日期目录里的作业文件名。</summary>
    public const string NotesFileName = "notes.json";

    private readonly ILogger<BoardNoteStore> _logger;
    private readonly string _rootDirectory;
    private readonly string _legacyFilePath;

    public BoardNoteStore(ILogger<BoardNoteStore> logger)
        : this(logger, RootDirectory, LegacyBoardFile.FilePath)
    {
    }

    /// <summary>测试用重载：归档根目录与旧文件位置都可以换掉，避免碰真实数据根。</summary>
    public BoardNoteStore(ILogger<BoardNoteStore> logger, string rootDirectory, string? legacyFilePath = null)
    {
        _logger = logger;
        _rootDirectory = rootDirectory;
        _legacyFilePath = legacyFilePath ?? LegacyBoardFile.FilePath;
    }

    /// <summary>生产环境的归档根目录：<c>data/board/</c>。</summary>
    public static string RootDirectory => Utils.GetDirectoryPath(RootDirectoryName);

    public IReadOnlyList<BoardNote> LoadAll()
    {
        if (!Directory.Exists(_rootDirectory))
            return [];

        var notes = new List<BoardNote>();
        foreach (var file in EnumerateNoteFiles(_rootDirectory))
        {
            try
            {
                var json = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                var day = JsonSerializer.Deserialize<BoardDayFile>(json, ConfigServiceBase.JsonOptions);
                if (day is not null)
                    notes.AddRange(day.Notes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // 单天文件坏掉只跳过这一天：把坏文件留在原地才有机会被人看，其余作业照常可用。
                _logger.LogWarning(ex, "Unable to read board notes file: {Path}", file);
            }
        }

        return [.. notes.OrderBy(note => note.CreatedAt)];
    }

    public void SaveAll(IReadOnlyCollection<BoardNote> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in notes.GroupBy(GetNoteDirectory))
        {
            var directory = Path.Combine(_rootDirectory, group.Key);
            var file = Path.Combine(directory, NotesFileName);
            Directory.CreateDirectory(directory);
            WriteAtomically(file, new BoardDayFile { Notes = [.. group] });
            written.Add(Path.GetFullPath(file));
        }

        RemoveStaleFiles(written);
        _logger.LogInformation("Board persisted: {Notes} notes across {Days} day files", notes.Count, written.Count);
    }

    public LegacyBoardFile? TryLoadLegacy() => LegacyBoardFile.TryRead(_logger, _legacyFilePath);

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
    public static string GetNoteDirectory(BoardNote note)
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

    /// <summary>
    ///     删掉这次没有写过的 <c>notes.json</c>：作业被删掉或创建日期变了之后，
    ///     旧文件不该留在目录里。只认 <c>年/月/日/notes.json</c> 这个形状，不碰别的文件。
    /// </summary>
    private void RemoveStaleFiles(HashSet<string> written)
    {
        foreach (var file in EnumerateNoteFiles(_rootDirectory))
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
                _logger.LogWarning(ex, "Unable to remove stale board notes file: {Path}", file);
            }
        }
    }

    private static IEnumerable<string> EnumerateNoteFiles(string root)
    {
        return !Directory.Exists(root)
            ? []
            : Directory.EnumerateFiles(root, NotesFileName, SearchOption.AllDirectories);
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

    /// <summary>临时文件 + 原子替换，避免进程中途被杀留下截断的 JSON。</summary>
    private static void WriteAtomically<T>(string filePath, T payload)
    {
        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(payload, ConfigServiceBase.JsonOptions));
        File.Move(temporaryPath, filePath, overwrite: true);
    }
}
