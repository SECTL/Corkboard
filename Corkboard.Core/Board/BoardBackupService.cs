using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Corkboard.Shared;

namespace Corkboard.Core.Board;

/// <summary>一次备份的结果：成功时 <see cref="Directory" /> 指向备份目录，失败时带一句话原因。</summary>
public sealed record BoardBackupResult(bool Success, string? Directory, string? ErrorMessage = null);

/// <summary>一份已经存在的备份。</summary>
public sealed record BoardBackupEntry(DateTimeOffset CreatedAt, string Directory);

/// <summary>
///     作业板备份：把 <c>data/board/</c> 整份拷到 <c>data/backups/board/&lt;时间戳&gt;/</c>。
///     <para>
///         清空作业板、以及设置里的「立即备份」都走这里。备份只是复制文件，不动原数据；
///         目录名用本地时间戳，按名字排序就是时间顺序。每次备份里还写一份 <c>manifest.txt</c>
///         记下时间与原因，手工翻备份目录时能看出这份是怎么来的。
///     </para>
///     <para>
///         数据目录不存在或者还空着（刚装好、一条作业都没布置过）同样算备份成功：
///         建一个空目录加 manifest 就是「这一刻板子是空的」，调用方据此就可以放心清空。
///     </para>
/// </summary>
public class BoardBackupService
{
    /// <summary>备份目录名的格式：本地时间到秒，按名字排序就是时间顺序。</summary>
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    /// <summary>备份目录里那份说明文件的文件名。</summary>
    private const string ManifestFileName = "manifest.txt";

    /// <summary>目录名认得出是这样生成的（同秒重名时带 <c>-2</c>、<c>-3</c> 后缀）。</summary>
    private static readonly Regex TimestampPattern = new(@"^(?<stamp>\d{8}-\d{6})(?:-\d+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string? _dataRootOverride;
    private readonly string? _backupRootOverride;

    /// <summary>
    ///     <paramref name="dataRoot" /> 与 <paramref name="backupRoot" /> 只给测试用：传临时目录就能把
    ///     数据根换掉。两个都不传（DI 的用法）时退回 <see cref="Utils" /> 里的真实数据根。
    /// </summary>
    public BoardBackupService(string? dataRoot = null, string? backupRoot = null)
    {
        _dataRootOverride = string.IsNullOrWhiteSpace(dataRoot) ? null : Path.GetFullPath(dataRoot);
        _backupRootOverride = string.IsNullOrWhiteSpace(backupRoot) ? null : Path.GetFullPath(backupRoot);
    }

    /// <summary>作业板数据目录（<c>data/board</c>），备份的源。</summary>
    public string BoardDataDirectory => _dataRootOverride ?? Utils.GetDirectoryPath("board");

    /// <summary>备份根目录（<c>data/backups/board</c>）。</summary>
    public string BackupRootDirectory => _backupRootOverride ?? Utils.GetDirectoryPath("backups", "board");

    /// <summary>
    ///     把当前的作业板数据备份一份。<paramref name="reason" /> 写进 manifest，说明这份备份是因为什么留下的。
    /// </summary>
    /// <returns>
    ///     成功时 <see cref="BoardBackupResult.Success" /> 为真、<see cref="BoardBackupResult.Directory" />
    ///     指向新备份目录；任何一步失败都返回失败结果，并且不会留下半成品备份目录。
    /// </returns>
    public Task<BoardBackupResult> CreateBackupAsync(string reason, CancellationToken cancellationToken = default)
        // 复制整棵目录是纯 IO，挪到线程池上跑，别把界面线程占住。
        => Task.Run(() => CreateBackup(reason, cancellationToken), cancellationToken);

    /// <summary>列出现有的备份，新的在前。只读目录结构，不读备份里的文件。</summary>
    public IReadOnlyList<BoardBackupEntry> ListBackups()
    {
        var root = BackupRootDirectory;
        if (!Directory.Exists(root))
            return [];

        var entries = new List<BoardBackupEntry>();
        foreach (var directory in Directory.EnumerateDirectories(root))
            entries.Add(new BoardBackupEntry(ResolveCreatedAt(directory), Path.GetFullPath(directory)));

        // 新在前；同一秒的两份（带 -2 后缀的那种）按名字倒序，后生成的那份排前面。
        return
        [
            .. entries
                .OrderByDescending(entry => entry.CreatedAt)
                .ThenByDescending(entry => Path.GetFileName(entry.Directory), StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>同步版本的备份本体。<see cref="CreateBackupAsync" /> 在线程池上跑它。</summary>
    private BoardBackupResult CreateBackup(string reason, CancellationToken cancellationToken)
    {
        string? target = null;
        try
        {
            Directory.CreateDirectory(BackupRootDirectory);
            target = CreateBackupDirectory(BackupRootDirectory);

            var fileCount = 0;
            var source = BoardDataDirectory;
            if (Directory.Exists(source))
            {
                var errors = new List<string>();
                fileCount = CopyDirectory(source, target, errors, cancellationToken);
                if (errors.Count > 0)
                {
                    // 备份缺文件就不算备份：删掉半成品，让调用方（比如清空作业板）据此放弃后续动作。
                    TryDeleteDirectory(target);
                    return new BoardBackupResult(false, null, BuildErrorMessage(errors));
                }
            }

            WriteManifest(target, reason, DateTimeOffset.Now, fileCount);
            return new BoardBackupResult(true, target);
        }
        catch (OperationCanceledException)
        {
            // 取消了也不算备份：把已经拷了一半的目录收拾掉再往外抛。
            TryDeleteDirectory(target);
            throw;
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(target);
            return new BoardBackupResult(false, null, $"备份失败：{ex.Message}");
        }
    }

    /// <summary>
    ///     在备份根目录下挑一个还没被占用的时间戳目录建出来。同一秒内备份两次时加 <c>-2</c>、<c>-3</c> 后缀。
    /// </summary>
    private static string CreateBackupDirectory(string backupRoot)
    {
        var stamp = DateTimeOffset.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture);

        for (var suffix = 1;; suffix++)
        {
            var name = suffix == 1 ? stamp : $"{stamp}-{suffix}";
            var candidate = Path.Combine(backupRoot, name);

            // 只用 Exists 判断再建目录，理论上仍有并发窗口；备份是用户点出来的单线程操作，够用了。
            if (Directory.Exists(candidate) || File.Exists(candidate))
                continue;

            Directory.CreateDirectory(candidate);
            return candidate;
        }
    }

    /// <summary>
    ///     递归把 <paramref name="source" /> 整个复制到 <paramref name="target" />。
    ///     单个文件失败只记下来继续拷其它文件，最后统一由调用方判定整体失败。
    /// </summary>
    /// <returns>成功复制过去的文件数。</returns>
    private static int CopyDirectory(string source, string target, List<string> errors,
        CancellationToken cancellationToken)
    {
        var fileCount = 0;
        var pending = new Stack<string>();
        pending.Push(source);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folder = pending.Pop();
            var relativeFolder = Path.GetRelativePath(source, folder);

            // 空目录也要照搬：把备份目录还原回去时，结构得和原数据一致。
            try
            {
                Directory.CreateDirectory(Path.Combine(target, relativeFolder));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{relativeFolder}：{ex.Message}");
                continue;
            }

            string[] files;
            string[] subFolders;
            try
            {
                files = Directory.GetFiles(folder);
                subFolders = Directory.GetDirectories(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{relativeFolder}：{ex.Message}");
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                try
                {
                    File.Copy(file, destination, overwrite: true);
                    fileCount++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{Path.GetRelativePath(source, file)}：{ex.Message}");
                }
            }

            foreach (var subFolder in subFolders)
                pending.Push(subFolder);
        }

        return fileCount;
    }

    /// <summary>目录名能解析成时间戳就按名字算时间；认不出来的（手工建的、旧版本留下的）退回文件系统时间。</summary>
    private static DateTimeOffset ResolveCreatedAt(string directory)
    {
        var match = TimestampPattern.Match(Path.GetFileName(directory));
        if (match.Success &&
            DateTime.TryParseExact(match.Groups["stamp"].Value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var stamp))
            return new DateTimeOffset(stamp, TimeZoneInfo.Local.GetUtcOffset(stamp));

        var creationTime = Directory.GetCreationTime(directory);
        return new DateTimeOffset(creationTime.Kind == DateTimeKind.Utc
            ? creationTime.ToLocalTime()
            : creationTime);
    }

    /// <summary>写一份说明，交代这份备份的时间、原因与文件数。</summary>
    private static void WriteManifest(string directory, string reason, DateTimeOffset createdAt, int fileCount)
    {
        var text = new StringBuilder()
            .AppendLine("Corkboard 作业板备份")
            .AppendLine($"时间：{createdAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}")
            .AppendLine($"原因：{(string.IsNullOrWhiteSpace(reason) ? "manual" : reason)}")
            .AppendLine($"文件数：{fileCount}")
            .ToString();

        File.WriteAllText(Path.Combine(directory, ManifestFileName), text, new UTF8Encoding(false));
    }

    /// <summary>把一串单文件失败收成一句给人看的话。</summary>
    private static string BuildErrorMessage(IReadOnlyList<string> errors)
    {
        var preview = string.Join("；", errors.Take(3));
        var detail = errors.Count > 3 ? $"{preview} 等" : preview;
        return $"备份失败：有 {errors.Count} 个文件没复制成功（{detail}）";
    }

    /// <summary>删掉半成品备份目录；删不掉就算了，别让清理失败盖掉真正的原因。</summary>
    private static void TryDeleteDirectory(string? directory)
    {
        if (directory is null || !Directory.Exists(directory))
            return;

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 忽略：失败的备份本来就不该被当作有效备份，这里只是尽力收拾。
        }
    }
}
