using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Corkboard.Core.Notes;
using Microsoft.Extensions.Logging;

namespace Corkboard.Core.Board.History;

/// <summary>
///     <see cref="INoteHistoryStore" /> 的默认实现：文件系统上的追加日志 + 内容快照库。
///     <para>
///         布局（在数据根下，刻意<strong>不</strong>放在 <c>data/board/</c> 里：
///         云同步按整个 <c>data/board</c> 打包上传，历史是给回放用的派生数据，
///         跟上去只会把备份涨得很大，所以单独放 <c>data/history/board/</c>）：
///         <code>
///         data/history/board/
///         ├── 2026-10-18.jsonl        # 一天一个文件，一行一条事件
///         └── blobs/ab/abc…def.br     # 内容快照，按 SHA-256 去重、Brotli 压缩
///         </code>
///     </para>
///     <para>
///         写文件都走同一把锁：后台上传/清理可能从线程池碰它，而界面线程同时在看回放。
///         日志只追加，替换最后一行（合并连续保存）是唯一的改写，靠记下最后一行在文件里的起点完成。
///     </para>
/// </summary>
public sealed class NoteHistoryStore : INoteHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly ILogger<NoteHistoryStore>? _logger;
    private readonly string _root;
    private readonly string _blobsDirectory;
    private readonly object _sync = new();

    // 最后写下的那一行的位置：合并连续保存时要把它整行换掉。
    private string? _lastFilePath;
    private long _lastOffset;
    private NoteHistoryEntry? _lastEntry;

    public NoteHistoryStore(ILogger<NoteHistoryStore>? logger = null, string? root = null)
    {
        _logger = logger;
        _root = root ?? Corkboard.Shared.Utils.GetDirectoryPath("history", "board");
        _blobsDirectory = Path.Combine(_root, "blobs");
    }

    private TimeSpan _coalesceWindow = BoardHistoryPolicy.ClampCoalesceWindow(BoardHistoryPolicy.DefaultCoalesceMinutes);

    /// <inheritdoc />
    public TimeSpan CoalesceWindow
    {
        get => _coalesceWindow;
        set => _coalesceWindow = value < BoardHistoryPolicy.ClampCoalesceWindow(BoardHistoryPolicy.MinCoalesceMinutes)
            ? BoardHistoryPolicy.ClampCoalesceWindow(BoardHistoryPolicy.MinCoalesceMinutes)
            : value > BoardHistoryPolicy.ClampCoalesceWindow(BoardHistoryPolicy.MaxCoalesceMinutes)
                ? BoardHistoryPolicy.ClampCoalesceWindow(BoardHistoryPolicy.MaxCoalesceMinutes)
                : value;
    }

    private int _retentionDays = BoardHistoryPolicy.DefaultRetentionDays;

    /// <inheritdoc />
    public int RetentionDays
    {
        get => _retentionDays;
        set => _retentionDays = BoardHistoryPolicy.ClampRetentionDays(value);
    }

    private long _maxBytes = BoardHistoryPolicy.ToBytes(BoardHistoryPolicy.DefaultMaxSizeMb);

    /// <inheritdoc />
    public long MaxBytes
    {
        get => _maxBytes;
        set => _maxBytes = Math.Clamp(value,
            BoardHistoryPolicy.ToBytes(BoardHistoryPolicy.MinMaxSizeMb),
            BoardHistoryPolicy.ToBytes(BoardHistoryPolicy.MaxMaxSizeMb));
    }

    /// <inheritdoc />
    public IReadOnlyList<NoteHistoryEntry> LoadAll()
    {
        lock (_sync)
            return LoadAllLocked();
    }

    /// <inheritdoc />
    public void Append(NoteHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_sync)
        {
            try
            {
                var path = DayFilePath(entry.At);
                var offset = File.Exists(path) ? new FileInfo(path).Length : 0;
                var bytes = SerializeLine(entry);

                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    stream.Write(bytes);

                RememberTail(path, offset, entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Could not append the note history entry for {NoteId}", entry.NoteId);
            }
        }
    }

    /// <inheritdoc />
    public bool ReplaceLast(NoteHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_sync)
        {
            if (_lastFilePath is null || _lastEntry is null)
                return false;

            // 只替换「同一条作业、同一天」的最后一行；别的作业刚写过就老老实实追加。
            if (_lastEntry.NoteId != entry.NoteId
                || !string.Equals(_lastFilePath, DayFilePath(entry.At), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                var bytes = SerializeLine(entry);

                using (var stream = new FileStream(_lastFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    if (stream.Length < _lastOffset)
                        return false;

                    stream.SetLength(_lastOffset);
                    stream.Position = _lastOffset;
                    stream.Write(bytes);
                }

                _lastEntry = entry;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Could not merge the previous history entry for {NoteId}", entry.NoteId);
                return false;
            }
        }
    }

    /// <inheritdoc />
    public string SaveSnapshot(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        var snapshot = NoteSnapshot.FromNote(note);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var path = BlobPath(hash);

        lock (_sync)
        {
            if (File.Exists(path))
                return hash;

            try
            {
                var directory = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(directory);

                var temporary = path + ".tmp";
                using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var compressed = new BrotliStream(file, CompressionLevel.Optimal))
                    compressed.Write(bytes);

                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Could not save the history snapshot for {NoteId}", note.Id);
            }

            return hash;
        }
    }

    /// <inheritdoc />
    public Note? LoadSnapshot(string snapshot)
    {
        if (!IsValidHash(snapshot))
            return null;

        var path = BlobPath(snapshot.ToLowerInvariant());
        if (!File.Exists(path))
            return null;

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var compressed = new BrotliStream(file, CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            compressed.CopyTo(buffer);

            return JsonSerializer.Deserialize<NoteSnapshot>(buffer.ToArray(), JsonOptions)?.ToNote();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            _logger?.LogWarning(ex, "Could not read the history snapshot {Snapshot}", snapshot);
            return null;
        }
    }

    /// <inheritdoc />
    public int Prune()
    {
        lock (_sync)
        {
            try
            {
                return PruneLocked();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Could not prune the note history");
                return 0;
            }
        }
    }

    /// <inheritdoc />
    public BoardHistoryStatistics GetStatistics()
    {
        lock (_sync)
        {
            var entries = LoadAllLocked();
            var blobs = CollectBlobFiles();

            var entryBytes = EnumerateDayFiles().Sum(file => file.Length);
            var blobBytes = blobs.Sum(file => file.Length);

            return new BoardHistoryStatistics(entries.Count, blobs.Count, entryBytes + blobBytes);
        }
    }

    private List<NoteHistoryEntry> LoadAllLocked()
    {
        var entries = new List<NoteHistoryEntry>();

        foreach (var file in EnumerateDayFiles())
        {
            foreach (var line in ReadLines(file.FullName))
            {
                if (TryParseEntry(line) is { } entry)
                    entries.Add(entry);
            }
        }

        // 日志本来就按追加顺序写；这里再排一次，防手工编辑/跨文件写入顺序打乱。
        return [.. entries.OrderBy(entry => entry.At)];
    }

    private int PruneLocked()
    {
        var days = EnumerateDayFiles();
        if (days.Count == 0)
            return 0;

        var removed = 0;
        var cutoff = DateTime.Today.AddDays(-RetentionDays);

        var kept = new List<(FileInfo File, DateTime Day)>();
        foreach (var file in days)
        {
            if (ParseDay(file.Name) is { } day && day >= cutoff)
            {
                kept.Add((file, day));
                continue;
            }

            file.Delete();
            removed++;
        }

        if (kept.Count == 0)
            return removed;

        // 每天引用了哪些快照：删掉一天时才知道哪些 blob 没人要了。
        var entriesByFile = kept.ToDictionary(
            item => item.File.FullName,
            item => ReadLines(item.File.FullName).Select(TryParseEntry).OfType<NoteHistoryEntry>().ToList());

        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entriesByFile.Values.SelectMany(list => list))
        {
            if (entry.Snapshot is not { } blob)
                continue;

            usage[blob] = usage.GetValueOrDefault(blob) + 1;
        }

        var blobs = CollectBlobFiles();

        long TotalBytes()
        {
            var keptBytes = kept.Sum(item => item.File.Length);
            var blobBytes = blobs
                .Where(blob => usage.ContainsKey(Path.GetFileNameWithoutExtension(blob.Name)))
                .Sum(blob => blob.Length);

            return keptBytes + blobBytes;
        }

        // 先按容量上限删最老的那几天，最新的那天永远留着（否则一超限就等于把历史全删了）。
        var ordered = kept.OrderBy(item => item.Day).ToList();
        var total = TotalBytes();

        while (total > MaxBytes && ordered.Count > 1)
        {
            var oldest = ordered[0];
            ordered.RemoveAt(0);

            if (entriesByFile.Remove(oldest.File.FullName, out var entries))
            {
                foreach (var entry in entries)
                {
                    if (entry.Snapshot is not { } blob)
                        continue;

                    if (usage.TryGetValue(blob, out var count) && count <= 1)
                        usage.Remove(blob);
                    else if (count > 1)
                        usage[blob] = count - 1;
                }
            }

            oldest.File.Delete();
            removed++;

            total = ordered.Sum(item => item.File.Length) + blobs
                .Where(blob => usage.ContainsKey(Path.GetFileNameWithoutExtension(blob.Name)))
                .Sum(blob => blob.Length);
        }

        // 没人引用的快照（旧日志被删掉后留下的）一起清掉。
        foreach (var blob in blobs)
        {
            if (!usage.ContainsKey(Path.GetFileNameWithoutExtension(blob.Name)))
                blob.Delete();
        }

        return removed;
    }

    private string DayFilePath(DateTimeOffset at)
    {
        Directory.CreateDirectory(_root);
        return Path.Combine(_root, $"{at.ToLocalTime():yyyy-MM-dd}.jsonl");
    }

    private string BlobPath(string hash) => Path.Combine(_blobsDirectory, hash[..2], hash + ".br");

    private byte[] SerializeLine(NoteHistoryEntry entry)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, JsonOptions) + "\n");

    private void RememberTail(string path, long offset, NoteHistoryEntry entry)
    {
        _lastFilePath = path;
        _lastOffset = offset;
        _lastEntry = entry;
    }

    private NoteHistoryEntry? TryParseEntry(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            return JsonSerializer.Deserialize<NoteHistoryEntry>(line, JsonOptions);
        }
        catch (JsonException ex)
        {
            // 一行坏了不该让整段历史读不出来：跳过它，日志里留个警告。
            _logger?.LogWarning(ex, "Skipped a corrupt note history line");
            return null;
        }
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        try
        {
            return File.ReadLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private List<FileInfo> EnumerateDayFiles()
    {
        if (!Directory.Exists(_root))
            return [];

        return Directory
            .EnumerateFiles(_root, "*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists && ParseDay(file.Name) is not null)
            .OrderBy(file => ParseDay(file.Name))
            .ToList();
    }

    private List<FileInfo> CollectBlobFiles()
    {
        if (!Directory.Exists(_blobsDirectory))
            return [];

        return Directory
            .EnumerateFiles(_blobsDirectory, "*.br", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .ToList();
    }

    private static DateTime? ParseDay(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return DateTime.TryParseExact(stem, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;
    }

    private static bool IsValidHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64)
            return false;

        foreach (var character in hash)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }

        return true;
    }
}
