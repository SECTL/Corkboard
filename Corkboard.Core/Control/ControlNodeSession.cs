using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     集控长连接的会话层：握手、心跳自检、指数退避重连、期望状态去重、指令路由与回执。
///     <para>
///         刻意不依赖 Avalonia / 配置 / 数据根：只对接
///         <see cref="IControlNodeTransport" />、<see cref="IControlNodeCredentialProvider" />、
///         <see cref="IControlNodeStateStore" /> 三个口，所以单测可以用假传输层把整条状态机跑一遍。
///     </para>
///     <para>
///         线程模型：一个后台循环负责「连 → 握手 → 读帧 → 断了退避重连」；
///         指令处理在独立任务里跑（<b>不在 UI 线程上</b>，处理器自己保证线程安全）；
///         所有状态变更都经 <see cref="_gate" /> 收口后再抛 <see cref="StateChanged" />。
///     </para>
/// </summary>
public sealed class ControlNodeSession : IAsyncDisposable
{
    /// <summary>没有凭证时的轮询间隔（用户可能刚在设置页里填完码）。</summary>
    private static readonly TimeSpan UnpairedPollInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>服务端用这个关闭码表示「协议太旧」，收到就别再重试了。</summary>
    private const int ProtocolTooOldCloseCode = 4006;

    private readonly IControlNodeCredentialProvider _credentials;
    private readonly IControlNodeStateStore _stateStore;
    private readonly IControlNodeTransportFactory _transportFactory;
    private readonly ILogger? _logger;
    private readonly ControlCommandLedger _ledger;
    private readonly ConcurrentDictionary<string, Func<ControlCommandInvocation, CancellationToken, Task<object?>>>
        _handlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<ControlCommandOutcome>> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ControlFrame>> _pending =
        new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ControlNodeConnectOptions _options;

    private long _appliedRevision;
    private int _commandTimeoutMs;
    private string? _desiredClassId;
    private bool _drawLocked = true;
    private TaskCompletionSource<bool>? _firstOutcome;
    private int _heartbeatSeconds = ControlV4Protocol.DefaultHeartbeatSeconds;
    private int _offlineAfterSeconds = ControlV4Protocol.DefaultOfflineAfterSeconds;
    private CancellationTokenSource? _roundCts;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private long _sequence;
    private ControlNodeLinkState _state;
    private IControlNodeTransport? _transport;

    /// <summary>
    ///     建一个会话。
    /// </summary>
    /// <param name="options">连接参数。</param>
    /// <param name="credentials">凭证读写（默认落数据根下的加密文件）。</param>
    /// <param name="stateStore">状态落盘（默认 <c>node-state.json</c>）。</param>
    /// <param name="transportFactory">长连接工厂（默认 WebSocket）。</param>
    /// <param name="logger">可选日志。</param>
    /// <param name="deviceName">设备名（网页上展示）；<c>null</c> 用主机名。</param>
    /// <param name="ledger">指令幂等表（默认新建一张）。</param>
    public ControlNodeSession(
        ControlNodeConnectOptions? options = null,
        IControlNodeCredentialProvider? credentials = null,
        IControlNodeStateStore? stateStore = null,
        IControlNodeTransportFactory? transportFactory = null,
        ILogger? logger = null,
        string? deviceName = null,
        ControlCommandLedger? ledger = null)
    {
        _options = options ?? new ControlNodeConnectOptions();
        _credentials = credentials ?? new NodeEnrollmentStore();
        _stateStore = stateStore ?? new FileControlNodeStateStore();
        _transportFactory = transportFactory ?? new WebSocketControlNodeTransportFactory(_options);
        _logger = logger;
        DeviceName = deviceName;
        _ledger = ledger ?? new ControlCommandLedger();
        _commandTimeoutMs = _options.FallbackCommandTimeoutMs;

        var stored = _stateStore.Load();
        _appliedRevision = Math.Max(0, stored.AppliedStateRevision);
        _state = ControlNodeLinkState.Initial with { SelectedClassId = stored.SelectedClassId };
    }

    /// <summary>当前状态快照（不会返回 <c>null</c>）。</summary>
    public ControlNodeLinkState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    /// <summary>状态变化。回调里抛异常只会被记 debug，不影响连接。</summary>
    public event EventHandler<ControlNodeLinkState>? StateChanged;

    /// <summary>已应用的期望状态 revision（重连 hello 要上报它）。</summary>
    public long AppliedRevision => Interlocked.Read(ref _appliedRevision);

    /// <summary>本机当前认为占着的班（没占是 <c>null</c>）。</summary>
    public string? SelectedClassId => State.SelectedClassId;

    /// <summary>服务端最近一次下发的期望班级（协议里这是「期望」，不是「占用」）。</summary>
    public string? DesiredClassId
    {
        get
        {
            lock (_gate)
                return _desiredClassId;
        }
    }

    /// <summary>服务端最近一次下发的抽取锁；缺字段时按「禁止抽取」处理（fail-closed）。</summary>
    public bool DrawLocked
    {
        get
        {
            lock (_gate)
                return _drawLocked;
        }
    }

    /// <summary>连接是否活着（被顶掉但连接还没断也算）。</summary>
    public bool IsConnected => State.IsConnected;

    /// <summary>会话是否已经启动（还在跑）。</summary>
    public bool IsStarted
    {
        get
        {
            lock (_gate)
                return _runTask is not null;
        }
    }

    /// <summary>
    ///     本机是否允许远程控制（设置里的开关 + 平台能力）。关掉时所有指令回
    ///     <c>local_remote_disabled</c>，不执行也不缓存结果。
    /// </summary>
    public bool IsRemoteControlAllowed { get; set; } = true;

    /// <summary>握手时声明的能力（由客户端按「已注册处理器 + 状态能力」算好传进来）。</summary>
    public IReadOnlyList<string> DeclaredCapabilities { get; set; } = ControlV4Protocol.StateCapabilities;

    /// <summary>设备名（网页上显示的名字）。</summary>
    public string? DeviceName { get; set; }

    /// <summary>服务端下发的单条指令超时（毫秒）。</summary>
    public int CommandTimeoutMs => _commandTimeoutMs;

    /// <summary>服务端下发的心跳间隔（秒）。</summary>
    public int HeartbeatSeconds => _heartbeatSeconds;

    /// <summary>服务端下发的离线判定阈值（秒）。</summary>
    public int OfflineAfterSeconds => _offlineAfterSeconds;

    /// <summary>已注册的指令名（客户端据此推导 <c>DeclaredCapabilities</c>）。</summary>
    public IReadOnlyList<string> RegisteredCommands
    {
        get
        {
            var names = new List<string>(_handlers.Keys);
            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }

    /// <summary>
    ///     注册一条指令的处理器。处理器返回的对象会被序列化成 <c>cmd.result.payload.data</c>；
    ///     返回 <c>null</c> 表示成功但没有数据。
    /// </summary>
    public void RegisterHandler(
        string command,
        Func<ControlCommandInvocation, CancellationToken, Task<object?>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[command] = handler;
    }

    /// <summary>
    ///     启动会话。<b>不抛异常</b>：没凭证 / 连不上 / 握手被拒都返回 <c>false</c>，
    ///     后台仍会按退避继续重连（除非进了终态）。
    /// </summary>
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_runTask is not null)
                return IsConnected || State.Status is ControlNodeLinkStatus.Connecting;

            var runCts = new CancellationTokenSource();
            _runCts = runCts;
            var firstOutcome = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _firstOutcome = firstOutcome;
            _runTask = Task.Run(() => RunAsync(runCts.Token), CancellationToken.None);

            // 首次连接最多等「建连 + 握手」两段超时；再等下去界面就卡住了。后台会继续重试。
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitCts.CancelAfter(_options.ConnectTimeout + _options.HandshakeTimeout);
            try
            {
                var winner = await Task.WhenAny(firstOutcome.Task, Task.Delay(Timeout.InfiniteTimeSpan, waitCts.Token))
                    .ConfigureAwait(false);
                return winner == firstOutcome.Task && await firstOutcome.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>停止会话（幂等）。终态 <see cref="ControlNodeLinkStatus.Revoked" /> 不会被覆盖成 Stopped。</summary>
    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var runCts = _runCts;
            var runTask = _runTask;
            _runCts = null;
            _runTask = null;

            if (runCts is null)
            {
                SetStopped();
                return;
            }

            runCts.Cancel();
            if (runTask is not null)
            {
                try
                {
                    await runTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 预期内。
                }
            }

            runCts.Dispose();
            SetStopped();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    ///     向服务端发一个 <c>req</c> 并等对应的 <c>res</c>。没连上时抛
    ///     <see cref="ControlNodeNotConnectedException" />，超时抛 <see cref="ControlNodeRequestException" />。
    /// </summary>
    public async Task<ControlFrame> RequestAsync(
        string topic,
        JsonObject? payload = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var transport = _transport;
        if (transport is null || !IsConnected)
            throw new ControlNodeNotConnectedException();

        var id = NextRequestId();
        var completion = new TaskCompletionSource<ControlFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            await SendFrameAsync(transport, ControlFrame.Request(id, topic, payload), cancellationToken)
                .ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout ?? _options.RequestTimeout);
            using var registration = timeoutCts.Token.Register(
                () => completion.TrySetException(new ControlNodeRequestException("timeout", $"topic={topic}")));
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>占班（上课）。离线时只记住「想占哪个班」，重连后自动重占。</summary>
    public async Task SelectClassAsync(string classId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classId);
        var normalized = classId.Trim();

        // 先记下来：任务就是「下次连上之后要占这个班」，所以离线也要落盘。
        UpdateState(state => state with { SelectedClassId = normalized }, persist: true);
        if (!IsConnected)
            return;

        await SendClassRequestAsync(ControlTopics.ClassSelect, normalized, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>释放班（下课）。离线时只把本地记录清掉。</summary>
    public async Task ReleaseClassAsync(CancellationToken cancellationToken = default)
    {
        UpdateState(state => state with { SelectedClassId = null }, persist: true);
        if (!IsConnected)
            return;

        await SendClassRequestAsync(ControlTopics.ClassRelease, null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleLock.Dispose();
        _ledger.Clear();
    }

    /// <summary>主循环：连 → 握手 → 读帧 → 断开退避重连。</summary>
    private async Task RunAsync(CancellationToken runToken)
    {
        var attempt = 0;
        try
        {
            while (!runToken.IsCancellationRequested)
            {
                ControlNodeCredential? credential;
                try
                {
                    credential = await _credentials.LoadAsync(runToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.LogDebug(ex, "集控凭证读取失败，按未接入处理");
                    credential = null;
                }

                if (credential is null || !credential.IsComplete)
                {
                    UpdateState(state => state with
                    {
                        Status = ControlNodeLinkStatus.Unpaired,
                        DetailKey = ControlDetailKeys.Unpaired,
                        RetryDelay = null
                    });
                    _firstOutcome?.TrySetResult(false);
                    await DelayAsync(UnpairedPollInterval, runToken).ConfigureAwait(false);
                    continue;
                }

                UpdateState(state => state with
                {
                    Status = ControlNodeLinkStatus.Connecting,
                    DetailKey = ControlDetailKeys.Connecting,
                    RetryDelay = null
                });

                IControlNodeTransport? transport = null;
                Task? heartbeatTask = null;
                var roundCts = CancellationTokenSource.CreateLinkedTokenSource(runToken);
                _roundCts = roundCts;
                try
                {
                    transport = await _transportFactory
                        .ConnectAsync(_options.ResolveWebSocketEndpoint(), roundCts.Token)
                        .ConfigureAwait(false);
                    _transport = transport;

                    await HandshakeAsync(transport, credential, roundCts.Token).ConfigureAwait(false);

                    attempt = 0;
                    _firstOutcome?.TrySetResult(true);
                    UpdateState(state => state with
                    {
                        Status = ControlNodeLinkStatus.Online,
                        DetailKey = ControlDetailKeys.Online,
                        RetryDelay = null
                    });

                    // 重连后自动重占上次的班（A4：断线重连不改变班级归属）。
                    if (_options.RestoreSelectedClass && !string.IsNullOrWhiteSpace(State.SelectedClassId))
                        await TryRestoreClassAsync(State.SelectedClassId!, roundCts.Token).ConfigureAwait(false);

                    heartbeatTask = HeartbeatLoopAsync(roundCts, roundCts.Token);
                    await ReadLoopAsync(transport, roundCts.Token).ConfigureAwait(false);
                }
                catch (ControlNodeTerminalException ex)
                {
                    _logger?.LogWarning("集控连接进入终态，不再重连：{Code}", ex.Code);
                    _firstOutcome?.TrySetResult(false);
                    return;
                }
                catch (OperationCanceledException) when (runToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "集控连接中断，准备重连");
                }
                finally
                {
                    _roundCts = null;
                    _transport = null;
                    roundCts.Cancel();
                    if (heartbeatTask is not null)
                    {
                        try
                        {
                            await heartbeatTask.ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is OperationCanceledException or IOException)
                        {
                            // 收尾期的取消/断线不值得记日志。
                        }
                    }

                    FailPending(new ControlNodeNotConnectedException());
                    if (transport is not null)
                    {
                        try
                        {
                            await transport.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(ex, "集控长连接关闭时出错");
                        }
                    }

                    roundCts.Dispose();
                }

                if (runToken.IsCancellationRequested)
                    return;

                var status = State.Status;
                if (status is ControlNodeLinkStatus.Revoked or ControlNodeLinkStatus.Stopped)
                    return;

                var delay = _options.BackoffFor(attempt);
                attempt++;
                UpdateState(state => state with
                {
                    Status = ControlNodeLinkStatus.Connecting,
                    DetailKey = ControlDetailKeys.Retrying,
                    RetryDelay = delay
                });
                await DelayAsync(delay, runToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _firstOutcome?.TrySetResult(false);
        }
    }

    /// <summary>发 hello 并等回包；终态码直接抛出 <see cref="ControlNodeTerminalException" />。</summary>
    private async Task HandshakeAsync(
        IControlNodeTransport transport,
        ControlNodeCredential credential,
        CancellationToken cancellationToken)
    {
        var hello = new ControlHelloPayload
        {
            Protocol = ControlV4Protocol.Version,
            Platform = _options.Platform,
            Version = _options.Version,
            DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? Environment.MachineName : DeviceName,
            DeviceId = credential.DeviceId,
            Secret = credential.Secret,
            Capabilities = DeclaredCapabilities,
            AppliedStateRevision = AppliedRevision
        };

        var id = NextRequestId();
        var payload = JsonSerializer.SerializeToNode(hello, ControlV4Protocol.JsonOptions) as JsonObject
                      ?? new JsonObject();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.HandshakeTimeout);

        await SendFrameAsync(transport, ControlFrame.Request(id, ControlTopics.Hello, payload), timeoutCts.Token)
            .ConfigureAwait(false);

        while (true)
        {
            var json = await transport.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
            if (json is null)
                throw new IOException("握手期间连接被服务端关闭");

            var frame = ControlV4Protocol.TryParse(json);
            if (frame is null)
                continue;

            if (frame.Type == ControlFrameTypes.Event)
            {
                // 握手回包前理论上不会推事件；真收到了也不丢（desired_state 的 revision 会自己纠偏）。
                HandleEvent(frame, timeoutCts.Token);
                continue;
            }

            if (frame.Type != ControlFrameTypes.Response || !string.Equals(frame.Id, id, StringComparison.Ordinal))
                continue;

            if (frame.Ok != true)
            {
                var code = string.IsNullOrWhiteSpace(frame.Code) ? ControlErrorCodes.Failed : frame.Code;
                await ThrowIfTerminalAsync(code).ConfigureAwait(false);
                throw new ControlNodeRequestException(code, frame.Message);
            }

            var reply = frame.Payload?.Deserialize<ControlHelloReply>(ControlV4Protocol.JsonOptions);
            ApplyHelloReply(reply);
            return;
        }
    }

    private void ApplyHelloReply(ControlHelloReply? reply)
    {
        if (reply is null)
            return;

        _commandTimeoutMs = reply.Limits?.CommandTimeoutMs > 0
            ? reply.Limits.CommandTimeoutMs
            : _options.FallbackCommandTimeoutMs;
        if (reply.HeartbeatSeconds > 0)
            _heartbeatSeconds = reply.HeartbeatSeconds;
        if (reply.OfflineAfterSeconds > 0)
            _offlineAfterSeconds = reply.OfflineAfterSeconds;

        _logger?.LogDebug(
            "集控握手成功：device={DeviceId} site={SiteId} timeout={Timeout}ms heartbeat={Heartbeat}s",
            reply.Device?.Id,
            reply.Device?.SiteId,
            _commandTimeoutMs,
            _heartbeatSeconds);
    }

    /// <summary>握手被拒时判断是不是终态；是就清凭证 + 置终态并抛出「别再重连」。</summary>
    private async Task ThrowIfTerminalAsync(string code)
    {
        switch (code)
        {
            case ControlErrorCodes.BadSecret or ControlErrorCodes.DeviceRevoked or ControlErrorCodes.NotAuthed:
                // 凭证已经作废：留着它只会让退避重连一直撞同一面墙。
                await _credentials.ClearAsync().ConfigureAwait(false);
                UpdateState(state => state with
                {
                    Status = ControlNodeLinkStatus.Revoked,
                    DetailKey = ControlDetailKeys.Revoked,
                    RetryDelay = null,
                    SelectedClassId = null
                });
                throw new ControlNodeTerminalException(code);

            case ControlErrorCodes.ProtocolTooOld:
                // 协议太旧重连也没用；但凭证是好的（升级客户端后还要用），不要删。
                UpdateState(state => state with
                {
                    Status = ControlNodeLinkStatus.Revoked,
                    DetailKey = ControlDetailKeys.ProtocolTooOld,
                    RetryDelay = null
                });
                throw new ControlNodeTerminalException(code);

            default:
                return;
        }
    }

    private async Task ReadLoopAsync(IControlNodeTransport transport, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var json = await transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (json is null)
            {
                if (transport.CloseCode == ProtocolTooOldCloseCode)
                {
                    UpdateState(state => state with
                    {
                        Status = ControlNodeLinkStatus.Revoked,
                        DetailKey = ControlDetailKeys.ProtocolTooOld,
                        RetryDelay = null
                    });
                    throw new ControlNodeTerminalException(ControlErrorCodes.ProtocolTooOld);
                }

                _logger?.LogDebug("集控长连接被服务端关闭：code={Code} reason={Reason}",
                    transport.CloseCode, transport.CloseReason);
                return;
            }

            var frame = ControlV4Protocol.TryParse(json);
            if (frame is null)
            {
                _logger?.LogDebug("集控收到无法解析的帧，已丢弃");
                continue;
            }

            HandleFrame(frame, cancellationToken);
        }
    }

    private void HandleFrame(ControlFrame frame, CancellationToken cancellationToken)
    {
        switch (frame.Type)
        {
            case ControlFrameTypes.Response:
                CompletePending(frame);
                break;

            case ControlFrameTypes.Event:
                HandleEvent(frame, cancellationToken);
                break;

            default:
                _logger?.LogDebug("集控收到未知帧类型 {Type}，已忽略", frame.Type);
                break;
        }
    }

    private void CompletePending(ControlFrame frame)
    {
        if (string.IsNullOrWhiteSpace(frame.Id))
            return;

        if (_pending.TryRemove(frame.Id, out var completion))
            completion.TrySetResult(frame);
    }

    private void HandleEvent(ControlFrame frame, CancellationToken cancellationToken)
    {
        switch (frame.Topic)
        {
            case ControlTopics.Command:
                var payload = frame.Payload;
                _ = Task.Run(() => HandleCommandAsync(payload, cancellationToken), CancellationToken.None);
                break;

            case ControlTopics.DesiredState:
                ApplyDesiredState(frame.Payload);
                break;

            case ControlTopics.Evicted:
                _ = Task.Run(() => HandleEvictedAsync(frame.Payload), CancellationToken.None);
                break;

            default:
                _logger?.LogDebug("集控收到未知事件 {Topic}，已忽略", frame.Topic);
                break;
        }
    }

    /// <summary>
    ///     应用期望状态：<b>旧 revision 一律丢弃</b>（服务端重连后可能把旧的补投一遍，
    ///     应用了就会把班级/抽取锁倒回上一个状态）。落盘 revision 是为了重启后不被重放。
    /// </summary>
    private void ApplyDesiredState(JsonObject? payload)
    {
        var desired = ControlDesiredStatePayload.FromPayload(payload);
        if (desired.Revision <= 0)
        {
            _logger?.LogWarning("集控期望状态缺 revision，已丢弃");
            return;
        }

        if (desired.Revision <= AppliedRevision)
        {
            _logger?.LogDebug("集控期望状态 revision {Revision} 不比已应用的 {Applied} 新，已丢弃",
                desired.Revision, AppliedRevision);
            return;
        }

        Interlocked.Exchange(ref _appliedRevision, desired.Revision);
        lock (_gate)
        {
            _desiredClassId = desired.ClassId;
            _drawLocked = desired.DrawLocked ?? true;
        }

        PersistState();

        // 状态本身没变（revision 不在 State 里），但订阅方要靠这次通知去重读
        // DesiredClassId / DrawLocked。
        RaiseStateChanged(State);
    }

    private async Task HandleEvictedAsync(JsonObject? payload)
    {
        ControlEvictedPayload? evicted;
        try
        {
            evicted = payload?.Deserialize<ControlEvictedPayload>(ControlV4Protocol.JsonOptions);
        }
        catch (JsonException)
        {
            evicted = null;
        }

        var reason = evicted?.Reason ?? string.Empty;
        _logger?.LogInformation("集控设备被踢出：reason={Reason} class={ClassId}", reason, evicted?.ResolveClassId());

        switch (reason)
        {
            case "revoked":
                // 网页上把设备删了：清凭证，否则会拿着作废的 secret 无限重连。
                await _credentials.ClearAsync().ConfigureAwait(false);
                UpdateState(state => state with
                {
                    Status = ControlNodeLinkStatus.Revoked,
                    DetailKey = ControlDetailKeys.Revoked,
                    RetryDelay = null,
                    SelectedClassId = null
                });
                _roundCts?.Cancel();
                break;

            case "superseded":
            case "takeover":
                // 别的设备接管了这个班：连接还在，本机只是失去了班（A4：回到未选班 + 黄条，不要重新接入）。
                UpdateState(state => state with
                {
                    Status = ControlNodeLinkStatus.Superseded,
                    DetailKey = ControlDetailKeys.Superseded,
                    RetryDelay = null,
                    SelectedClassId = null
                }, persist: true);
                break;

            case "replaced":
                UpdateState(state => state with
                {
                    Status = ControlNodeLinkStatus.Stopped,
                    DetailKey = ControlDetailKeys.Stopped,
                    RetryDelay = null,
                    SelectedClassId = null
                }, persist: true);
                _roundCts?.Cancel();
                break;

            default:
                _logger?.LogDebug("集控踢出原因 {Reason} 不认识，已忽略", reason);
                break;
        }
    }

    private async Task HandleCommandAsync(JsonObject? payload, CancellationToken cancellationToken)
    {
        var invocation = ControlCommandInvocation.FromPayload(payload);
        if (string.IsNullOrWhiteSpace(invocation.CommandId) || string.IsNullOrWhiteSpace(invocation.Command))
        {
            _logger?.LogWarning("集控指令缺 command_id 或 cmd，已丢弃");
            return;
        }

        var commandId = invocation.CommandId;

        // 校验顺序与协议 §3.3 一致：过期 → 本机开关 → 能力 → 幂等；通过之后先 ack 再执行。
        if (IsExpired(invocation))
        {
            await TrySendAckAsync(commandId, false, ControlErrorCodes.Expired, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!IsRemoteControlAllowed)
        {
            await TrySendAckAsync(commandId, false, ControlErrorCodes.LocalRemoteDisabled, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!_handlers.ContainsKey(invocation.Command))
        {
            await TrySendAckAsync(commandId, false, ControlErrorCodes.CapabilityUnsupported, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (_ledger.TryGet(commandId, out var cached) && cached is not null)
        {
            await TrySendAckAsync(commandId, true, null, cancellationToken).ConfigureAwait(false);
            await TrySendResultAsync(commandId, cached, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_inFlight.TryGetValue(commandId, out var running))
        {
            // 同一条指令正在跑（服务端没收到 result 又投了一次）：等它出结果，回同一份，不重复执行。
            await TrySendAckAsync(commandId, true, null, cancellationToken).ConfigureAwait(false);
            var shared = await running.ConfigureAwait(false);
            await TrySendResultAsync(commandId, shared, cancellationToken).ConfigureAwait(false);
            return;
        }

        var completion = new TaskCompletionSource<ControlCommandOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inFlight.TryAdd(commandId, completion.Task))
            return;

        try
        {
            await TrySendAckAsync(commandId, true, null, cancellationToken).ConfigureAwait(false);
            var outcome = await ExecuteWithDeadlineAsync(invocation, cancellationToken).ConfigureAwait(false);
            _ledger.TryRemember(commandId, outcome);
            completion.TrySetResult(outcome);
            await TrySendResultAsync(commandId, outcome, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var failed = ControlCommandOutcome.Failure(ControlErrorCodes.Failed, ex.Message);
            _ledger.TryRemember(commandId, failed);
            completion.TrySetResult(failed);
            _logger?.LogDebug(ex, "集控指令 {Command} 执行失败", invocation.Command);
            await TrySendResultAsync(commandId, failed, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(commandId, out _);
        }
    }

    private async Task<ControlCommandOutcome> ExecuteWithDeadlineAsync(
        ControlCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(invocation.Command, out var handler))
            return ControlCommandOutcome.Failure(ControlErrorCodes.CapabilityUnsupported);

        var budgetMs = invocation.DeadlineMs > 0 ? invocation.DeadlineMs : _commandTimeoutMs;
        var budget = TimeSpan.FromMilliseconds(Math.Max(0, budgetMs));
        var timeout = budget - _options.CommandResultMargin;
        if (timeout <= TimeSpan.Zero)
            timeout = TimeSpan.FromMilliseconds(1);

        var execution = InvokeHandlerAsync(handler, invocation, cancellationToken);
        var winner = await Task.WhenAny(execution, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
        if (winner == execution)
            return await execution.ConfigureAwait(false);

        // 到点没跑完：客户端在 deadline − 200ms 代回 timeout（服务端不补投，迟到的结果没人要）。
        _ = execution.ContinueWith(
            task => _logger?.LogDebug(task.Exception, "集控指令 {Command} 超时后才结束", invocation.Command),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return ControlCommandOutcome.Failure(ControlErrorCodes.Timeout);
    }

    private async Task<ControlCommandOutcome> InvokeHandlerAsync(
        Func<ControlCommandInvocation, CancellationToken, Task<object?>> handler,
        ControlCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler(invocation, cancellationToken).ConfigureAwait(false);
            return ControlCommandOutcome.Success(ToJsonObject(result));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "集控指令 {Command} 的处理器抛出异常", invocation.Command);
            return ControlCommandOutcome.Failure(ControlErrorCodes.Failed, ex.Message);
        }
    }

    private static JsonObject ToJsonObject(object? result)
    {
        if (result is null)
            return new JsonObject();
        if (result is JsonObject jsonObject)
            return (JsonObject)jsonObject.DeepClone();

        try
        {
            var node = JsonSerializer.SerializeToNode(result, result.GetType(), ControlV4Protocol.JsonOptions);
            return node as JsonObject ?? new JsonObject { ["value"] = node };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new JsonObject();
        }
    }

    private static bool IsExpired(ControlCommandInvocation invocation)
    {
        if (invocation.ExpiresAt <= 0)
            return false;

        // 服务端按毫秒时间戳发；参考实现见过秒级的旧写法（小于 1e11 视为秒）。
        var expiresAtMs = invocation.ExpiresAt < 100_000_000_000L ? invocation.ExpiresAt * 1000 : invocation.ExpiresAt;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > expiresAtMs;
    }

    private Task TrySendAckAsync(string commandId, bool accepted, string? code, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToNode(
                          new ControlCommandAckPayload { Accepted = accepted, Code = code },
                          ControlV4Protocol.JsonOptions) as JsonObject ??
                      new JsonObject { ["accepted"] = accepted };
        return TrySendFrameAsync(ControlFrame.Request(commandId, ControlTopics.CommandAck, payload), cancellationToken);
    }

    private Task TrySendResultAsync(string commandId, ControlCommandOutcome outcome, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToNode(
                          new ControlCommandResultPayload
                          {
                              Ok = outcome.Ok,
                              Data = outcome.Data,
                              Code = outcome.Ok ? null : outcome.Code ?? ControlErrorCodes.Failed,
                              Message = outcome.Message
                          },
                          ControlV4Protocol.JsonOptions) as JsonObject ??
                      new JsonObject { ["ok"] = outcome.Ok };
        return TrySendFrameAsync(ControlFrame.Request(commandId, ControlTopics.CommandResult, payload), cancellationToken);
    }

    private async Task TrySendFrameAsync(ControlFrame frame, CancellationToken cancellationToken)
    {
        var transport = _transport;
        if (transport is null)
            return;

        try
        {
            await SendFrameAsync(transport, frame, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 回执发不出去只说明连接已经坏了；读循环那边会自己发现并重连。
            _logger?.LogDebug(ex, "集控回执发送失败：topic={Topic} id={Id}", frame.Topic, frame.Id);
        }
    }

    private static Task SendFrameAsync(IControlNodeTransport transport, ControlFrame frame, CancellationToken ct) =>
        transport.SendAsync(ControlV4Protocol.Serialize(frame), ct);

    /// <summary>探活自检：服务端每 20s 会发 WS ping，这里再补一层应用层 ping 用来发现半开连接。</summary>
    private async Task HeartbeatLoopAsync(CancellationTokenSource roundCts, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _heartbeatSeconds));
        var failures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            await DelayAsync(interval, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                return;

            try
            {
                await RequestAsync(ControlTopics.Ping, new JsonObject(), _options.RequestTimeout, cancellationToken)
                    .ConfigureAwait(false);
                failures = 0;
            }
            catch (Exception ex) when (ex is ControlNodeRequestException or ControlNodeNotConnectedException
                                           or IOException)
            {
                failures++;
                _logger?.LogDebug(ex, "集控探活失败（连续 {Count} 次）", failures);
                if (failures >= 2)
                {
                    _logger?.LogWarning("集控探活连续失败，主动断开以触发重连");
                    roundCts.Cancel();
                    return;
                }
            }
        }
    }

    private async Task TryRestoreClassAsync(string classId, CancellationToken cancellationToken)
    {
        try
        {
            await SendClassRequestAsync(ControlTopics.ClassSelect, classId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ControlNodeRequestException or ControlNodeNotConnectedException or IOException)
        {
            _logger?.LogDebug(ex, "集控重连后重占班 {ClassId} 失败", classId);
        }
    }

    private async Task SendClassRequestAsync(
        string topic,
        string? classId,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToNode(
                          new ControlClassSelectPayload { ClassId = classId, Platform = _options.Platform },
                          ControlV4Protocol.JsonOptions) as JsonObject ??
                      new JsonObject();

        var response = await RequestAsync(topic, payload, null, cancellationToken).ConfigureAwait(false);
        if (response.Ok != true)
            throw new ControlNodeRequestException(
                string.IsNullOrWhiteSpace(response.Code) ? ControlErrorCodes.Failed : response.Code,
                response.Message);
    }

    private void FailPending(Exception exception)
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var completion))
                completion.TrySetException(exception);
        }
    }

    private void PersistState()
    {
        _stateStore.Save(new ControlNodeState(AppliedRevision, State.SelectedClassId, DateTimeOffset.UtcNow));
    }

    private void SetStopped()
    {
        if (State.Status is ControlNodeLinkStatus.Revoked or ControlNodeLinkStatus.Disabled)
            return;

        UpdateState(state => state with
        {
            Status = ControlNodeLinkStatus.Stopped,
            DetailKey = ControlDetailKeys.Stopped,
            RetryDelay = null
        });
    }

    private void UpdateState(Func<ControlNodeLinkState, ControlNodeLinkState> update, bool persist = false)
    {
        ControlNodeLinkState newState;
        lock (_gate)
        {
            newState = update(_state);
            if (newState == _state)
                return;
            _state = newState;
        }

        if (persist)
            PersistState();
        RaiseStateChanged(newState);
    }

    private void RaiseStateChanged(ControlNodeLinkState state)
    {
        var handler = StateChanged;
        if (handler is null)
            return;

        try
        {
            handler(this, state);
        }
        catch (Exception ex)
        {
            _logger?.LogCallbackFailure(nameof(StateChanged), ex);
        }
    }

    private string NextRequestId() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"net_{Interlocked.Increment(ref _sequence)}_{Guid.NewGuid().ToString("N")[..8]}");

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
            return;

        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消由调用方的循环条件判断，这里不抛。
        }
    }

    /// <summary>请求失败（服务端拒绝 / 超时）。</summary>
    public sealed class ControlNodeRequestException : Exception
    {
        /// <summary>建一个带协议错误码的异常。</summary>
        public ControlNodeRequestException(string code, string? message = null) : base(message ?? code) =>
            Code = code;

        /// <summary>协议错误码（<c>timeout</c> / <c>busy</c> / …）。</summary>
        public string Code { get; }
    }

    /// <summary>终态异常：握手被拒 / 被吊销，循环据此退出、不再重连。</summary>
    private sealed class ControlNodeTerminalException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }
}
