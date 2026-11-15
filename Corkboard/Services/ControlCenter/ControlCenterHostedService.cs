using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Corkboard.Core.Board;
using Corkboard.Core.ControlCenter;
using Corkboard.Core.Exam;
using Corkboard.Core.Langs.Common;
using Corkboard.Core.Models.SubConfigs.ControlCenter;
using Corkboard.Core.Services.Config;
using Corkboard.Helpers;
using Corkboard.Services.Ui;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.ControlCenter;

/// <summary>
///     集控（SECTL control center）的应用层运行时服务。
///     <para>
///         Core 的 <see cref="ControlNodeClient" /> 只负责协议、连接与凭证；<b>用户可见的副作用</b>
///         （接入 / 配对的 toast、重载配置、回传作业快照）都在这里通过 <see cref="ControlNodeClient.RegisterHandler" />
///         挂上去。这样 Core 不依赖 Avalonia，也不会在处理指令时直接碰界面。
///     </para>
///     <para>
///         客户端本身不监听配置：开关、服务器地址、设备名发生变化时由本服务重建客户端
///         （<see cref="RestartAsync" />），避免「配置改了、连接还指着旧地址」。
///         重建是串行化的；地址 / 设备名这类随输入逐字变化的项做 400ms 防抖。
///     </para>
///     <para>
///         <b>Corkboard 不做公告卡、也不做推图浮层</b>：服务端给 corkboard 平台留的
///         <c>refreshNotice</c> / <c>pushImage</c> 这两条推送类命令这里故意不注册，
///         客户端于是也不声明这两项能力，网页控制台对这台设备下发时会被服务端直接挡回。
///     </para>
/// </summary>
public sealed class ControlCenterHostedService : BackgroundService, IAsyncDisposable
{
    /// <summary>服务器地址 / 设备名变更后等这么久再重连，免得每敲一个字就重连一次。</summary>
    private static readonly TimeSpan RestartDebounce = TimeSpan.FromMilliseconds(400);

    /// <summary>作业板变化后等这么久再上传，避开一次编辑过程中反复触发。</summary>
    private static readonly TimeSpan UploadDebounce = TimeSpan.FromSeconds(8);

    /// <summary>兜底巡检间隔：板子没动也隔一阵补传一次（服务端丢了、网络刚恢复都能补上）。</summary>
    private static readonly TimeSpan UploadHeartbeatInterval = TimeSpan.FromMinutes(10);

    /// <summary>唯一注册的命令：重载配置（不弹任何界面）。</summary>
    private const string CommandReloadConfig = "reloadConfig";

    /// <summary>只读查询：把作业板上的作业压成快照回给网页控制台（老师「查看作业」）。</summary>
    private const string CommandHomeworkRead = "homework.read";

    /// <summary>
    ///     <c>homework.read</c> 回执里手写笔迹的字符预算。走 WebSocket 单帧（上限 64 KiB），
    ///     正文与元数据还要占地方，所以比备份上传（96 KiB）留得更紧。
    /// </summary>
    private const int HomeworkReadInkBudget = 32 * 1024;

    private readonly MainConfigHandler _configHandler;
    private readonly IControlNodeCredentialProvider _credentials;
    private readonly IBoardService _boardService;
    private readonly IExamService _examService;
    private readonly IToastService _toastService;
    private readonly ILogger<ControlCenterHostedService> _logger;

    /// <summary>重建客户端 / 启动客户端的串行闸门：配置变化与重启请求不可能同时动同一个客户端。</summary>
    private readonly SemaphoreSlim _restartGate = new(1, 1);

    /// <summary>保护 <see cref="_client" /> 的读写；`EnrollAsync` 这类公开方法可能在托管服务启动前被调用。</summary>
    private readonly object _clientGate = new();

    /// <summary>400ms 防抖的取消源；每次新的地址 / 设备名变更都会替换它。</summary>
    private readonly object _debounceGate = new();

    /// <summary>上传的串行闸门：防抖、连上补传、10 分钟巡检三条路不能同时传。</summary>
    private readonly SemaphoreSlim _uploadGate = new(1, 1);

    /// <summary>10 分钟兜底巡检循环的取消源（停服 / 释放时取消）。</summary>
    private readonly CancellationTokenSource _uploadLoopCts = new();

    /// <summary>保护上传相关的记账与 HTTP 客户端（<see cref="_uploadApi" />）。</summary>
    private readonly object _uploadStateGate = new();

    private CancellationTokenSource? _pendingRestart;

    /// <summary>当前订阅着的子配置对象（<c>Reload()</c> 会整个换掉它，所以要跟着换订阅）。</summary>
    private ControlCenterSettingsConfig? _watchedConfig;

    private ControlNodeClient? _client;

    /// <summary>作业备份的 HTTP 客户端：站点地址变了就换一个（地址只影响请求目标）。</summary>
    private ControlNodeEnrollmentApi? _uploadApi;

    private string _uploadApiServerUrl = string.Empty;

    /// <summary>8 秒防抖计时器；基于 <see cref="DebounceTimer" />（DispatcherTimer），只能在 UI 线程上建。</summary>
    private DebounceTimer? _uploadDebounce;

    private Task? _uploadLoop;

    /// <summary>作业自动备份的对外状态。</summary>
    private ControlHomeworkBackupState _homeworkBackup = ControlHomeworkBackupState.Initial;

    /// <summary>上一次成功上传时的内容指纹；一样就不重复传。</summary>
    private string? _lastUploadedFingerprint;

    /// <summary>上一次成功上传时的板上作业条数（指纹之外的第二个判据）。</summary>
    private int _lastUploadedNoteCount;

    /// <summary>板子变化 / 连接状态是否已经挂上（重连与停服要成对增删）。</summary>
    private bool _uploadTriggersAttached;

    /// <summary>上一次记过 warning 的错误键：同一个错误连续失败只记一次日志。</summary>
    private string? _lastLoggedBackupErrorKey;

    /// <summary>接入之后一直没备份成功时，只弹一次普通提示。</summary>
    private bool _backupFailureToasted;

    private ControlNodeLinkState _snapshot = ControlNodeLinkState.Initial;

    /// <summary>上一次看到的开关值（<c>PropertyChanged</c> 不告诉我们旧值，只能自己记）。</summary>
    private bool _lastEnabled;

    /// <summary>开关 + 地址 + 设备名的指纹，用于判断 <c>Reload()</c> 是否真的换了连接参数。</summary>
    private string _fingerprint = string.Empty;

    private bool _disposed;

    public ControlCenterHostedService(
        MainConfigHandler configHandler,
        IControlNodeCredentialProvider credentials,
        IBoardService boardService,
        IExamService examService,
        IToastService toastService,
        ILogger<ControlCenterHostedService> logger)
    {
        _configHandler = configHandler;
        _credentials = credentials;
        _boardService = boardService;
        _examService = examService;
        _toastService = toastService;
        _logger = logger;

        _configHandler.Reloaded += OnConfigReloaded;
        SubscribeControlCenter();
        SubscribeUploadTriggers();
        _fingerprint = CreateFingerprint(_configHandler.Data.ControlCenter);
    }

    /// <summary>集控长连接的当前状态（供设置页画状态条）。</summary>
    public ControlNodeLinkState Snapshot
    {
        get
        {
            lock (_clientGate)
                return _snapshot;
        }
    }

    /// <summary>状态变化通知。<b>可能来自后台线程</b>，界面订阅者要自己切回 UI 线程。</summary>
    public event EventHandler<ControlNodeLinkState>? SnapshotChanged;

    /// <summary>作业自动备份的当前状态（供设置页画「最近备份」）。</summary>
    public ControlHomeworkBackupState HomeworkBackup
    {
        get
        {
            lock (_uploadStateGate)
                return _homeworkBackup;
        }
    }

    /// <summary>作业自动备份状态变化通知。<b>可能来自后台线程</b>，界面订阅者要自己切回 UI 线程。</summary>
    public event EventHandler<ControlHomeworkBackupState>? HomeworkBackupChanged;

    /// <summary>本机是否已经有完整的设备凭证（= 已接入过集控）。</summary>
    public bool IsConfigured => _credentials.Current is { IsComplete: true };

    /// <summary>本机的设备 id（没接入时为 <see langword="null" />）。</summary>
    public string? DeviceId => _credentials.Current?.DeviceId;

    /// <summary>接入的地点 id；没有凭证时退回配置里填过的值。</summary>
    public string? SiteId
    {
        get
        {
            var credential = _credentials.Current;
            if (!string.IsNullOrWhiteSpace(credential?.SiteId))
                return credential!.SiteId;

            var configured = _configHandler.Data.ControlCenter.SiteId;
            return string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
        }
    }

    /// <summary>接入的地点名称（服务端没下发时为 <see langword="null" />）。</summary>
    public string? SiteName => _credentials.Current?.SiteName;

    /// <summary>
    ///     接入集控：第一个参数既可以是地点 ID，也可以直接粘贴网页上那一整句
    ///     （「地点ID：SC-… 配对码：…」）。失败只弹 toast，不抛异常。
    /// </summary>
    public async Task EnrollAsync(string siteIdOrSentence, string pairCode, CancellationToken ct = default)
    {
        try
        {
            var config = _configHandler.Data.ControlCenter;

            // 先把输入塞进配置：Core 的「启用后自动接入」读的就是这里；整句粘贴时它也会自己解析。
            config.SiteId = siteIdOrSentence;

            var client = EnsureClient();
            var (ok, errorKey) = await client
                .EnrollAsync(siteIdOrSentence, pairCode, config.DisplayName, ct)
                .ConfigureAwait(false);

            if (ok)
            {
                config.PairCode = string.Empty;
                _toastService.Show(Resources.Settings_Control_Enroll_Success, ToastSeverity.Success);

                // 已经启用的场合把连接跑起来（StartAsync 幂等：会话已在跑时立刻返回）。
                if (config.Enabled)
                    await client.StartAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var detail = ResolveEnrollErrorText(errorKey);
                _toastService.Show(
                    string.Format(CultureInfo.CurrentCulture, Resources.Settings_Control_Enroll_Failed, detail),
                    ToastSeverity.Error);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "集控接入异常");
            _toastService.Show(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.Settings_Control_Enroll_Failed,
                    Resources.Settings_Control_Enroll_Error_Unknown),
                ToastSeverity.Error);
        }
        finally
        {
            RaiseSnapshotChanged();
        }
    }

    /// <summary>忘记接入：清掉设备凭证，回到「未接入」。地点 id 保留（重新接入时不用重填）。</summary>
    public async Task ForgetAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsureClient().ForgetEnrollmentAsync().ConfigureAwait(false);
            _configHandler.Data.ControlCenter.PairCode = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "集控清除凭证失败");
        }
        finally
        {
            RaiseSnapshotChanged();
        }
    }

    /// <summary>读设备结构树（年级 → 班级）。失败返回空列表（Core 已兜底，不抛）。</summary>
    public Task<IReadOnlyList<ControlGradeNode>> GetTreeAsync(CancellationToken ct = default) =>
        EnsureClient().GetDeviceTreeAsync(ct);

    /// <summary>换班（<c>class.select</c>）：离线时只记住，重连后自动重占。</summary>
    public Task SelectClassAsync(string classId) => EnsureClient().SelectClassAsync(classId);

    /// <summary>
    ///     重建集控客户端：停掉旧连接、按当前配置造一个新的再启动。
    ///     配置里开关 / 地址 / 设备名变化、以及云端恢复配置后都会走到这里；重建是串行化的。
    /// </summary>
    public Task RestartAsync() => StartClientAsync(CancellationToken.None);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 先把落盘的设备凭证读进内存，这样设置页在「还没启用」时也能显示「已接入」。
            try
            {
                await _credentials.LoadAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取集控设备凭证失败，按「未接入」处理");
            }

            await StartClientAsync(stoppingToken).ConfigureAwait(false);
            StartUploadLoop();

            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常停服。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "集控后台服务异常退出");
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _disposed = true;
        UnsubscribeConfig();
        CancelPendingRestart();
        UnsubscribeUploadTriggers();
        CancelUploadLoop();
        DisposeUploadApi();

        await StopAndDisposeClientAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     异步释放：退订配置监听、取消待执行的重连、停掉并销毁客户端。
    ///     <para>
    ///         宿主优先调用这个（本类同时实现 <see cref="IAsyncDisposable" />），
    ///         避免在 UI 线程上同步等待网络相关的释放。
    ///     </para>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // 不因 _disposed 提前返回：StopAsync 与 ExecuteAsync 可能有竞态，
        // 这里再扫一次才能保证晚一步挂上的客户端也被销毁。
        _disposed = true;
        UnsubscribeConfig();
        CancelPendingRestart();
        UnsubscribeUploadTriggers();
        CancelUploadLoop();
        DisposeUploadApi();

        await StopAndDisposeClientAsync().ConfigureAwait(false);
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _disposed = true;
        UnsubscribeConfig();
        CancelPendingRestart();
        UnsubscribeUploadTriggers();
        CancelUploadLoop();
        DisposeUploadApi();

        // 同步释放路径上不等网络：先摘掉引用，销毁在后台完成（客户端自己的 DisposeAsync 不带网络等待）。
        _ = StopAndDisposeClientAsync();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>构造客户端并挂上命令处理器（重载配置 / 读取作业）与状态订阅。</summary>
    private ControlNodeClient CreateClient()
    {
        var client = new ControlNodeClient(
            _configHandler.Data.ControlCenter,
            logger: null,
            credentials: _credentials);

        client.RegisterHandler(CommandReloadConfig, HandleReloadConfigAsync);
        client.RegisterHandler(CommandHomeworkRead, HandleHomeworkReadAsync);
        client.StateChanged += OnClientStateChanged;
        return client;
    }

    /// <summary>托管服务还没跑起来（或已经停掉）时，公开方法用的兜底客户端：只保证对象存在，不主动连接。</summary>
    private ControlNodeClient EnsureClient()
    {
        lock (_clientGate)
        {
            if (_client is { } existing)
                return existing;

            var client = CreateClient();
            _client = client;
            return client;
        }
    }

    /// <summary>启动（或重建）客户端。整个过程串行，失败只记日志（Core 的启动本来就不抛）。</summary>
    private async Task StartClientAsync(CancellationToken cancellationToken)
    {
        await _restartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            ControlNodeClient? previous;
            lock (_clientGate)
            {
                previous = _client;
                _client = null;
            }

            if (previous is not null)
                await DisposeClientAsync(previous).ConfigureAwait(false);

            if (_disposed)
                return;

            var client = CreateClient();
            lock (_clientGate)
                _client = client;

            // 先摘引用再启动：启动过程中的状态事件由 OnClientStateChanged 按引用过滤，旧客户端的残留事件会被忽略。
            await client.StartAsync(cancellationToken).ConfigureAwait(false);
            SetSnapshot(client.State);
        }
        catch (OperationCanceledException)
        {
            // 停服 / 重建被取消：保持现状即可。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "集控客户端启动失败");
        }
        finally
        {
            _restartGate.Release();
        }
    }

    /// <summary>停掉并销毁当前客户端（停服、开关关掉、重建前都会用）。</summary>
    private async Task StopAndDisposeClientAsync()
    {
        ControlNodeClient? client;
        lock (_clientGate)
        {
            client = _client;
            _client = null;
        }

        if (client is null)
            return;

        try
        {
            await client.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "停止集控客户端时出错");
        }

        await DisposeClientAsync(client).ConfigureAwait(false);
    }

    private async Task DisposeClientAsync(ControlNodeClient client)
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "释放集控客户端时出错");
        }
    }

    private void OnClientStateChanged(object? sender, ControlNodeLinkState state)
    {
        lock (_clientGate)
        {
            if (!ReferenceEquals(sender, _client))
                return;
        }

        SetSnapshot(state);
    }

    /// <summary>更新快照；状态没变就不吵醒订阅者（指令处理会另外强制发一次列表刷新）。</summary>
    private void SetSnapshot(ControlNodeLinkState state)
    {
        lock (_clientGate)
        {
            if (_snapshot == state)
                return;
            _snapshot = state;
        }

        RaiseSnapshotChanged();
    }

    /// <summary>强制发一次状态变化：接入 / 忘记接入之后 <see cref="IsConfigured" /> 等派生属性也变了。</summary>
    private void RaiseSnapshotChanged()
    {
        EventHandler<ControlNodeLinkState>? handler;
        ControlNodeLinkState state;
        lock (_clientGate)
        {
            handler = SnapshotChanged;
            state = _snapshot;
        }

        if (handler is null)
            return;

        try
        {
            handler(this, state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "集控状态订阅者抛出了异常");
        }
    }

    // ------------------------------------------------------------------
    // 命令处理器（在后台线程上跑；返回值会被序列化进 cmd.result.data）
    // ------------------------------------------------------------------

    /// <summary>
    ///     <c>reloadConfig</c>：让应用重新读一次 <c>settings.json</c>。
    ///     <para>
    ///         <b>绝不能在这里同步调 <c>Reload()</c></b>：重载会把配置对象整个换掉，
    ///         连接参数随之变化，回执帧就可能在连接重建的过程中发不出去。
    ///         所以先返回成功，等回执走完之后再延迟重载。
    ///     </para>
    /// </summary>
    private Task<object?> HandleReloadConfigAsync(JsonObject args, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200).ConfigureAwait(false);
                if (_disposed)
                    return;

                _configHandler.Reload();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "集控指令触发的配置重载失败");
            }
        });

        return Task.FromResult<object?>(new { reloaded = true });
    }

    /// <summary>
    ///     <c>homework.read</c>：把当前作业板上的作业压成一份只读快照，作为回执数据回给网页控制台。
    ///     <para>
    ///         作业集合会随用户编辑在 UI 线程上变动，而处理器跑在后台线程——所以整份拷贝
    ///         （<c>ToList</c>）必须在 UI 线程做完再交给纯逻辑的
    ///         <see cref="ControlHomeworkSnapshot.Build" />，否则可能拿到半改的集合。
    ///     </para>
    ///     <para>
    ///         这条命令只是「看」：不写作业、不改配置、不弹界面，所以也算不上需要注意的操作。
    ///     </para>
    /// </summary>
    /// <summary>备份快照允许带的笔迹总预算（比 <c>homework.read</c> 大，但服务端仍限 256 KiB）。</summary>
    private const int MaxTotalInkLengthForBackup = ControlHomeworkSnapshot.MaxTotalInkLength;

    /// <summary>
    ///     在 UI 线程上拷一份「作业 + 考试看板」并压成快照：作业集合与考试集合都只在 UI 线程变，
    ///     后台线程直接读可能拿到半改的集合。指纹把作业内容与考试日程一起算进去——考试改了也要重传。
    /// </summary>
    private async Task<(JsonObject? Snapshot, string Fingerprint, int NoteCount)> BuildSnapshotAsync(
        int inkBudget,
        int days,
        DateTimeOffset now)
    {
        var config = _configHandler.Data.ControlCenter;
        var examEnabled = config.ExamSyncEnabled;
        var examRecentCount = config.ExamSyncRecentCount;

        return await Dispatcher.UIThread
            .InvokeAsync<(JsonObject? Snapshot, string Fingerprint, int NoteCount)>(() =>
            {
                var notes = _boardService.Notes.ToList();
                var examBoard = examEnabled
                    ? ControlExamBoardSnapshot.Build(
                        _examService.ActiveBoard,
                        _examService.Exams.ToList(),
                        now,
                        examRecentCount)
                    : null;

                var built = ControlHomeworkSnapshot.Build(
                    notes,
                    _boardService.Subjects.ToList(),
                    _boardService.SubjectOrder.ToList(),
                    now,
                    inkBudget,
                    days,
                    examBoard);

                var homeworkFingerprint = ControlHomeworkBackup.CreateContentFingerprint(notes);
                var examSignature = examBoard is null ? string.Empty : examBoard.ToJsonString();
                var fingerprint = CombineFingerprint(homeworkFingerprint, examSignature);

                return (built, fingerprint, notes.Count(note => note.IsOnBoard));
            })
            .GetTask()
            .ConfigureAwait(false);
    }

    /// <summary>把「作业内容指纹」与「考试日程签名」合成一个指纹（都变了才算变）。</summary>
    private static string CombineFingerprint(string contentFingerprint, string examSignature)
    {
        if (string.IsNullOrEmpty(examSignature))
            return contentFingerprint;

        var bytes = Encoding.UTF8.GetBytes(string.Concat(contentFingerprint, "\u001f", examSignature));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>
    ///     考完的考试按配置自动清掉（宽限 <see cref="ControlCenterSettingsConfig.ExamAutoCleanGraceDays" /> 天，
    ///     免得「刚考完还在看排名/补录」时被抢走）。
    /// </summary>
    private void CleanFinishedExams()
    {
        if (_disposed)
            return;

        var config = _configHandler.Data.ControlCenter;
        if (!config.ExamAutoCleanFinished)
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(CleanFinishedExams);
            return;
        }

        var ids = ControlExamBoardSnapshot.SelectFinishedForRemoval(
            _examService.Exams,
            DateTimeOffset.Now,
            ControlCenterSettingsConfig.ExamAutoCleanGraceDays);

        if (ids.Count == 0)
            return;

        foreach (var id in ids)
            _examService.Remove(id);

        _logger.LogInformation("集控：已自动清理 {Count} 场考完的考试", ids.Count);
        ScheduleUpload();
    }

    private async Task<object?> HandleHomeworkReadAsync(JsonObject args, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;

        // homework.read 是「看一次」：只带当天，免得一次性回太多（WS 单帧 64 KiB）。
        var built = await BuildSnapshotAsync(HomeworkReadInkBudget, days: 1, now)
            .ConfigureAwait(false);

        return built.Snapshot;
    }

    // ------------------------------------------------------------------
    // 作业自动备份：板子一变（防抖 8s）→ 传一份只读快照；连上补传一次；10 分钟兜底
    // ------------------------------------------------------------------

    /// <summary>订阅「板子变了」与「连上了」两个触发点；停服 / 释放前成对退订。</summary>
    private void SubscribeUploadTriggers()
    {
        if (_uploadTriggersAttached)
            return;

        _uploadTriggersAttached = true;
        _boardService.Changed += OnBoardChanged;
        _examService.Changed += OnBoardChanged;
        SnapshotChanged += OnSnapshotChangedForUpload;
    }

    private void UnsubscribeUploadTriggers()
    {
        if (!_uploadTriggersAttached)
            return;

        _uploadTriggersAttached = false;
        _boardService.Changed -= OnBoardChanged;
        _examService.Changed -= OnBoardChanged;
        SnapshotChanged -= OnSnapshotChangedForUpload;

        // 待发的那一次直接扔掉（服务要停了 / 已经断线，再传没有意义）。
        _uploadDebounce?.Cancel();
    }

    private void OnBoardChanged(object? sender, EventArgs e) => ScheduleUpload();

    /// <summary>连上集控（或换班）后补传一次：断线期间攒下的变化不能等 10 分钟巡检。</summary>
    private void OnSnapshotChangedForUpload(object? sender, ControlNodeLinkState state)
    {
        if (state.IsConnected)
            ScheduleUpload();
    }

    /// <summary>安排一次上传（防抖 8 秒）：连续的编辑只会传停手后的最后一次。</summary>
    private void ScheduleUpload()
    {
        if (_disposed)
            return;

        // DebounceTimer 基于 DispatcherTimer，必须在 UI 线程上建 / 排；板子的 Changed 本来就在 UI 线程，
        // 快照回调等后台来源则先切过去。
        if (Dispatcher.UIThread.CheckAccess())
            ScheduleUploadCore();
        else
            Dispatcher.UIThread.Post(ScheduleUploadCore);
    }

    private void ScheduleUploadCore()
    {
        if (_disposed)
            return;

        _uploadDebounce ??= new DebounceTimer(UploadDebounce);
        _uploadDebounce.Schedule(() => _ = UploadHomeworkSnapshotAsync());
    }

    /// <summary>启动 10 分钟兜底巡检（只跑一次；<see cref="ExecuteAsync" /> 里调）。</summary>
    private void StartUploadLoop()
    {
        lock (_uploadStateGate)
        {
            if (_uploadLoop is not null || _disposed)
                return;

            _uploadLoop = RunUploadLoopAsync(_uploadLoopCts.Token);
        }
    }

    private async Task RunUploadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 先等一个周期：刚启动时的补传由「连上集控」那条路直接触发，不用抢在启动瞬间传。
            using var timer = new PeriodicTimer(UploadHeartbeatInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                CleanFinishedExams();
                _ = UploadHomeworkSnapshotAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停服。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "作业自动备份的兜底巡检循环异常退出");
        }
    }

    private void CancelUploadLoop()
    {
        try
        {
            _uploadLoopCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经取消过了。
        }
    }

    private bool HasClient()
    {
        lock (_clientGate)
            return _client is not null;
    }

    /// <summary>取（或按当前站点地址换一个）作业备份用的 HTTP 客户端。</summary>
    private ControlNodeEnrollmentApi GetUploadApi(string serverUrl)
    {
        lock (_uploadStateGate)
        {
            if (_uploadApi is not null && string.Equals(_uploadApiServerUrl, serverUrl, StringComparison.Ordinal))
                return _uploadApi;

            _uploadApi?.Dispose();
            _uploadApiServerUrl = serverUrl;
            _uploadApi = new ControlNodeEnrollmentApi(new ControlNodeConnectOptions { ServerUrl = serverUrl });
            return _uploadApi;
        }
    }

    private void DisposeUploadApi()
    {
        lock (_uploadStateGate)
        {
            _uploadApi?.Dispose();
            _uploadApi = null;
        }
    }

    /// <summary>
    ///     传一份只读作业快照。
    ///     <para>
    ///         内容与上次成功传的那份一样（指纹 + 条数都没变）就跳过：防抖与 10 分钟巡检会频繁走到这里，
    ///         每次都传一份一模一样的快照只会白占服务端配额。
    ///     </para>
    ///     <para>
    ///         失败只记一次 warning、不弹 toast（后台行为，打断用户写作业更糟）；
    ///         只有「接入之后一直没备份成功过」的那一段才弹一条普通级别提示。
    ///     </para>
    /// </summary>
    private async Task UploadHomeworkSnapshotAsync()
    {
        if (_disposed)
            return;

        if (!ControlHomeworkBackup.ShouldUpload(
                _configHandler.Data.ControlCenter,
                _credentials.Current,
                Snapshot,
                HasClient()))
            return;

        await _uploadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            // 等闸门期间状态可能已经变了（刚断线 / 关掉了备份），进来后再判一次。
            var config = _configHandler.Data.ControlCenter;
            var credential = _credentials.Current;
            if (!ControlHomeworkBackup.ShouldUpload(config, credential, Snapshot, HasClient()))
                return;

            // 作业集合会随用户编辑在 UI 线程上变动，而这里是后台线程——拷贝、算指纹、拼快照都在 UI 线程做完。
            var now = DateTimeOffset.Now;
            var days = config.HomeworkBackupDays;
            var built = await BuildSnapshotAsync(MaxTotalInkLengthForBackup, days, now)
                .ConfigureAwait(false);
            var snapshot = built.Snapshot;
            var fingerprint = built.Fingerprint;
            var noteCount = built.NoteCount;

            if (snapshot is null)
                return;

            lock (_uploadStateGate)
            {
                // 上次成功传的就是这一份，别重复传。
                if (_lastUploadedFingerprint == fingerprint && _lastUploadedNoteCount == noteCount)
                    return;
            }

            var classId = string.IsNullOrWhiteSpace(config.SelectedClassId) ? null : config.SelectedClassId;
            var result = await GetUploadApi(config.ServerUrl)
                .UploadHomeworkSnapshotAsync(credential!, snapshot, classId)
                .ConfigureAwait(false);

            if (result.Ok)
            {
                lock (_uploadStateGate)
                {
                    _lastUploadedFingerprint = fingerprint;
                    _lastUploadedNoteCount = noteCount;
                    _homeworkBackup = new ControlHomeworkBackupState(
                        DateTimeOffset.Now,
                        _homeworkBackup.UploadedCount + 1);
                }

                _lastLoggedBackupErrorKey = null;
                _backupFailureToasted = false;
                RaiseHomeworkBackupChanged();
                return;
            }

            RecordBackupFailure(result.ErrorKey, result.Message);
        }
        finally
        {
            _uploadGate.Release();
        }
    }

    /// <summary>记一次失败：同一个错误连续出现只记一次日志，接入后从没成功过时弹一次提示。</summary>
    private void RecordBackupFailure(string? errorKey, string? message)
    {
        var key = string.IsNullOrWhiteSpace(errorKey) ? ControlApiErrorKeys.Unknown : errorKey;

        bool log;
        bool toast;
        lock (_uploadStateGate)
        {
            log = !string.Equals(_lastLoggedBackupErrorKey, key, StringComparison.Ordinal);
            _lastLoggedBackupErrorKey = key;

            toast = !_backupFailureToasted && _homeworkBackup.LastSuccessAt is null;
            if (toast)
                _backupFailureToasted = true;

            _homeworkBackup = _homeworkBackup with { LastErrorKey = key, LastErrorText = message };
        }

        if (log)
            _logger.LogWarning("作业自动备份失败：{ErrorKey} {Message}", key, message);

        RaiseHomeworkBackupChanged();

        if (toast)
            _toastService.Show(Resources.Settings_Control_HomeworkBackup_FailedToast, ToastSeverity.Info);
    }

    private void RaiseHomeworkBackupChanged()
    {
        EventHandler<ControlHomeworkBackupState>? handler;
        ControlHomeworkBackupState state;
        lock (_uploadStateGate)
        {
            handler = HomeworkBackupChanged;
            state = _homeworkBackup;
        }

        if (handler is null)
            return;

        try
        {
            handler(this, state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "作业备份状态订阅者抛出了异常");
        }
    }

    // ------------------------------------------------------------------
    // 配置监听：开关 / 地址 / 设备名变化 → 停止或重建客户端
    // ------------------------------------------------------------------

    /// <summary>订阅当前子配置对象（<c>Data.ControlCenter</c> 可能被 <c>Reload()</c> 整个换掉）。</summary>
    private void SubscribeControlCenter()
    {
        var config = _configHandler.Data.ControlCenter;
        if (ReferenceEquals(config, _watchedConfig))
            return;

        if (_watchedConfig is not null)
            _watchedConfig.PropertyChanged -= OnControlCenterPropertyChanged;

        _watchedConfig = config;
        _watchedConfig.PropertyChanged += OnControlCenterPropertyChanged;
        _lastEnabled = config.Enabled;
    }

    private void UnsubscribeConfig()
    {
        _configHandler.Reloaded -= OnConfigReloaded;

        if (_watchedConfig is null)
            return;

        _watchedConfig.PropertyChanged -= OnControlCenterPropertyChanged;
        _watchedConfig = null;
    }

    private void OnControlCenterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
            return;

        var name = e.PropertyName;
        if (string.IsNullOrEmpty(name))
            return;

        var config = _configHandler.Data.ControlCenter;
        switch (name)
        {
            case nameof(ControlCenterSettingsConfig.Enabled):
            {
                var enabled = config.Enabled;
                var wasEnabled = _lastEnabled;
                _lastEnabled = enabled;
                _fingerprint = CreateFingerprint(config);

                if (enabled && !wasEnabled)
                {
                    ScheduleRestart(TimeSpan.Zero);
                }
                else if (!enabled && wasEnabled)
                {
                    // 关掉只停连接、不再重连；等重新打开时再整体重建。
                    _uploadDebounce?.Cancel();
                    _ = StopForDisabledAsync();
                }

                break;
            }
            case nameof(ControlCenterSettingsConfig.HomeworkBackupEnabled):
                // 刚打开就传一次（板子可能已经改过、而上次成功之后又断了一段时间）。
                if (config.HomeworkBackupEnabled)
                    ScheduleUpload();
                else
                    _uploadDebounce?.Cancel();
                break;
            case nameof(ControlCenterSettingsConfig.ServerUrl):
            case nameof(ControlCenterSettingsConfig.DisplayName):
                _fingerprint = CreateFingerprint(config);
                ScheduleRestart(RestartDebounce);
                break;
        }
    }

    /// <summary><c>Reload()</c> 会把子配置对象整个换掉：重订阅，并按「连接参数是否变了」决定要不要重建。</summary>
    private void OnConfigReloaded(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        var previous = _fingerprint;
        SubscribeControlCenter();

        var config = _configHandler.Data.ControlCenter;
        _fingerprint = CreateFingerprint(config);

        if (string.Equals(previous, _fingerprint, StringComparison.Ordinal))
            return;

        _logger.LogInformation("集控配置已重载，连接参数有变化，准备重建客户端");
        ScheduleRestart(TimeSpan.Zero);
    }

    /// <summary>关掉集控：停掉连接并把状态显式落到 <c>Disabled</c>（客户端自己不会再重连）。</summary>
    private async Task StopForDisabledAsync()
    {
        try
        {
            ControlNodeClient? client;
            lock (_clientGate)
                client = _client;

            if (client is not null)
                await client.StopAsync().ConfigureAwait(false);

            SetSnapshot(new ControlNodeLinkState(ControlNodeLinkStatus.Disabled, ControlDetailKeys.Disabled));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "关闭集控连接失败");
        }
    }

    /// <summary>安排一次重建；<paramref name="delay" /> 为防抖等待，新的变更会取消掉旧的等待。</summary>
    private void ScheduleRestart(TimeSpan delay)
    {
        if (_disposed)
            return;

        CancellationTokenSource cts;
        lock (_debounceGate)
        {
            _pendingRestart?.Cancel();
            _pendingRestart?.Dispose();
            cts = new CancellationTokenSource();
            _pendingRestart = cts;
        }

        _ = RunScheduledRestartAsync(cts, delay);
    }

    private async Task RunScheduledRestartAsync(CancellationTokenSource cts, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);

            cts.Token.ThrowIfCancellationRequested();
            await RestartAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 被更新的一次变更取代了。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "集控客户端重建失败");
        }
        finally
        {
            lock (_debounceGate)
            {
                if (ReferenceEquals(_pendingRestart, cts))
                    _pendingRestart = null;
            }

            cts.Dispose();
        }
    }

    private void CancelPendingRestart()
    {
        CancellationTokenSource? pending;
        lock (_debounceGate)
        {
            pending = _pendingRestart;
            _pendingRestart = null;
        }

        if (pending is null)
            return;

        try
        {
            pending.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经退出了，忽略。
        }
    }

    /// <summary>把 Core 给的错误键翻成文案；键不认识时回退「稍后重试」。</summary>
    private static string ResolveEnrollErrorText(string? errorKey)
    {
        var fallback = Resources.Settings_Control_Enroll_Error_Unknown;
        if (string.IsNullOrWhiteSpace(errorKey))
            return fallback;

        try
        {
            var text = Resources.ResourceManager.GetString(errorKey, Resources.Culture);
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or MissingManifestResourceException)
        {
            return fallback;
        }
    }

    private static string CreateFingerprint(ControlCenterSettingsConfig config) =>
        string.Concat(
            config.Enabled ? "1" : "0",
            "\u001f",
            config.ServerUrl,
            "\u001f",
            config.DisplayName);
}