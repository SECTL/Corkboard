using System.Globalization;
using System.Text.RegularExpressions;
using Corkboard.Core.Board;

namespace Corkboard.Core.Tests;

/// <summary>
///     作业板备份：整份复制、目录名时间戳、manifest、清单排序，以及失败时不留半成品。
///     全部在临时沙箱目录里跑，不碰真实的数据根。
/// </summary>
public sealed class BoardBackupTests : IDisposable
{
    private readonly string _sandbox =
        Path.Combine(Path.GetTempPath(), "corkboard-backup", Guid.NewGuid().ToString("N"));

    public BoardBackupTests()
    {
        Directory.CreateDirectory(DataRoot);
        _service = new BoardBackupService(DataRoot, BackupRoot);
    }

    private string DataRoot => Path.Combine(_sandbox, "data", "board");

    private string BackupRoot => Path.Combine(_sandbox, "data", "backups", "board");

    private readonly BoardBackupService _service;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox))
                Directory.Delete(_sandbox, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不影响断言结果。
        }
    }

    private void WriteDataFile(string relativePath, string content)
    {
        var path = Path.Combine(DataRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static readonly Regex BackupDirectoryName = new(@"^\d{8}-\d{6}(-\d+)?$");

    [Fact]
    public async Task CreateBackupAsync_CopiesWholeDataDirectory()
    {
        WriteDataFile(Path.Combine("2026", "10", "17", "notes.json"), """[{"id":"1"}]""");
        WriteDataFile("board.json", """{"subjects":["数学"]}""");

        var result = await _service.CreateBackupAsync("test", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(result.Directory);
        Assert.Null(result.ErrorMessage);

        var backup = result.Directory!;
        Assert.StartsWith(Path.GetFullPath(BackupRoot), Path.GetFullPath(backup), StringComparison.OrdinalIgnoreCase);
        Assert.Matches(BackupDirectoryName, Path.GetFileName(backup));

        // 内容一致：日期文件与 board.json 都要原样拷过去。
        Assert.Equal("""[{"id":"1"}]""",
            File.ReadAllText(Path.Combine(backup, "2026", "10", "17", "notes.json")));
        Assert.Equal("""{"subjects":["数学"]}""",
            File.ReadAllText(Path.Combine(backup, "board.json")));

        // manifest 里写着原因与时间，手工翻备份时能认出这份是怎么来的。
        var manifest = File.ReadAllText(Path.Combine(backup, "manifest.txt"));
        Assert.Contains("test", manifest);
    }

    [Fact]
    public async Task CreateBackupAsync_TwiceAMomentApart_DoesNotOverwriteTheFirstBackup()
    {
        WriteDataFile(Path.Combine("2026", "10", "17", "notes.json"), "[]");

        var first = await _service.CreateBackupAsync("first", TestContext.Current.CancellationToken);
        var second = await _service.CreateBackupAsync("second", TestContext.Current.CancellationToken);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotEqual(first.Directory, second.Directory);

        // 两份都得还在、都得各自带自己的文件；同一秒内的第二份带 -2 后缀。
        Assert.True(Directory.Exists(first.Directory));
        Assert.True(Directory.Exists(second.Directory));
        Assert.Equal(2, Directory.GetDirectories(BackupRoot).Length);
        Assert.Single(Directory.GetFiles(first.Directory!, "notes.json", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(second.Directory!, "notes.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CreateBackupAsync_WithMissingOrEmptyDataDirectory_StillSucceeds()
    {
        // 刚装好还没布置过作业：没有数据目录也算「这一刻板子是空的」，可以直接清空。
        Directory.Delete(DataRoot, recursive: true);

        var result = await _service.CreateBackupAsync("empty", TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(result.Directory);
        Assert.True(Directory.Exists(result.Directory));

        // 空备份里只有 manifest。
        var manifest = Path.Combine(result.Directory!, "manifest.txt");
        Assert.True(File.Exists(manifest));
        Assert.Single(Directory.GetFiles(result.Directory!, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CreateBackupAsync_WhenBackupRootIsBlocked_ReturnsFailureWithoutTouchingData()
    {
        WriteDataFile(Path.Combine("2026", "10", "17", "notes.json"), "[]");

        // 备份根目录的位置上放一个文件：建不出目录，备份整体失败。
        var blockedRoot = Path.Combine(_sandbox, "blocked");
        Directory.CreateDirectory(_sandbox);
        File.WriteAllText(blockedRoot, "not a directory");

        var service = new BoardBackupService(DataRoot, Path.Combine(blockedRoot, "board"));
        var result = await service.CreateBackupAsync("blocked", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Null(result.Directory);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));

        // 失败不能动原数据。
        Assert.True(File.Exists(Path.Combine(DataRoot, "2026", "10", "17", "notes.json")));
    }

    [Fact]
    public async Task CreateBackupAsync_WhenAFileCannotBeCopied_RemovesTheHalfDoneBackup()
    {
        // 文件被独占打开时复制会失败——只有 Windows 强制共享模式，其它平台跳过。
        if (!OperatingSystem.IsWindows())
            return;

        var lockedFile = Path.Combine(DataRoot, "2026", "10", "17", "notes.json");
        WriteDataFile(Path.Combine("2026", "10", "17", "notes.json"), "[]");

        await using var lockHandle = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite,
            FileShare.None);

        var result = await _service.CreateBackupAsync("locked", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Null(result.Directory);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));

        // 半成品目录必须删掉：留下一个缺文件的备份比没有备份更糟。
        Assert.True(!Directory.Exists(BackupRoot) || Directory.GetDirectories(BackupRoot).Length == 0);
    }

    [Fact]
    public void ListBackups_SortsParsedTimestampNamesNewestFirst()
    {
        Directory.CreateDirectory(Path.Combine(BackupRoot, "20260101-010101"));
        Directory.CreateDirectory(Path.Combine(BackupRoot, "20260302-020202"));
        Directory.CreateDirectory(Path.Combine(BackupRoot, "20260203-030303"));

        var backups = _service.ListBackups();

        Assert.Equal(["20260302-020202", "20260203-030303", "20260101-010101"],
            backups.Select(entry => Path.GetFileName(entry.Directory)));
        Assert.Equal(new DateTimeOffset(2026, 3, 2, 2, 2, 2, TimeZoneInfo.Local.GetUtcOffset(
            new DateTime(2026, 3, 2, 2, 2, 2))), backups[0].CreatedAt);
    }

    [Fact]
    public void ListBackups_IncludesDirectoriesWhoseNamesAreNotTimestamps()
    {
        // 手工建的、旧版本留下的目录也列出来，时间退回文件系统创建时间。
        Directory.CreateDirectory(Path.Combine(BackupRoot, "manual-copy"));

        var entry = Assert.Single(_service.ListBackups());

        Assert.Equal("manual-copy", Path.GetFileName(entry.Directory));
        Assert.True((DateTimeOffset.Now - entry.CreatedAt).Duration() < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void ListBackups_WhenNoneExist_ReturnsEmpty()
    {
        Assert.Empty(_service.ListBackups());
        Assert.False(Directory.Exists(BackupRoot));
    }

    [Fact]
    public async Task ListBackups_AfterCreateBackup_FindsTheNewDirectory()
    {
        var result = await _service.CreateBackupAsync("list", TestContext.Current.CancellationToken);

        Assert.True(result.Success);

        var entry = Assert.Single(_service.ListBackups());
        Assert.Equal(Path.GetFullPath(result.Directory!), Path.GetFullPath(entry.Directory));
        Assert.Equal(Path.GetFileName(result.Directory), entry.CreatedAt.ToString("yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture));
    }
}
