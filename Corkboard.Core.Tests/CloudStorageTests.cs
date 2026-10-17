using System.IO.Compression;
using Corkboard.Core.Services.Cloud;
using Corkboard.Core.Homework;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Tests;

/// <summary>云存储用量、文件列表、错误体、备份包与自动同步策略的单测。</summary>
public class CloudStorageTests
{
    private const string UsageJson = """
    {
      "used_storage": 1048576,
      "used_storage_formatted": "1 MB",
      "total_storage": 10485760,
      "total_storage_formatted": "10 MB",
      "available_storage": 9437184,
      "available_storage_formatted": "9 MB",
      "percentage": 10,
      "file_count": 3,
      "platform_used_storage": 524288,
      "platform_used_storage_formatted": "512 KB",
      "platform_file_count": 2,
      "reconciled": false
    }
    """;

    /// <summary>用量返回体照文档给的字段解析，优先用服务端给的可读串。</summary>
    [Fact]
    public void Usage_ParsesDocumentedFields()
    {
        var usage = CloudStorageUsage.Parse(UsageJson);

        Assert.Equal(1048576, usage.UsedStorage);
        Assert.Equal(10485760, usage.TotalStorage);
        Assert.Equal(9437184, usage.AvailableStorage);
        Assert.Equal(10, usage.Percentage);
        Assert.Equal(3, usage.FileCount);
        Assert.Equal(524288, usage.PlatformUsedStorage);
        Assert.Equal(2, usage.PlatformFileCount);
        Assert.Equal("1 MB", usage.UsedText);
        Assert.Equal("512 KB", usage.PlatformUsedText);
    }

    /// <summary>服务端没给可读串时自己格式化，且缺字段不抛异常。</summary>
    [Fact]
    public void Usage_FallsBackToLocalFormatting()
    {
        var usage = CloudStorageUsage.Parse("""{ "used_storage": 2048, "platform_used_storage": 0 }""");

        Assert.Equal("2 KB", usage.UsedText);
        Assert.Equal("0 B", usage.PlatformUsedText);
        Assert.Equal(0, usage.TotalStorage);
    }

    /// <summary>体积写法用 1024 进制、10 以下带一位小数。</summary>
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(10485760, "10 MB")]
    [InlineData(1610612736, "1.5 GB")]
    public void Format_UsesReadableUnits(long bytes, string expected)
    {
        Assert.Equal(expected, CloudStorageFormat.Bytes(bytes));
    }

    /// <summary>文件列表按文件名找到备份件。</summary>
    [Fact]
    public void Files_FindBackupsByName()
    {
        const string json = """
        {
          "files": [
            { "file_id": "cf_1", "filename": "photo.png", "size": 100, "updated_at": "2026-04-11T08:00:00.000Z" },
            { "file_id": "cf_2", "filename": "corkboard-board.zip", "size": 2048, "updated_at": "2026-04-12T08:00:00.000Z" },
            { "file_id": "cf_3", "filename": "corkboard-settings.json", "size": 512, "updated_at": "2026-04-12T09:00:00.000Z" }
          ],
          "total": 3,
          "has_more": false
        }
        """;

        var files = CloudFileInfo.ParseList(json);

        Assert.Equal(3, files.Count);
        Assert.Equal("cf_2", CloudBackupFiles.FindBoard(files)!.FileId);
        Assert.Equal("cf_3", CloudBackupFiles.FindSettings(files)!.FileId);
        Assert.Equal("2 KB", CloudBackupFiles.FindBoard(files)!.SizeText);
        Assert.NotNull(CloudBackupFiles.FindBoard(files)!.UpdatedAt);
    }

    /// <summary>同名备份有好几份（历史上重复上传留下的）时取最新的一份，旧件也要能被一次列全。</summary>
    [Fact]
    public void Files_WithDuplicatesUseTheNewest()
    {
        // 服务端实测按时间倒序返回，这里也照这个顺序：第一份最新。
        const string json = """
        {
          "files": [
            { "file_id": "cf_new_board", "filename": "corkboard-board.zip", "size": 3072, "updated_at": "2026-04-13T08:00:00.000Z" },
            { "file_id": "cf_new_settings", "filename": "corkboard-settings.json", "size": 1024, "updated_at": "2026-04-13T08:00:01.000Z" },
            { "file_id": "cf_old_settings", "filename": "corkboard-settings.json", "size": 512, "updated_at": "2026-04-12T08:00:00.000Z" },
            { "file_id": "cf_old_board", "filename": "corkboard-board.zip", "size": 2048, "updated_at": "2026-04-12T08:00:00.000Z" }
          ],
          "total": 4,
          "has_more": false
        }
        """;

        var files = CloudFileInfo.ParseList(json);

        Assert.Equal("cf_new_board", CloudBackupFiles.FindBoard(files)!.FileId);
        Assert.Equal("cf_new_settings", CloudBackupFiles.FindSettings(files)!.FileId);
        Assert.Equal(new[] { "cf_new_board", "cf_old_board" },
            CloudBackupFiles.AllBoardArchives(files).Select(file => file.FileId));
        Assert.Equal(new[] { "cf_new_settings", "cf_old_settings" },
            CloudBackupFiles.AllSettingsFiles(files).Select(file => file.FileId));
    }

    /// <summary>
    ///     「清空云端」要删的是 Corkboard 自己传的备份：整包、分片、清单、设置都在内，
    ///     平台上别人的文件（不认识的文件名）一律不碰。
    /// </summary>
    [Fact]
    public void Files_AllManagedFilesCoversBackupsOnly()
    {
        const string json = """
        {
          "files": [
            { "file_id": "cf_photo", "filename": "photo.png", "size": 100, "updated_at": "2026-04-13T08:00:00.000Z" },
            { "file_id": "cf_part_a", "filename": "corkboard-board-20260413-080000-0f1e2d3c-p01of02.part", "size": 3072, "updated_at": "2026-04-13T08:00:00.000Z" },
            { "file_id": "cf_part_b", "filename": "corkboard-board-20260413-080000-0f1e2d3c-p02of02.part", "size": 512, "updated_at": "2026-04-13T08:00:00.000Z" },
            { "file_id": "cf_manifest", "filename": "corkboard-board-20260413-080000-0f1e2d3c-manifest.json", "size": 700, "updated_at": "2026-04-13T08:00:01.000Z" },
            { "file_id": "cf_legacy", "filename": "corkboard-board.zip", "size": 2048, "updated_at": "2026-04-12T08:00:00.000Z" },
            { "file_id": "cf_settings", "filename": "corkboard-settings.json", "size": 1024, "updated_at": "2026-04-13T08:00:02.000Z" }
          ],
          "total": 6,
          "has_more": false
        }
        """;

        var files = CloudFileInfo.ParseList(json);

        Assert.Equal(new[] { "cf_part_a", "cf_part_b", "cf_manifest", "cf_legacy", "cf_settings" },
            CloudBackupFiles.AllManagedFiles(files).Select(file => file.FileId));
        Assert.False(CloudBackupFiles.IsManagedFile(files[0]));
    }

    /// <summary>
    ///     二进制备份走 base64：云存储会把上传的字节按 UTF-8 解码（非法字节变 U+FFFD），
    ///     所以压缩包必须先变成纯 ASCII 文本再传，取回来还能原样还原。
    /// </summary>
    [Fact]
    public async Task Payload_Base64RoundTripsBinary()
    {
        var bytes = new byte[256];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)i;

        var encoded = CloudBackupPayload.Encode(bytes);

        Assert.True(encoded.All(character => character < 128));
        Assert.Equal(bytes, CloudBackupPayload.Decode(encoded));
        Assert.Equal(bytes, CloudBackupPayload.Decode("  " + encoded + Environment.NewLine));
        Assert.Equal(bytes, CloudBackupPayload.Decode("\uFEFF" + encoded));

        var directory = Path.Combine(Path.GetTempPath(), "corkboard-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.bin");
            var encodedPath = Path.Combine(directory, "encoded.txt");
            var decodedPath = Path.Combine(directory, "decoded.bin");
            var token = TestContext.Current.CancellationToken;
            await File.WriteAllBytesAsync(source, bytes, token);

            await CloudBackupPayload.WriteEncodedAsync(source, encodedPath, token);
            await CloudBackupPayload.WriteDecodedAsync(encodedPath, decodedPath, token);

            Assert.Equal(bytes, await File.ReadAllBytesAsync(decodedPath, token));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>作业板备份的云端名带 base64 后缀，挑件时也认它。</summary>
    [Fact]
    public void Files_RecognizesBase64BoardArchive()
    {
        Assert.Equal("corkboard-board.zip.b64", CloudBackupFiles.BoardCloudName);
        Assert.True(CloudBackupFiles.IsBoardArchive(CloudBackupFiles.BoardArchiveName));
        Assert.True(CloudBackupFiles.IsBoardArchive(CloudBackupFiles.BoardCloudName));
        Assert.False(CloudBackupFiles.IsBase64BoardArchive(CloudBackupFiles.BoardArchiveName));
        Assert.True(CloudBackupFiles.IsBase64BoardArchive(CloudBackupFiles.BoardCloudName));
        Assert.False(CloudBackupFiles.IsSettingsFile(CloudBackupFiles.BoardCloudName));

        var files = CloudFileInfo.ParseList("""
        {
          "files": [
            { "file_id": "cf_b64", "filename": "corkboard-board.zip.b64", "size": 4096, "updated_at": "2026-04-13T08:00:00.000Z" },
            { "file_id": "cf_old", "filename": "corkboard-board.zip", "size": 2048, "updated_at": "2026-04-12T08:00:00.000Z" }
          ],
          "total": 2,
          "has_more": false
        }
        """);

        Assert.Equal("cf_b64", CloudBackupFiles.FindBoard(files)!.FileId);
        Assert.Equal(2, CloudBackupFiles.AllBoardArchives(files).Count());
    }

    /// <summary>错误体解析；正常返回体不会被当成错误。</summary>
    [Fact]
    public void Error_ParsesOnlyErrorBodies()
    {
        var error = CloudStorageError.TryParse("""{ "error": "storage_exceeded", "error_description": "Storage quota exceeded" }""");
        Assert.Equal("storage_exceeded", error!.Code);
        Assert.Equal("Storage quota exceeded", error.Description);
        Assert.Null(CloudStorageError.TryParse("""{ "success": true }"""));
        Assert.Null(CloudStorageError.TryParse(null));
        Assert.Null(CloudStorageError.TryParse("<html>"));
    }

    /// <summary>自动拉取的判定：本机有未同步改动时绝不自动拉。</summary>
    [Fact]
    public void Policy_AutoPullOnlyWhenLocalIsClean()
    {
        var lastSync = new DateTimeOffset(2026, 4, 11, 8, 0, 0, TimeSpan.Zero);
        var remoteNewer = lastSync.AddHours(1);

        Assert.False(CloudSyncPolicy.ShouldAutoPull(null, lastSync, lastSync));
        // 新设备：云端有备份、本机什么也没有
        Assert.True(CloudSyncPolicy.ShouldAutoPull(remoteNewer, null, null));
        // 本机有数据但从没同步过：不能拿云端盖掉
        Assert.False(CloudSyncPolicy.ShouldAutoPull(remoteNewer, null, lastSync));
        // 本机自上次同步后没改过 → 可以自动拉
        Assert.True(CloudSyncPolicy.ShouldAutoPull(remoteNewer, lastSync, lastSync));
        // 本机改过了 → 交给用户决定
        Assert.False(CloudSyncPolicy.ShouldAutoPull(remoteNewer, lastSync, lastSync.AddMinutes(5)));
        // 云端不比本机新
        Assert.False(CloudSyncPolicy.ShouldAutoPull(lastSync, lastSync, lastSync));
    }

    /// <summary>需不需要上传的判定。</summary>
    [Fact]
    public void Policy_NeedsUploadWhenLocalIsNewer()
    {
        var lastSync = new DateTimeOffset(2026, 4, 11, 8, 0, 0, TimeSpan.Zero);

        Assert.False(CloudSyncPolicy.NeedsUpload(null, lastSync));
        Assert.True(CloudSyncPolicy.NeedsUpload(lastSync, null));
        Assert.True(CloudSyncPolicy.NeedsUpload(lastSync.AddMinutes(1), lastSync));
        Assert.False(CloudSyncPolicy.NeedsUpload(lastSync, lastSync));
    }

    /// <summary>自动上传的防抖截止时间：只在更早的时候才提前，不能被后到的长等待推后。</summary>
    [Fact]
    public void Policy_NextDeadlineKeepsTheEarlierOne()
    {
        var now = new DateTimeOffset(2026, 4, 11, 8, 0, 0, TimeSpan.Zero);

        // 没排过任何等待：按这次的延迟算。
        var boardDeadline = CloudSyncPolicy.NextDeadline(null, now, TimeSpan.FromSeconds(5));
        Assert.Equal(now.AddSeconds(5), boardDeadline);

        // 作业板改动后紧跟一次设置落盘（45 秒）：还得按作业板那 5 秒算。
        Assert.Equal(boardDeadline, CloudSyncPolicy.NextDeadline(boardDeadline, now, TimeSpan.FromSeconds(45)));
        Assert.Equal(boardDeadline, CloudSyncPolicy.NextDeadline(boardDeadline, now.AddSeconds(1), TimeSpan.FromSeconds(45)));

        // 反过来：先排着 45 秒、这时作业板改了，要提前到 5 秒。
        var settingsDeadline = CloudSyncPolicy.NextDeadline(null, now, TimeSpan.FromSeconds(45));
        Assert.Equal(now.AddSeconds(5), CloudSyncPolicy.NextDeadline(settingsDeadline, now, TimeSpan.FromSeconds(5)));

        // 已经过点的截止时间照样保留：那次上传本来就要发生了，再往后推只会白等，
        // 而且上传时看的是当时的改动，新改动照样会一起传上去。
        var overdue = now.AddSeconds(-1);
        Assert.Equal(overdue, CloudSyncPolicy.NextDeadline(overdue, now, TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    ///     「云端被别处清空了」只在「本机同步成功过、云端却空了」时成立：
    ///     从没同步过是正常的空云端，本机有备份也只是别人的云端，都不该提示。
    /// </summary>
    [Fact]
    public void Policy_RemoteClearedOnlyWhenWeHadSyncedBefore()
    {
        var lastSync = new DateTimeOffset(2026, 4, 11, 8, 0, 0, TimeSpan.Zero);

        // 本机记着同步过，云端一份都没有了：别处清空的。
        Assert.True(CloudSyncPolicy.WasClearedRemotely(lastSync, false));

        // 云端还有备份：没被清空。
        Assert.False(CloudSyncPolicy.WasClearedRemotely(lastSync, true));

        // 从没同步过：云端空着是正常的，不是「被清空」。
        Assert.False(CloudSyncPolicy.WasClearedRemotely(null, false));
        Assert.False(CloudSyncPolicy.WasClearedRemotely(null, true));
    }

    /// <summary>备份包能原样打回来，清单不落到数据目录里。</summary>
    [Fact]
    public void Archive_RoundTripsBoardData()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "source");
            var notes = Path.Combine(source, "2026", "10", "11", "notes.json");
            Directory.CreateDirectory(Path.GetDirectoryName(notes)!);
            File.WriteAllText(notes, """{"notes":[]}""");

            var archivePath = Path.Combine(root, "board.zip");
            CloudBackupArchive.Create(source, archivePath, new CloudBackupManifest("Corkboard", "1.0.0", "pf_test", DateTimeOffset.UnixEpoch));

            var manifest = CloudBackupArchive.ReadManifest(archivePath);
            Assert.NotNull(manifest);
            Assert.Equal("pf_test", manifest!.PlatformId);

            var target = Path.Combine(root, "restored");
            Assert.Equal(1, CloudBackupArchive.Extract(archivePath, target));
            Assert.Equal("""{"notes":[]}""", File.ReadAllText(Path.Combine(target, "2026", "10", "11", "notes.json")));
            Assert.False(File.Exists(Path.Combine(target, CloudBackupFiles.ManifestEntryName)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>备份包里带目录穿越路径时必须拒绝恢复。</summary>
    [Fact]
    public void Archive_RejectsPathTraversal()
    {
        var root = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(root, "evil.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../escaped.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("bad");
            }

            var target = Path.Combine(root, "restored");
            Assert.Throws<CloudStorageException>(() => CloudBackupArchive.Extract(archivePath, target));
            Assert.False(File.Exists(Path.Combine(root, "escaped.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     两边都动过才融合：本机没动（那是整体拉取）、云端没有更新（没东西可并）、
    ///     云端连备份都没有，都不该走融合。
    /// </summary>
    [Fact]
    public void Policy_MergeOnlyWhenBothSidesChanged()
    {
        var lastSync = new DateTimeOffset(2026, 4, 11, 8, 0, 0, TimeSpan.Zero);

        // 两边都比上次同步新：融合。
        Assert.True(CloudSyncPolicy.ShouldMerge(lastSync.AddMinutes(5), lastSync, lastSync.AddMinutes(3)));

        // 从没同步过（新设备、或者云端刚被别的设备清空过）：两边都有数据就融合。
        Assert.True(CloudSyncPolicy.ShouldMerge(lastSync, null, lastSync));

        // 云端没有备份：没什么可融合的。
        Assert.False(CloudSyncPolicy.ShouldMerge(null, lastSync, lastSync.AddMinutes(3)));

        // 本机没动过：那是整体拉取的事，跟融合互斥。
        Assert.False(CloudSyncPolicy.ShouldMerge(lastSync.AddMinutes(5), lastSync, null));
        Assert.True(CloudSyncPolicy.ShouldAutoPull(lastSync.AddMinutes(5), lastSync, null));

        // 云端那份不比上次同步新：没有新东西可以并进来。
        Assert.False(CloudSyncPolicy.ShouldMerge(lastSync, lastSync, lastSync.AddMinutes(3)));

        // 本机自己的改动已经同步过了：不算两边都动过。
        Assert.False(CloudSyncPolicy.ShouldMerge(lastSync.AddMinutes(5), lastSync, lastSync));
    }

    /// <summary>「只同步最近 N 天」的窗口：0 或负数表示不限，正数就是从当下往前数 N 天（滚动窗口）。</summary>
    [Fact]
    public void Policy_UploadWindowIsRollingAndOptional()
    {
        var now = new DateTimeOffset(2026, 10, 11, 20, 0, 0, TimeSpan.FromHours(8));

        // 不设窗口：一份都不筛。
        Assert.Null(CloudSyncPolicy.UploadCutoff(0, now));
        Assert.Null(CloudSyncPolicy.UploadCutoff(-3, now));

        // 设了窗口：窗口边界那一刻算窗口内（作业按创建时间跟它比）。
        var cutoff = CloudSyncPolicy.UploadCutoff(7, now);
        Assert.NotNull(cutoff);
        Assert.Equal(now.AddDays(-7), cutoff.Value);
        Assert.True(now.AddDays(-7) >= cutoff.Value);
        Assert.False(now.AddDays(-7).AddTicks(-1) >= cutoff.Value);
    }

    /// <summary>并集：本机的都在，云端多出来的补进来，两边都有的取更新的那份。</summary>
    [Fact]
    public void Merge_KeepsLocalAndAddsRemote()
    {
        var day = new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.FromHours(8));
        var mine = Guid.NewGuid();
        var both = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        var local = new[]
        {
            Note(mine, day, day.AddMinutes(1), "本机新写的"),
            Note(both, day, day.AddMinutes(2), "本机改的")
        };

        var remote = new[]
        {
            Note(both, day, day.AddMinutes(30), "云端改的"),
            Note(theirs, day.AddMinutes(5), day.AddMinutes(6), "另一台设备布置的")
        };

        var merged = HomeworkNoteMerge.Merge(local, remote);

        Assert.Equal(3, merged.Notes.Count);
        Assert.Equal(1, merged.AddedFromRemote);
        Assert.Equal(1, merged.UpdatedFromRemote);
        Assert.Equal(0, merged.KeptLocal);
        Assert.True(merged.TookAnythingFromRemote);

        // 同一条取更新的那份：两边都改过的那条按云端的新内容；本机独有的、云端独有的一条不落地丢。
        Assert.Equal("云端改的", merged.Notes.Single(note => note.Id == both).Content);
        Assert.Equal("本机新写的", merged.Notes.Single(note => note.Id == mine).Content);
        Assert.Equal("另一台设备布置的", merged.Notes.Single(note => note.Id == theirs).Content);

        // 按创建时间排序：另一台设备那条创建得最晚，排在最后。
        Assert.Equal(theirs, merged.Notes[^1].Id);
    }

    /// <summary>融合只加不减：本机已有的作业一条都不会丢，云端更旧的那份不会盖掉本机。</summary>
    [Fact]
    public void Merge_NeverDropsLocalNotes()
    {
        var day = new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.FromHours(8));
        var localOnly = Guid.NewGuid();
        var shared = Guid.NewGuid();

        var merged = HomeworkNoteMerge.Merge(
            [Note(localOnly, day, day.AddMinutes(1), "本机的"), Note(shared, day, day.AddMinutes(1), "两边一样")],
            [Note(shared, day, day, "云端旧的那份")]);

        Assert.Equal(2, merged.Notes.Count);
        Assert.False(merged.TookAnythingFromRemote);
        Assert.Equal(0, merged.AddedFromRemote);
        Assert.Equal(1, merged.KeptLocal);
        Assert.Equal("两边一样", merged.Notes.Single(note => note.Id == shared).Content);
    }

    /// <summary>清理标记跟着更新的那份走：一边自动清理过、另一边还在板子上，融合后也算清理过。</summary>
    [Fact]
    public void Merge_TakesCleanedFlagWithTheNewerCopy()
    {
        var day = new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.FromHours(8));
        var id = Guid.NewGuid();

        var merged = HomeworkNoteMerge.Merge(
            [Note(id, day, day.AddMinutes(1), "做完了")],
            [Note(id, day, day.AddMinutes(20), "做完了", day.AddMinutes(20))]);

        var note = Assert.Single(merged.Notes);
        Assert.Equal(day.AddMinutes(20), note.CleanedAt);
    }

    /// <summary>
    ///     同一侧自己带重复时并集不会把它复制出去：历史数据里同一个 Id 可能落在两个日期文件里
    ///     （<c>NoteStore.LoadAll</c> 按文件读、不按 Id 去重），这时每个 Id 只留更新的那一份。
    /// </summary>
    [Fact]
    public void Merge_DeduplicatesRepeatedIdsInsideOneSide()
    {
        var day = new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.FromHours(8));
        var id = Guid.NewGuid();

        var fromLocal = HomeworkNoteMerge.Merge(
            [Note(id, day, day.AddMinutes(1), "本机旧的那份"), Note(id, day, day.AddMinutes(30), "本机新的那份")],
            []);

        Assert.Equal("本机新的那份", Assert.Single(fromLocal.Notes).Content);
        Assert.False(fromLocal.TookAnythingFromRemote);

        var fromRemote = HomeworkNoteMerge.Merge(
            [],
            [Note(id, day, day.AddMinutes(1), "云端旧的那份"), Note(id, day, day.AddMinutes(20), "云端新的那份")]);

        Assert.Equal("云端新的那份", Assert.Single(fromRemote.Notes).Content);
    }

    /// <summary>
    ///     两边各建过一次同一份作业（Id 不同、科目与内容与截止日期都一样）时，融合后只留一条，
    ///     留下的那份取更新的——不然板子上会出现两张一模一样的卡片。
    /// </summary>
    [Fact]
    public void Merge_CollapsesSameNoteCreatedOnBothSides()
    {
        var day = new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.FromHours(8));
        var due = new DateOnly(2026, 10, 12);

        var merged = HomeworkNoteMerge.Merge(
            [Note(Guid.NewGuid(), day, day.AddMinutes(1), "<p>P12–15</p>", subject: "数学", dueDate: due)],
            [Note(Guid.NewGuid(), day, day.AddMinutes(40), "<p>P12–15</p>", subject: "数学", dueDate: due)]);

        var note = Assert.Single(merged.Notes);
        Assert.Equal(day.AddMinutes(40), note.UpdatedAt);
        Assert.Equal(1, merged.RemovedDuplicates);
    }

    /// <summary>看着像、其实不是同一条的要留着：科目不同、截止日期不同、创建日期不同都算两条。</summary>
    [Fact]
    public void Merge_KeepsNotesThatAreOnlySimilar()
    {
        var day = new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.FromHours(8));
        var due = new DateOnly(2026, 10, 12);

        var otherSubject = HomeworkNoteMerge.Merge(
            [Note(Guid.NewGuid(), day, day, "<p>P12–15</p>", subject: "数学", dueDate: due)],
            [Note(Guid.NewGuid(), day, day, "<p>P12–15</p>", subject: "物理", dueDate: due)]);

        var otherDueDate = HomeworkNoteMerge.Merge(
            [Note(Guid.NewGuid(), day, day, "<p>P12–15</p>", subject: "数学", dueDate: due)],
            [Note(Guid.NewGuid(), day, day, "<p>P12–15</p>", subject: "数学", dueDate: due.AddDays(1))]);

        var otherDay = HomeworkNoteMerge.Merge(
            [Note(Guid.NewGuid(), day, day, "<p>P12–15</p>", subject: "数学", dueDate: due)],
            [Note(Guid.NewGuid(), day.AddDays(1), day.AddDays(1), "<p>P12–15</p>", subject: "数学", dueDate: due)]);

        Assert.Equal(2, otherSubject.Notes.Count);
        Assert.Equal(2, otherDueDate.Notes.Count);
        Assert.Equal(2, otherDay.Notes.Count);
        Assert.Equal(0, otherSubject.RemovedDuplicates + otherDueDate.RemovedDuplicates + otherDay.RemovedDuplicates);
    }

    private static Note Note(Guid id, DateTimeOffset created, DateTimeOffset updated, string content,
        DateTimeOffset? cleanedAt = null, string subject = "", DateOnly? dueDate = null)
        => new()
        {
            Id = id,
            Content = content,
            Subject = subject,
            DueDate = dueDate,
            CreatedAt = created,
            UpdatedAt = updated,
            CleanedAt = cleanedAt
        };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "corkboard-cloud-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
