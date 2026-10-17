using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Board;
using Corkboard.Core.Homework;
using Corkboard.Core.Models;
using Corkboard.Core.Models.SubConfigs.Cloud;
using Corkboard.Core.Notes;
using Corkboard.Core.Services.Cloud;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Auth;
using Corkboard.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Corkboard.Services.Cloud;

/// <summary>
///     云同步：把作业板与设置作为文件备份到思拓创联云存储，云端有新东西时自动拿下来或者并进来。
/// </summary>
/// <remarks>
///     <para>
///         本机没动过就整体拉取；两边都动过就**并集融合**（<c>HomeworkNoteMerge</c>）：作业按 Id 合到一起、
///         同一条取更新的那份，合完把结果传回云端让两边收敛。绝不整体覆盖用户刚写的作业——作业是
///         自由文本 + 富文本，逐字段拼出来的是谁也不认识的一条，所以只按整条取新。
///     </para>
///     <para>
///         作业的并集**不认识手动删除**：本机删掉的作业如果云端那份还在会被带回来（数据里没有删除墓碑）。
///         冲突时把作业找回来比悄悄丢一条安全，先按这个来。
///     </para>
///     <para>
///         防自激：同步成功后要写 <see cref="CloudSyncSettingsConfig.LastSyncedAt" />，而配置一写就触发
///     <see cref="ConfigHandlerBase{T}.Saved" />、又会拉起一次上传。所以批量写这些簿记字段时用
///     <c>_synchronizing</c> 挡住自己的事件。
///     </para>
///     <para>
///         判断「设置有没有改动」用内容摘要而不是文件时间：窗口被拖动这类**被排除同步**的操作也会写
///     配置文件，用文件时间当依据的话界面永远显示本机有改动、再也不敢自动拉取。
///     </para>
/// </remarks>
public sealed class CloudSyncService : ICloudSyncService, IDisposable
{
    /// <summary>
    ///     作业板改动后等这么久再上传。布置 / 修改 / 删除作业都是「一次操作一次落盘」，
    ///     等几秒没人再动就传，用户不用自己去点「立即备份」。
    /// </summary>
    private static readonly TimeSpan BoardAutoUploadDelay = TimeSpan.FromSeconds(5);

    /// <summary>设置改动后等这么久再上传：设置常常一连串地改（拖滑杆、连点开关），攒久一点再传。</summary>
    private static readonly TimeSpan SettingsAutoUploadDelay = TimeSpan.FromSeconds(45);

    /// <summary>
    ///     隔多久问一次云端有没有变化。
    ///     <para>
    ///         别的设备传了新备份、或者把云端清空了，本机必须在几十秒内自己知道，
    ///     而不是等用户打开设置页点「刷新」。一分钟一次、一次两个请求，代价可以接受。
    ///     </para>
    /// </summary>
    private static readonly TimeSpan RemotePollInterval = TimeSpan.FromSeconds(60);

    private const string ProductName = "Corkboard";
    private const string BoardDirectoryName = "board";
    private const string WorkDirectoryName = "cloud-sync";

    private readonly MainConfigHandler _configHandler;
    private readonly IBoardService _board;
    private readonly ICloudStorageClient _client;
    private readonly ISectlAuthService _auth;
    private readonly ConfigServiceBase _configService;
    private readonly ILogger<CloudSyncService>? _logger;

    private readonly object _gate = new();
    private CloudStorageUsage _usage = CloudStorageUsage.Empty;
    private CloudFileInfo? _remoteBoard;
    private CloudFileInfo? _remoteSettings;
    private DateTimeOffset? _remoteUpdatedAt;
    private bool _remoteIsNewer;
    private bool _hasCloudFiles;
    private bool _remoteCleared;
    private bool _isBusy;
    private string? _lastError;

    private DispatcherTimer? _autoUpload;

    /// <summary>
    ///     已经排着的那次自动上传的截止时间。两次改动挨在一起时（作业落盘往往紧跟一次配置落盘），
    ///     按更早的那个算：否则先排的 5 秒会被后到的 45 秒顶掉，作业要等 45 秒才传上去。
    /// </summary>
    private DateTimeOffset? _autoUploadDeadline;

    /// <summary>问云端有没有变化的定时器（<see cref="RemotePollInterval" /> 一次）。</summary>
    private DispatcherTimer? _remotePoll;

    private bool _synchronizing;

    /// <summary>
    ///     上一次自动拉取是冲着哪个云端版本去的。
    ///     <para>
    ///         后台轮询会一遍遍地走到自动拉取这一步，同一个版本只试一次：拉成功了不用再拉，
    ///     拉失败（比如分片缺了）也不该每分钟重试一遍。云端的版本号变了才会再试。
    ///     </para>
    /// </summary>
    private DateTimeOffset? _autoPulledRemote;

    /// <summary>
    ///     上一次融合是冲着哪个云端版本去的：和自动拉取同一个道理，同一个版本只融合一次，
    ///     融合失败（比如分片缺了）也不每分钟重试一遍。
    /// </summary>
    private DateTimeOffset? _mergedRemoteBoard;

    /// <summary>上一次看见的云同步开关状态，用来认出「刚把开关打开」这个动作。</summary>
    private bool _lastEnabledSeen;

    /// <summary>上一次看见的登录状态，用来认出「刚登录」这个动作（令牌刷新不算）。</summary>
    private bool _signedInSeen;

    private bool _initialized;
    private bool _disposed;

    public CloudSyncService(MainConfigHandler configHandler,
        IBoardService board,
        ICloudStorageClient client,
        ISectlAuthService auth,
        ConfigServiceBase configService,
        ILogger<CloudSyncService>? logger = null)
    {
        _configHandler = configHandler;
        _board = board;
        _client = client;
        _auth = auth;
        _configService = configService;
        _logger = logger;

        _configHandler.Saved += OnConfigSaved;
        _board.Changed += OnBoardChanged;
        _auth.StateChanged += OnAuthStateChanged;
    }

    public event EventHandler? StateChanged;

    public CloudStorageUsage Usage
    {
        get
        {
            lock (_gate)
            {
                return _usage;
            }
        }
    }

    public CloudSyncScope CurrentScope => ScopeOf(Config);

    public bool HasCloudBackup
    {
        get
        {
            lock (_gate)
            {
                return _remoteBoard is not null || _remoteSettings is not null;
            }
        }
    }

    public bool HasBoardBackup
    {
        get
        {
            lock (_gate)
            {
                return _remoteBoard is not null;
            }
        }
    }

    public bool HasSettingsBackup
    {
        get
        {
            lock (_gate)
            {
                return _remoteSettings is not null;
            }
        }
    }

    public bool HasCloudFiles
    {
        get
        {
            lock (_gate)
            {
                return _hasCloudFiles;
            }
        }
    }

    /// <summary>云端那份备份在别处被清空了（本机簿记也跟着清了），界面据此提示用户重新备份。</summary>
    public bool RemoteCleared
    {
        get
        {
            lock (_gate)
            {
                return _remoteCleared;
            }
        }
    }

    /// <summary>
    ///     本机上次同步成功的时间。**每次都从配置句柄现读**，不能用页面构造时拿到的那份配置对象：
    ///     从云端恢复设置会把配置对象整个换掉，旧引用还停在恢复前的时间上。
    /// </summary>
    public DateTimeOffset? LastSyncedAt => _configHandler.Data.Cloud.LastSyncedAt;

    public DateTimeOffset? RemoteUpdatedAt
    {
        get
        {
            lock (_gate)
            {
                return _remoteUpdatedAt;
            }
        }
    }

    public bool RemoteIsNewer
    {
        get
        {
            lock (_gate)
            {
                return _remoteIsNewer;
            }
        }
    }

    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _isBusy;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    private CloudSyncSettingsConfig Config => _configHandler.Data.Cloud;

    private static string BoardDirectory => Utils.GetDirectoryPath(BoardDirectoryName);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _initialized = true;
        _lastEnabledSeen = Config.Enabled;
        _signedInSeen = _auth.IsSignedIn;
        if (!_auth.IsSignedIn)
            return;

        // 别的设备传了新备份、或者把云端清空了，本机不靠用户点「刷新」也能自己知道。
        StartRemotePoll();

        // 用量和云备份清单都是账号维度的，开关没开也该显示出来（设置页要看剩余空间），
        // 「本机没有改动时自动拉取」才是开关管的事，所以 autoPull 只在开启云同步时才带。
        await SafeCheckAsync(Config.Enabled, false, cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => CheckAsync(false, false, false, cancellationToken);

    public async Task BackupAsync(CloudSyncScope scope, CancellationToken cancellationToken = default)
    {
        if (scope == CloudSyncScope.None)
            return;

        EnsureSignedIn();
        SetBusy(true);
        try
        {
            await BackupCoreAsync(scope, cancellationToken);
            await CheckAsync(false, true, false, cancellationToken);
        }
        catch (Exception exception)
        {
            RecordError(exception);
            throw;
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task RestoreAsync(CloudSyncScope scope, CancellationToken cancellationToken = default)
    {
        if (scope == CloudSyncScope.None)
            return;

        EnsureSignedIn();
        SetBusy(true);
        try
        {
            await RestoreCoreAsync(scope, cancellationToken);
            await CheckAsync(false, true, false, cancellationToken);
        }
        catch (Exception exception)
        {
            RecordError(exception);
            throw;
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    ///     清空 Corkboard 在云端的备份：整包、分片、清单、设置全删掉，本机数据一点不动。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         只删自己这一套文件名（<see cref="CloudBackupFiles.AllManagedFiles" />）：平台上还可能有别的文件，
    ///         清空云端不该把它们一起带走。中断的上传留下的孤儿分片也在删除范围内——它们占着配额，
    ///         又凑不成一份能恢复的备份。
    ///     </para>
    ///     <para>
    ///         簿记要一起清：删完之后本机已经没有「已同步」这回事，<c>LastSyncedAt</c> 留着会让界面显示一个
    ///         早就不存在的同步时间，还会让「本机有没有改动」误判成不用再传。另外先把排着的自动上传掐掉，
    ///         否则用户刚清完云端，几秒钟后那份备份又自己传回去了。
    ///     </para>
    ///     <para>
    ///         部分失败要说话：删不掉的那几个文件留着，报错让用户再点一次，别装作全清干净了。
    ///     </para>
    /// </remarks>
    public async Task ClearCloudAsync(CancellationToken cancellationToken = default)
    {
        EnsureSignedIn();
        StopAutoUpload();
        SetBusy(true);
        try
        {
            var files = await _client.ListFilesAsync(cancellationToken);
            var targets = CloudBackupFiles.AllManagedFiles(files);
            var failed = 0;

            foreach (var file in targets)
            {
                try
                {
                    await _client.DeleteAsync(file.FileId, cancellationToken);
                }
                catch (Exception exception) when (exception is CloudStorageException or HttpRequestException)
                {
                    failed++;
                    _logger?.LogWarning(exception, "清空云端时删不掉这个云文件：{FileName}。", file.FileName);
                }
            }

            ResetSyncBookkeeping();

            // 用量与文件清单要重查一遍（云端已经空了，界面上的占用不能还停在删之前）。
            // 这一步失败不该盖掉「已经删完了」这个事实，所以只记日志。
            await SafeCheckAsync(false, false, cancellationToken);

            _logger?.LogInformation("已清空云端备份（{Count} 个文件，{Failed} 个没删掉）。", targets.Count, failed);

            if (failed > 0)
                throw new CloudStorageException("delete_failed",
                    $"有 {failed} 个云文件没删掉，稍后再点一次「清空云端」");
        }
        catch (Exception exception)
        {
            RecordError(exception);
            throw;
        }
        finally
        {
            SetBusy(false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _configHandler.Saved -= OnConfigSaved;
        _board.Changed -= OnBoardChanged;
        _auth.StateChanged -= OnAuthStateChanged;
        StopAutoUpload();
        StopRemotePoll();
    }

    private void EnsureSignedIn()
    {
        if (!_auth.IsSignedIn)
            throw new CloudStorageException("unauthorized", "先登录思拓创联账号再使用云同步");
    }

    /// <summary>查一遍云端（用量 + 备份文件）；失败时抛出，由调用方决定怎么显示。</summary>
    /// <param name="background">
    ///     后台轮询发起的查询：不占界面的忙闲状态，失败也只记日志。
    ///     每分钟一次的动作要是会闪一下「正在同步」、或者留一条红字，比不同步还烦人。
    /// </param>
    private async Task CheckAsync(bool autoPull, bool assumeSynced, bool background, CancellationToken cancellationToken)
    {
        if (!background)
            SetBusy(true);

        try
        {
            var usage = await _client.GetUsageAsync(false, cancellationToken);
            var files = await _client.ListFilesAsync(cancellationToken);
            ApplyCloudFiles(usage, files, assumeSynced);

            lock (_gate)
            {
                _lastError = null;
            }

            NotifyState();

            // 别处把云端清空了：本机跟着清簿记并提示用户重新备份。
            DetectRemoteCleared();

            if (autoPull)
                await AutoPullAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            if (background)
                _logger?.LogDebug(exception, "后台查询云存储状态失败。");
            else
                RecordError(exception);

            throw;
        }
        finally
        {
            if (!background)
                SetBusy(false);
        }
    }

    /// <summary>启动期、事件与后台轮询里的查询：失败只记日志，绝不把异常抛到没人接的地方。</summary>
    private async Task SafeCheckAsync(bool autoPull, bool background, CancellationToken cancellationToken)
    {
        try
        {
            await CheckAsync(autoPull, false, background, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogDebug(exception, "查询云存储状态失败。");
        }
    }

    private void ApplyCloudFiles(CloudStorageUsage usage, IReadOnlyList<CloudFileInfo> files, bool assumeSynced)
    {
        var board = CloudBackupFiles.FindBoard(files);
        var settings = CloudBackupFiles.FindSettings(files);
        var scope = CurrentScope;
        var remote = Later(
            scope.HasFlag(CloudSyncScope.Board) ? board?.UpdatedAt : null,
            scope.HasFlag(CloudSyncScope.Settings) ? settings?.UpdatedAt : null);

        var hasCloudFiles = CloudBackupFiles.AllManagedFiles(files).Count > 0;

        lock (_gate)
        {
            _usage = usage;
            _remoteBoard = board;
            _remoteSettings = settings;
            _remoteUpdatedAt = Later(board?.UpdatedAt, settings?.UpdatedAt);
            _remoteIsNewer = !assumeSynced && IsRemoteNewer(remote);
            _hasCloudFiles = hasCloudFiles;
        }

        NotifyState();
    }

    private bool IsRemoteNewer(DateTimeOffset? remote)
        => remote is { } value &&
           (Config.LastSyncedAt is not { } last || value > last + CloudSyncPolicy.Tolerance);

    /// <summary>本机没有未同步改动时自动拉取；两边都动过时改成并集融合，绝不整体覆盖本机。</summary>
    private async Task AutoPullAsync(CancellationToken cancellationToken)
    {
        var config = Config;
        if (!config.Enabled || !config.AutoPull || !_auth.IsSignedIn)
            return;

        var scope = ScopeOf(config);
        if (scope == CloudSyncScope.None || RemoteUpdatedAtFor(scope) is not { } remote)
            return;

        var (boardDirty, settingsDirty) = ComputeDirty(scope);
        var dirty = boardDirty || settingsDirty;
        var now = DateTimeOffset.Now;

        if (CloudSyncPolicy.ShouldAutoPull(remote, config.LastSyncedAt, dirty ? now : null))
        {
            lock (_gate)
            {
                // 同一个云端版本只自动拉一次：轮询会不停地走到这里，拉过了就别再拉。
                if (_autoPulledRemote == remote)
                    return;

                _autoPulledRemote = remote;
            }

            _logger?.LogInformation("本机没有未同步的改动，正在从云端恢复备份。");
            await RestoreCoreAsync(scope, cancellationToken);
            return;
        }

        // 两边都改过：谁覆盖谁都会丢东西，改成并集融合。
        if (!boardDirty || !scope.HasFlag(CloudSyncScope.Board))
            return;

        if (RemoteUpdatedAtFor(CloudSyncScope.Board) is not { } remoteBoard)
            return;

        if (!CloudSyncPolicy.ShouldMerge(remoteBoard, config.LastSyncedAt, now))
            return;

        lock (_gate)
        {
            if (_mergedRemoteBoard == remoteBoard)
                return;

            _mergedRemoteBoard = remoteBoard;
        }

        await MergeBoardAsync(cancellationToken);
    }

    /// <summary>
    ///     云端与本机都有改动时的收尾：把云端的作业并到本机（并集），再把合并结果传回云端。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         本机已有的作业一条不动：只补云端多出来的、以及同一条里云端更新的那份——冲突时宁可多留一条，
    ///     也不能把用户本机的内容冲掉。设置不参与这次融合：作业是共同的内容，设置多半是本机各自的偏好。
    ///     </para>
    ///     <para>
    ///         合完必须传回去：云端那份还只有一半，传上去两边才真正一样；这次上传顺带刷新「上次同步」，
    ///     否则下一轮轮询还会撞上同一个云端版本。
    ///     </para>
    /// </remarks>
    private async Task MergeBoardAsync(CancellationToken cancellationToken)
    {
        var files = await _client.ListFilesAsync(cancellationToken).ConfigureAwait(false);
        var boardBackup = CloudBackupFiles.ReadBoardBackup(files);
        if (!boardBackup.HasAny)
            return;

        var work = CreateWorkDirectory();
        try
        {
            var archivePath = Path.Combine(work, CloudBackupFiles.BoardArchiveName);
            try
            {
                if (boardBackup.Manifest is not null)
                {
                    await DownloadPackageAsync(boardBackup, archivePath, work, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await DownloadLegacyBoardAsync(boardBackup, archivePath, work, cancellationToken)
                        .ConfigureAwait(false);
                }

                var staging = Path.Combine(work, "cloud-board");
                if (CloudBackupArchive.Extract(archivePath, staging) == 0)
                {
                    _logger?.LogInformation("云端的作业板备份里没有作业，这次不融合。");
                }
                else
                {
                    var remoteNotes = new NoteStore(NullLogger<NoteStore>.Instance, staging).LoadAll();
                    var localNotes = _board.Notes.ToList();
                    var merged = HomeworkNoteMerge.Merge(localNotes, remoteNotes);

                    _logger?.LogInformation(
                        "云端和本机都有改动，已按并集融合：本机 {Local} 条 + 云端 {Remote} 条 → 共 {Total} 条" +
                        "（云端补进来 {Added} 条、更新 {Updated} 条、合并重复 {Removed} 条）。",
                        localNotes.Count, remoteNotes.Count, merged.Notes.Count, merged.AddedFromRemote,
                        merged.UpdatedFromRemote, merged.RemovedDuplicates);

                    if (merged.TookAnythingFromRemote)
                    {
                        // 挡掉自己这次 Reload 触发的 Changed：下面的上传就在眼前，不用再排一次。
                        _synchronizing = true;
                        try
                        {
                            // 就地写回本机的日期文件，**不能**整体换目录：那样会连 board.json
                            // （科目表与手动排序）一起抹掉，而它不在并集的管辖范围里。
                            new NoteStore(NullLogger<NoteStore>.Instance, BoardDirectory)
                                .SaveAll(merged.Notes);

                            CopyBoardFiles(staging, BoardDirectory, overwrite: false);
                            _board.Reload();
                        }
                        finally
                        {
                            _synchronizing = false;
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or FormatException or IOException)
            {
                _logger?.LogWarning(exception, "云端作业板备份读不出来，这次不融合。");
                throw new CloudStorageException("invalid_backup", "云端的作业板备份读不出来，可能已经损坏");
            }

            await BackupCoreAsync(CloudSyncScope.Board, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DeleteDirectory(work);
        }
    }

    private DateTimeOffset? RemoteUpdatedAtFor(CloudSyncScope scope)
    {
        lock (_gate)
        {
            return Later(
                scope.HasFlag(CloudSyncScope.Board) ? _remoteBoard?.UpdatedAt : null,
                scope.HasFlag(CloudSyncScope.Settings) ? _remoteSettings?.UpdatedAt : null);
        }
    }

    private (bool Board, bool Settings) ComputeDirty(CloudSyncScope scope)
    {
        var lastSyncedAt = Config.LastSyncedAt;
        var boardDirty = scope.HasFlag(CloudSyncScope.Board) && IsBoardDirty(lastSyncedAt);
        var settingsDirty = scope.HasFlag(CloudSyncScope.Settings) && IsSettingsDirty();
        return (boardDirty, settingsDirty);
    }

    private static bool IsBoardDirty(DateTimeOffset? lastSyncedAt)
        => CloudSyncPolicy.NeedsUpload(DirectoryLastWrite(BoardDirectory), lastSyncedAt);

    private bool IsSettingsDirty()
    {
        var hash = ComputeSettingsHash();
        if (hash is null)
            return false;

        var last = Config.LastSettingsHash;
        return last is null || !string.Equals(hash, last, StringComparison.Ordinal);
    }

    /// <summary>本地设置（排除本机专属项之后）的内容摘要；读不出来时返回 <c>null</c>。</summary>
    private string? ComputeSettingsHash()
    {
        try
        {
            var path = _configHandler.Data.ConfigFilePath;
            if (!File.Exists(path))
                return null;

            var stripped = CloudSyncExclusions.StripForUpload(File.ReadAllText(path));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stripped)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogDebug(exception, "计算设置内容摘要失败。");
            return null;
        }
    }

    private async Task BackupCoreAsync(CloudSyncScope scope, CancellationToken cancellationToken)
    {
        var work = CreateWorkDirectory();
        try
        {
            var existing = await _client.ListFilesAsync(cancellationToken);
            var settingsUploaded = false;

            if (scope.HasFlag(CloudSyncScope.Board))
                await BackupBoardAsync(existing, work, cancellationToken).ConfigureAwait(false);

            if (scope.HasFlag(CloudSyncScope.Settings))
            {
                var path = _configHandler.Data.ConfigFilePath;
                if (File.Exists(path))
                {
                    var stripped = CloudSyncExclusions.StripForUpload(await File.ReadAllTextAsync(path, cancellationToken));
                    var replaced = CloudBackupFiles.AllSettingsFiles(existing).ToList();
                    var bytes = Encoding.UTF8.GetBytes(stripped);

                    // 上传前的保底：算上会被顶掉的旧件还装不下，就别白跑一趟请求。
                    EnsureQuotaForUpload(bytes.LongLength, TotalSize(replaced));

                    var fileId = await _client.UploadAsync(bytes, CloudBackupFiles.SettingsFileName, cancellationToken);
                    await DeleteReplacedAsync(replaced, fileId, cancellationToken);
                    settingsUploaded = true;
                }
            }

            CommitSyncState(settingsUploaded);
            _logger?.LogInformation("已上传云备份（{Scope}）。", scope);
        }
        finally
        {
            DeleteDirectory(work);
        }
    }

    /// <summary>
    ///     传作业板备份：压缩包切成几片逐片上传，清单最后传。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         为什么要分片：云函数的请求体上限约 1 MiB，整包 base64 之后很容易撞上；作业里的图片是以
    ///         data URI 嵌在正文里的，几篇带图的作业就能到几十 MB。分片之后单次请求固定几百 KB。
    ///     </para>
    ///     <para>
    ///         顺序是刻意的：清单在，这份备份才算完整。中途失败就把这次已经传上去的文件全删掉——
    ///         上一次的备份原封不动，云端也不会留下半份备份骗过下一次恢复。
    ///     </para>
    /// </remarks>
    private async Task BackupBoardAsync(IReadOnlyList<CloudFileInfo> existing, string work,
        CancellationToken cancellationToken)
    {
        var archivePath = Path.Combine(work, CloudBackupFiles.BoardArchiveName);
        var archiveManifest = new CloudBackupManifest(ProductName, GlobalConstants.Version, GlobalConstants.PlatformId,
            DateTimeOffset.Now);
        CloudBackupArchive.Create(PrepareBoardUploadDirectory(work), archivePath, archiveManifest);

        var payload = await File.ReadAllBytesAsync(archivePath, cancellationToken).ConfigureAwait(false);
        if (payload.LongLength > CloudBackupPackage.MaxArchiveBytes)
        {
            throw new CloudStorageException("payload_too_large",
                $"作业板备份有 {CloudStorageFormat.Bytes(payload.LongLength)}，" +
                $"超过云端备份上限 {CloudStorageFormat.Bytes(CloudBackupPackage.MaxArchiveBytes)}");
        }

        var parts = CloudBackupPackage.Slice(payload);
        var manifest = CloudBackupPackage.BuildManifest(
            CloudBackupPackage.CreateBackupId(DateTimeOffset.Now),
            parts,
            CloudBackupPackage.DefaultPartBytes,
            [BoardDirectoryName],
            DateTimeOffset.Now,
            GlobalConstants.Version,
            CloudBackupFiles.BoardArchiveName,
            payload);

        // 上传前的保底：这次要占的空间，减掉会被顶掉的旧件之后还装不下就别发请求。
        var previous = CloudBackupFiles.AllBoardFiles(existing).ToList();
        EnsureQuotaForUpload(payload.LongLength, TotalSize(previous));

        await UploadPackageAsync(manifest, parts, cancellationToken).ConfigureAwait(false);
        await DeleteBoardBackupsExceptAsync(previous, manifest, cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("已上传作业板云备份 {BackupId}（{Bytes} 字节，{Parts} 片）。",
            manifest.BackupId, payload.LongLength, parts.Count);
    }

    /// <summary>
    ///     这次要打包装上去的作业板目录。
    /// </summary>
    /// <remarks>
    ///     没设窗口（<c>cloud.sync_days</c> 是 <c>0</c>）就直接用本机目录，一份文件都不用复制；
    ///     设了窗口就只把窗口内的作业写进临时目录：窗口外的历史作业留在本机。
    ///     科目表与手动排序在 <c>board.json</c> 里，不分窗口，永远跟着备份走。
    /// </remarks>
    private string PrepareBoardUploadDirectory(string work)
    {
        if (UploadCutoff() is not { } cutoff)
            return BoardDirectory;

        var directory = Path.Combine(work, "upload-board");
        var notes = new NoteStore(NullLogger<NoteStore>.Instance, BoardDirectory)
            .LoadAll()
            .Where(note => note.CreatedAt >= cutoff)
            .ToList();

        new NoteStore(NullLogger<NoteStore>.Instance, directory).SaveAll(notes);
        CopyBoardFiles(BoardDirectory, directory, overwrite: false);

        _logger?.LogInformation("只同步最近 {Days} 天的作业：这次传 {Count} 条，窗口外的都留在本机。",
            Config.SyncDays, notes.Count);

        return directory;
    }

    /// <summary>「只同步最近 N 天」的窗口起点；不限时返回 <c>null</c>。</summary>
    private DateTimeOffset? UploadCutoff() => CloudSyncPolicy.UploadCutoff(Config.SyncDays, DateTimeOffset.Now);

    /// <summary>按顺序传分片、最后传清单；任何一步出错就把这次已经传上去的全删掉。</summary>
    private async Task UploadPackageAsync(CloudBackupPackageManifest manifest, IReadOnlyList<byte[]> parts,
        CancellationToken cancellationToken)
    {
        var uploaded = new List<string>();
        try
        {
            for (var index = 0; index < parts.Count; index++)
            {
                var entry = manifest.Parts[index];
                var fileId = await _client.UploadAsync(parts[index], entry.Name, cancellationToken).ConfigureAwait(false);
                if (fileId is not null)
                    uploaded.Add(fileId);

                _logger?.LogDebug("已上传作业板备份分片 {Index}/{Count}（{Name}）。", entry.Index, parts.Count,
                    entry.Name);
            }

            var json = Encoding.UTF8.GetBytes(CloudBackupPackage.SerializeManifest(manifest));
            var manifestId = await _client
                .UploadAsync(json, CloudBackupPackage.BuildManifestName(manifest.BackupId), cancellationToken)
                .ConfigureAwait(false);
            if (manifestId is not null)
                uploaded.Add(manifestId);
        }
        catch
        {
            await RollbackAsync(uploaded, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>上传失败后的收尾：把这次传上去的文件删掉，别在云端留下半份备份。</summary>
    private async Task RollbackAsync(IEnumerable<string> fileIds, CancellationToken cancellationToken)
    {
        foreach (var fileId in fileIds)
        {
            try
            {
                await _client.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is CloudStorageException or HttpRequestException)
            {
                _logger?.LogDebug(exception, "回滚云备份文件失败：{FileId}。", fileId);
            }
        }
    }

    /// <summary>
    ///     这次上传成功之后，把云端其它的作业板备份清掉：上一代的分片与清单、更早的老格式整包、
    ///     以及历史上中断留下的残留分片。清单在恢复时是会校验的，所以这些都不会被误认成一份完整备份。
    /// </summary>
    private async Task DeleteBoardBackupsExceptAsync(IEnumerable<CloudFileInfo> previous,
        CloudBackupPackageManifest manifest, CancellationToken cancellationToken)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CloudBackupPackage.BuildManifestName(manifest.BackupId)
        };
        foreach (var part in manifest.Parts)
            keep.Add(part.Name);

        foreach (var file in previous)
        {
            if (keep.Contains(file.FileName))
                continue;

            try
            {
                await _client.DeleteAsync(file.FileId, cancellationToken).ConfigureAwait(false);
            }
            catch (CloudStorageException exception)
            {
                _logger?.LogDebug(exception, "云端旧备份没删掉，不影响这次备份。");
            }
        }
    }

    /// <summary>
    ///     上传前的保底：把这次要传的体积和被顶掉的旧件一起算，装不下就不发请求，直接告诉用户还差多少。
    ///     用量还没取到（总容量为 0）时不拦，交给服务端的配额错误说话。
    /// </summary>
    private void EnsureQuotaForUpload(long payloadBytes, long freedBytes)
    {
        var usage = Usage;
        if (usage.TotalStorage <= 0)
            return;

        var needed = payloadBytes - freedBytes;
        if (needed <= 0 || needed <= usage.AvailableStorage)
            return;

        throw new CloudStorageException("storage_exceeded",
            $"云端空间不够了：这次要传 {CloudStorageFormat.Bytes(payloadBytes)}，" +
            $"云端只剩 {CloudStorageFormat.Bytes(usage.AvailableStorage)}");
    }

    private static long TotalSize(IEnumerable<CloudFileInfo> files) => files.Sum(file => file.Size);

    private async Task RestoreCoreAsync(CloudSyncScope scope, CancellationToken cancellationToken)
    {
        var files = await _client.ListFilesAsync(cancellationToken);
        var boardBackup = CloudBackupFiles.ReadBoardBackup(files);
        var settingsFile = CloudBackupFiles.FindSettings(files);

        var wantBoard = scope.HasFlag(CloudSyncScope.Board) && boardBackup.HasAny;
        var wantSettings = scope.HasFlag(CloudSyncScope.Settings) && settingsFile is not null;
        if (!wantBoard && !wantSettings)
            throw new CloudStorageException("file_not_found", "云端还没有可恢复的备份");

        var work = CreateWorkDirectory();
        var settingsRestored = false;
        try
        {
            if (wantBoard)
            {
                var archivePath = Path.Combine(work, CloudBackupFiles.BoardArchiveName);
                try
                {
                    if (boardBackup.Manifest is not null)
                    {
                        await DownloadPackageAsync(boardBackup, archivePath, work, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        // 老格式：一整包传上去的那份，还没有清单可以校验。
                        await DownloadLegacyBoardAsync(boardBackup, archivePath, work, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    // 先解到临时目录：包是坏的、或者里面没有作业板数据，都还没碰到现有数据。
                    var staging = Path.Combine(work, "board");
                    var count = CloudBackupArchive.Extract(archivePath, staging);
                    if (count == 0 && DirectoryHasFiles(BoardDirectory))
                        throw new CloudStorageException("invalid_backup", "这份云备份里没有作业板数据，已停止恢复");

                    if (UploadCutoff() is { } cutoff)
                        RestoreBoardWithinWindow(staging, cutoff);
                    else
                        ReplaceDirectory(staging, BoardDirectory);

                    _board.Reload();
                }
                catch (Exception exception) when (exception is InvalidDataException or FormatException or IOException)
                {
                    _logger?.LogWarning(exception, "云端作业板备份解析失败。");
                    throw new CloudStorageException("invalid_backup", "云端的作业板备份读不出来，可能已经损坏");
                }
            }

            if (wantSettings)
            {
                var remotePath = Path.Combine(work, CloudBackupFiles.SettingsFileName);
                await _client.DownloadAsync(settingsFile!.FileId, remotePath, cancellationToken);

                var remote = await File.ReadAllTextAsync(remotePath, cancellationToken);
                var localPath = _configHandler.Data.ConfigFilePath;
                var local = File.Exists(localPath) ? await File.ReadAllTextAsync(localPath, cancellationToken) : "{}";
                var merged = CloudSyncExclusions.MergeRemoteOntoLocal(local, remote);

                var model = JsonSerializer.Deserialize<MainConfigModel>(merged, ConfigServiceBase.JsonOptions)
                            ?? throw new CloudStorageException("invalid_backup", "云端的设置文件读不出来，已停止恢复");

                _synchronizing = true;
                try
                {
                    // 走配置服务落盘再让配置句柄重读，别自己写 settings.json——免得绕过解析与迁移逻辑。
                    _configService.SaveConfig(model);
                    _configHandler.Reload();
                }
                finally
                {
                    _synchronizing = false;
                }

                settingsRestored = true;
            }
        }
        finally
        {
            DeleteDirectory(work);
        }

        CommitSyncState(settingsRestored);
        _logger?.LogInformation("已从云端恢复备份（{Scope}）。", scope);
    }

    /// <summary>
    ///     设了「只同步最近 N 天」时的作业板恢复：只让云端那份覆盖窗口内的部分，窗口外的本机历史作业原样留着。
    /// </summary>
    /// <remarks>
    ///     云端那份本来就只带窗口内的作业；要是照样整体换目录，本机窗口外那些老作业（云端压根没有）
    ///     就会被一起删掉。所以这里按并集写回：窗口外的本机作业 + 云端带下来的作业，再让板级文件
    ///     （<c>board.json</c>）以云端为准——用户点的是「用云端备份覆盖本机」。
    /// </remarks>
    private static void RestoreBoardWithinWindow(string staging, DateTimeOffset cutoff)
    {
        var local = new NoteStore(NullLogger<NoteStore>.Instance, BoardDirectory)
            .LoadAll()
            .Where(note => note.CreatedAt < cutoff)
            .ToList();
        var remote = new NoteStore(NullLogger<NoteStore>.Instance, staging).LoadAll();
        var merged = HomeworkNoteMerge.Merge(local, remote);

        new NoteStore(NullLogger<NoteStore>.Instance, BoardDirectory).SaveAll(merged.Notes);
        CopyBoardFiles(staging, BoardDirectory, overwrite: true);
    }

    /// <summary>
    ///     按清单把作业板备份下载回来：清单 → 逐个分片 → 拼接并校验摘要，最后把压缩包写到
    ///     <paramref name="archivePath" />。
    /// </summary>
    private async Task DownloadPackageAsync(CloudBoardBackup boardBackup, string archivePath, string work,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(work, "board-manifest.json");
        await _client.DownloadAsync(boardBackup.Manifest!.FileId, manifestPath, cancellationToken)
            .ConfigureAwait(false);

        var manifest = CloudBackupPackage.DeserializeManifest(await File.ReadAllTextAsync(manifestPath, cancellationToken))
                       ?? throw new CloudStorageException("invalid_backup", "云端的作业板备份清单读不出来，可能已经损坏");
        CloudBackupPackage.ValidateManifest(manifest);

        var missing = CloudBackupPackage.MissingParts(manifest, boardBackup.Parts);
        if (missing.Count > 0)
        {
            throw new CloudStorageException("incomplete_backup",
                $"云端的作业板备份不完整（少了 {missing.Count} 片），重新点一次「立即备份」就好");
        }

        var byName = boardBackup.Parts.ToDictionary(file => file.FileName, StringComparer.OrdinalIgnoreCase);
        var payloads = new List<byte[]>(manifest.Parts.Count);
        for (var index = 0; index < manifest.Parts.Count; index++)
        {
            var entry = manifest.Parts[index];
            var partPath = Path.Combine(work, $"board-part-{entry.Index:D2}");
            await _client.DownloadAsync(byName[entry.Name].FileId, partPath, cancellationToken).ConfigureAwait(false);
            payloads.Add(await File.ReadAllBytesAsync(partPath, cancellationToken).ConfigureAwait(false));
        }

        await File.WriteAllBytesAsync(archivePath, CloudBackupPackage.MergeAndVerify(manifest, payloads), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>恢复最早那种单文件备份（可能还是被云端按 UTF-8 弄坏过的那份，尽量抢救）。</summary>
    private async Task DownloadLegacyBoardAsync(CloudBoardBackup boardBackup, string archivePath, string work,
        CancellationToken cancellationToken)
    {
        var file = boardBackup.NewestLegacy
                   ?? throw new CloudStorageException("file_not_found", "云端还没有可恢复的备份");

        // 用自己拼的文件名落盘：云端的 filename 是服务端数据，不能直接拿去拼路径。
        var isBase64 = CloudBackupFiles.IsBase64BoardArchive(file.FileName);
        var downloadPath = Path.Combine(work, "board-download" + (isBase64 ? CloudBackupFiles.Base64Suffix : ".zip"));
        await _client.DownloadAsync(file.FileId, downloadPath, cancellationToken).ConfigureAwait(false);

        if (!isBase64)
        {
            // 没加 base64 之前传上去的老件，原样搬过去（多半已经坏了，交给解压那一步报错）。
            File.Move(downloadPath, archivePath, true);
            return;
        }

        await CloudBackupPayload.WriteDecodedAsync(downloadPath, archivePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>同步成功后的簿记：记时间（必要时记设置摘要），并把自己触发的配置事件挡掉。</summary>
    private void CommitSyncState(bool recordSettingsHash)
    {
        _synchronizing = true;
        try
        {
            var config = Config;
            config.LastSyncedAt = DateTimeOffset.Now;
            if (recordSettingsHash)
                config.LastSettingsHash = ComputeSettingsHash();
        }
        finally
        {
            _synchronizing = false;
        }

        lock (_gate)
        {
            _remoteIsNewer = false;
            _remoteCleared = false;
            _lastError = null;
        }

        NotifyState();
    }

    /// <summary>
    ///     清空云端之后的簿记：本机不再有任何「已同步」状态，云端那份清单也一起作废。
    /// </summary>
    /// <param name="remoteCleared">
    ///     这次清空到底是不是「别处干的」。用户自己点「清空云端」时是 false——他刚做的事不用再提示一遍；
    ///     发现云端在别处被清空时是 true，界面要提示本机数据没事、但得重新备份。
    /// </param>
    /// <remarks>
    ///     写配置同样要挡掉 <see cref="ConfigHandlerBase{T}.Saved" />：这两笔是清空动作的收尾，
    ///     不是用户改了设置，不能顺手拉起一次自动上传。
    /// </remarks>
    private void ResetSyncBookkeeping(bool remoteCleared = false)
    {
        _synchronizing = true;
        try
        {
            var config = Config;
            config.LastSyncedAt = null;
            config.LastSettingsHash = null;
        }
        finally
        {
            _synchronizing = false;
        }

        lock (_gate)
        {
            _remoteBoard = null;
            _remoteSettings = null;
            _remoteUpdatedAt = null;
            _remoteIsNewer = false;
            _remoteCleared = remoteCleared;
            _lastError = null;
        }

        NotifyState();
    }

    /// <summary>
    ///     本机记着「同步过」，云端却一份备份都没有了：那是别处（另一台设备、另一个客户端）把云端清空了。
    /// </summary>
    /// <remarks>
    ///     本机跟着把簿记清掉，并让界面提示用户重新备份一份。不这么做的话界面还挂着一个早就不存在的
    ///     「上次同步」时间，本机也会以为自己不用再传，用户改完作业根本不会自动备份。
    /// </remarks>
    private void DetectRemoteCleared()
    {
        if (!CloudSyncPolicy.WasClearedRemotely(Config.LastSyncedAt, HasCloudBackup))
            return;

        _logger?.LogInformation("云端已经没有备份了（可能是别的设备清空的），本机同步簿记跟着清掉。");
        ResetSyncBookkeeping(remoteCleared: true);
    }

    /// <summary>
    ///     删掉被这次上传顶掉的云端旧件。同名可能有好几份（历史上重复上传留下的），一次全清，
    ///     否则空间白占、恢复时也可能挑到旧的那份。
    /// </summary>
    private async Task DeleteReplacedAsync(IEnumerable<CloudFileInfo> previous, string? newFileId,
        CancellationToken cancellationToken)
    {
        if (newFileId is null)
            return;

        foreach (var file in previous)
        {
            if (string.Equals(file.FileId, newFileId, StringComparison.Ordinal))
                continue;

            try
            {
                await _client.DeleteAsync(file.FileId, cancellationToken);
            }
            catch (CloudStorageException exception)
            {
                _logger?.LogDebug(exception, "云端旧备份没删掉，不影响这次备份。");
            }
        }
    }

    private void OnConfigSaved(object? sender, EventArgs e)
    {
        if (_disposed || _synchronizing)
            return;

        var enabled = Config.Enabled;
        var justEnabled = enabled && !_lastEnabledSeen;
        _lastEnabledSeen = enabled;

        if (!enabled)
        {
            StopAutoUpload();
            StopRemotePoll();
            return;
        }

        ScheduleAutoUpload(SettingsAutoUploadDelay);
        StartRemotePoll();

        // 刚打开同步开关：立刻看一眼云端有没有更新（不等下一轮 60 秒轮询），本机没改动就自动拿下来。
        // 只在「刚打开」这个动作上查，不是每次存配置都查：拖窗口这类本机专属设置也会写配置。
        if (justEnabled && _initialized && _auth.IsSignedIn)
            _ = SafeCheckAsync(true, false, CancellationToken.None);
    }

    private void OnBoardChanged(object? sender, EventArgs e)
    {
        if (_disposed || _synchronizing)
            return;

        if (Config.Enabled)
            ScheduleAutoUpload(BoardAutoUploadDelay);
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        if (_disposed || !_initialized)
            return;

        var signedIn = _auth.IsSignedIn;
        if (signedIn == _signedInSeen)
            return;

        _signedInSeen = signedIn;

        if (!signedIn)
        {
            lock (_gate)
            {
                _autoPulledRemote = null;
                _mergedRemoteBoard = null;
            }

            RunOnUiThread(() =>
            {
                StopAutoUpload();
                StopRemotePoll();
                ResetCloudState();
            });
            return;
        }

        // 刚登录：立刻查一遍（不等下一轮轮询），顺手把轮询起起来。
        RunOnUiThread(() =>
        {
            if (!Config.Enabled)
                return;

            StartRemotePoll();
            _ = SafeCheckAsync(true, false, CancellationToken.None);
        });
    }

    private void ScheduleAutoUpload(TimeSpan delay)
    {
        if (_disposed)
            return;

        RunOnUiThread(() =>
        {
            if (_disposed || !Config.Enabled)
                return;

            var now = DateTimeOffset.UtcNow;
            var deadline = CloudSyncPolicy.NextDeadline(_autoUploadDeadline, now, delay);

            // 已经排着一个更早的截止时间就不动它：作业板那 5 秒不能被设置那边的 45 秒顶掉。
            if (_autoUploadDeadline == deadline)
                return;

            // 截止时间可能刚好在这一次改动之前（前一次上传已经到期、计时器还没跑到），
            // 那就立刻再来一次；负的间隔不能交给 DispatcherTimer。
            var interval = deadline - now;
            if (interval < TimeSpan.FromMilliseconds(1))
                interval = TimeSpan.FromMilliseconds(1);

            _autoUpload ??= new DispatcherTimer();
            _autoUpload.Stop();
            _autoUpload.Tick -= OnAutoUploadTick;
            _autoUpload.Tick += OnAutoUploadTick;
            _autoUpload.Interval = interval;
            _autoUploadDeadline = deadline;
            _autoUpload.Start();
        });
    }

    private void StopAutoUpload() => RunOnUiThread(() =>
    {
        _autoUpload?.Stop();
        _autoUploadDeadline = null;
    });

    private void OnAutoUploadTick(object? sender, EventArgs e)
    {
        _autoUpload?.Stop();
        _autoUploadDeadline = null;
        _ = SafeAutoBackupAsync();
    }

    /// <summary>
    ///     起一条定时轮询：别的设备传了新备份就按策略自动拿下来，别处把云端清空了就跟着清簿记。
    ///     用户不用自己去点「刷新」。
    /// </summary>
    private void StartRemotePoll() => RunOnUiThread(() =>
    {
        if (_disposed)
            return;

        _remotePoll ??= new DispatcherTimer();
        _remotePoll.Tick -= OnRemotePollTick;
        _remotePoll.Tick += OnRemotePollTick;
        _remotePoll.Interval = RemotePollInterval;
        _remotePoll.Start();
    });

    private void StopRemotePoll() => RunOnUiThread(() => _remotePoll?.Stop());

    /// <summary>
    ///     轮询到点了：只在真的在用云同步（已登录 + 开关开着）且不忙的时候问云端，
    ///     走静默查询——失败只写日志，不占界面的忙闲、也不留红字。
    /// </summary>
    private void OnRemotePollTick(object? sender, EventArgs e)
    {
        if (_disposed || IsBusy)
            return;

        if (!Config.Enabled || !_auth.IsSignedIn)
            return;

        _ = SafeCheckAsync(true, true, CancellationToken.None);
    }

    /// <summary>防抖到期后的自动上传：只传真正变过的那部分，失败只记日志。</summary>
    private async Task SafeAutoBackupAsync()
    {
        if (_disposed || IsBusy)
            return;

        var config = Config;
        if (!config.Enabled || !_auth.IsSignedIn)
            return;

        var (boardDirty, settingsDirty) = ComputeDirty(ScopeOf(config));
        var scope = (boardDirty ? CloudSyncScope.Board : CloudSyncScope.None) |
                    (settingsDirty ? CloudSyncScope.Settings : CloudSyncScope.None);
        if (scope == CloudSyncScope.None)
            return;

        SetBusy(true);
        try
        {
            await BackupCoreAsync(scope, CancellationToken.None);
            await CheckAsync(false, true, false, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RecordError(exception);
            _logger?.LogWarning(exception, "自动上传云备份失败。");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ResetCloudState()
    {
        lock (_gate)
        {
            _usage = CloudStorageUsage.Empty;
            _remoteBoard = null;
            _remoteSettings = null;
            _remoteUpdatedAt = null;
            _remoteIsNewer = false;
            _hasCloudFiles = false;
            _remoteCleared = false;
            _lastError = null;
        }

        NotifyState();
    }

    private void SetBusy(bool value)
    {
        lock (_gate)
        {
            if (_isBusy == value)
                return;

            _isBusy = value;
        }

        NotifyState();
    }

    private void RecordError(Exception exception)
    {
        lock (_gate)
        {
            _lastError = exception.Message;
        }

        NotifyState();
    }

    private void NotifyState() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private static CloudSyncScope ScopeOf(CloudSyncSettingsConfig config)
    {
        var scope = CloudSyncScope.None;
        if (config.SyncBoard)
            scope |= CloudSyncScope.Board;
        if (config.SyncSettings)
            scope |= CloudSyncScope.Settings;
        return scope;
    }

    private static DateTimeOffset? Later(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
            return right;
        if (right is null)
            return left;
        return left.Value >= right.Value ? left : right;
    }

    private static DateTimeOffset? DirectoryLastWrite(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return null;

            DateTimeOffset? latest = null;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var written = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
                if (latest is null || written > latest.Value)
                    latest = written;
            }

            return latest;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool DirectoryHasFiles(string directory)
    {
        try
        {
            return Directory.Exists(directory) &&
                   Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>用解出来的备份替换整个目录（先清空再搬，避免留下云端已经没有的旧文件）。</summary>
    private static void ReplaceDirectory(string source, string target)
    {
        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 单个文件删不掉（被占用）不该中断整次恢复，覆盖同名文件时还会再试一次。
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                Directory.Delete(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            var folder = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            File.Move(file, destination, true);
        }
    }

    /// <summary>
    ///     搬板级文件（<c>notes.json</c> 以外的东西）：科目定义 <c>board.json</c> 之类。
    ///     <para>
    ///         作业文件由作业并集与 <c>SaveAll</c> 负责，这里一律跳过。
    ///         <paramref name="overwrite" /> 为 <c>false</c> 时两边都有的文件以目标为准——融合时本机的科目表
    ///         与手动排序是用户自己调的，不该被云端覆盖；为 <c>true</c> 时以源为准——恢复时用户点的就是
    ///         「用云端备份覆盖本机」。
    ///     </para>
    /// </summary>
    private static void CopyBoardFiles(string source, string target, bool overwrite)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (string.Equals(Path.GetFileName(relative), NoteStore.NotesFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.Combine(target, relative);
            if (!overwrite && File.Exists(destination))
                continue;

            var folder = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            File.Copy(file, destination, overwrite);
        }
    }

    private static string CreateWorkDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), ProductName, WorkDirectoryName);
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 删不掉就用已存在的目录，反正下面每个文件都是覆盖写。
        }

        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 临时目录留在 %TEMP% 里没关系，下次同步会再清一次。
        }
    }
}
