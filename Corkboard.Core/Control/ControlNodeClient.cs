using System.Text.Json.Nodes;
using Corkboard.Core.Models.SubConfigs.ControlCenter;
using Microsoft.Extensions.Logging;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     集控（远程控制）客户端门面：下一波的 UI 只依赖这一个类。
///     <para>
///         它把配置、凭证文件、状态文件、HTTP 接入与长连接会话拼在一起，并且**对外不抛**：
///         <see cref="StartAsync" /> 没开 / 没凭证 / 连不上都只返回 <c>false</c> 并记日志——
///         远程控制挂了不能把整个作业板带崩。
///     </para>
///     <para>
///         <see cref="DeclaredCapabilities" /> 永远由「已注册的指令处理器」推导，再补三个固定状态能力；
///         注册了 <c>refreshNotice</c> 才声明 <c>refreshNotice</c>。
///         <b>永远不声明 <c>broadcast</c></b>（Corkboard 没有广播面）。
///     </para>
/// </summary>
public sealed class ControlNodeClient : IAsyncDisposable
{
    private readonly ControlCenterSettingsConfig _config;
    private readonly IControlNodeCredentialProvider _credentials;
    private readonly ControlNodeEnrollmentApi _enrollmentApi;
    private readonly bool _ownsEnrollmentApi;
    private readonly object _gate = new();
    private readonly HashSet<string> _handlerCommands = new(StringComparer.Ordinal);
    private readonly ILogger<ControlNodeClient>? _logger;
    private readonly ControlNodeConnectOptions _options;
    private readonly ControlNodeSession _session;

    private bool _enabled;
    private ControlNodeLinkState _state;

    /// <summary>
    ///     建一个集控客户端。除配置外的依赖都可注入（测试 / 应用层替换传输实现）。
    /// </summary>
    /// <param name="config">集控设置（就是 <c>settings.json</c> 里的 <c>control_center</c> 段）。</param>
    /// <param name="logger">可选日志。</param>
    /// <param name="credentials">凭证读写（默认数据根下的 AES-GCM 文件）。</param>
    /// <param name="stateStore">状态落盘（默认 <c>node-state.json</c>）。</param>
    /// <param name="transportFactory">长连接工厂（默认 WebSocket）。</param>
    /// <param name="enrollmentApi">HTTP 接入接口（默认自己建，测试可注入假的 <see cref="HttpMessageHandler" />）。</param>
    /// <param name="options">连接参数（默认值即可）。</param>
    public ControlNodeClient(
        ControlCenterSettingsConfig config,
        ILogger<ControlNodeClient>? logger = null,
        IControlNodeCredentialProvider? credentials = null,
        IControlNodeStateStore? stateStore = null,
        IControlNodeTransportFactory? transportFactory = null,
        ControlNodeEnrollmentApi? enrollmentApi = null,
        ControlNodeConnectOptions? options = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _options = options ?? new ControlNodeConnectOptions();
        _credentials = credentials ?? new NodeEnrollmentStore();
        _enrollmentApi = enrollmentApi ?? new ControlNodeEnrollmentApi(_options);
        _ownsEnrollmentApi = enrollmentApi is null;
        _session = new ControlNodeSession(
            _options,
            _credentials,
            stateStore,
            transportFactory,
            logger,
            string.IsNullOrWhiteSpace(config.DisplayName) ? Environment.MachineName : config.DisplayName);
        _session.StateChanged += OnSessionStateChanged;

        _enabled = config.Enabled;
        _state = config.Enabled
            ? new ControlNodeLinkState(ControlNodeLinkStatus.Unpaired, ControlDetailKeys.Unpaired)
            : ControlNodeLinkState.Initial;
    }

    /// <summary>当前状态快照。</summary>
    public ControlNodeLinkState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    /// <summary>状态变化事件（回调里抛异常只记 debug，不影响连接）。</summary>
    public event EventHandler<ControlNodeLinkState>? StateChanged;

    /// <summary>
    ///     本机要声明的能力：已注册处理器（= 指令名）∪ 三个状态能力，
    ///     按 ordinal 排序、去重、**过滤 <c>broadcast</c>**。
    /// </summary>
    public IReadOnlyList<string> DeclaredCapabilities
    {
        get
        {
            var capabilities = new SortedSet<string>(StringComparer.Ordinal);
            lock (_gate)
            {
                foreach (var command in _handlerCommands)
                {
                    if (!string.Equals(command, ControlV4Protocol.CapabilityBroadcast, StringComparison.Ordinal))
                        capabilities.Add(command);
                }
            }

            foreach (var capability in ControlV4Protocol.StateCapabilities)
                capabilities.Add(capability);

            return capabilities.ToArray();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _session.StateChanged -= OnSessionStateChanged;
        await _session.DisposeAsync().ConfigureAwait(false);
        if (_ownsEnrollmentApi)
            _enrollmentApi.Dispose();
    }

    /// <summary>
    ///     启动集控客户端。<b>不抛异常</b>：没启用 / 没凭证 / 连不上都返回 <c>false</c>（后台会继续退避重连）。
    ///     <para>
    ///         配置里同时有地点 id 与配对码时会先自动接入（一次性配对码用掉后立刻写回空串）。
    ///     </para>
    /// </summary>
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _enabled = _config.Enabled;
            if (!_enabled)
            {
                await _session.StopAsync().ConfigureAwait(false);
                SetState(ControlNodeLinkStatus.Disabled, ControlDetailKeys.Disabled);
                return false;
            }

            SyncOptions();

            if (!string.IsNullOrWhiteSpace(_config.SiteId) && !string.IsNullOrWhiteSpace(_config.PairCode))
            {
                var (ok, errorKey) = await EnrollAsync(
                    _config.SiteId, _config.PairCode, _config.DisplayName, cancellationToken).ConfigureAwait(false);
                if (!ok)
                {
                    _logger?.LogInformation("集控启用但自动接入失败（{ErrorKey}），等待用户重新输入配对码", errorKey);
                    return false;
                }
            }

            var credential = _credentials.Current ??
                             await _credentials.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (credential is null || !credential.IsComplete)
            {
                SetState(ControlNodeLinkStatus.Unpaired, ControlDetailKeys.Unpaired);
                _logger?.LogInformation("集控已启用但还没有设备凭证");
                return false;
            }

            var started = await _session.StartAsync(cancellationToken).ConfigureAwait(false);
            if (!started)
                _logger?.LogInformation("集控首次连接未成功，后台会按退避继续重连");
            return started;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "集控启动失败");
            return false;
        }
    }

    /// <summary>停止集控客户端（幂等）。</summary>
    public async Task StopAsync()
    {
        _enabled = false;
        try
        {
            await _session.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "集控停止时出错");
        }
    }

    /// <summary>
    ///     注册一条指令的处理器。参数是 <c>cmd.args</c>；返回值会被序列化进 <c>cmd.result.data</c>。
    ///     <para>
    ///         处理器在后台线程上跑（**不是 UI 线程**），要碰界面请自己切回去；
    ///         返回 <c>null</c> 表示成功但没有数据。
    ///     </para>
    /// </summary>
    public void RegisterHandler(string command, Func<JsonObject, CancellationToken, Task<object?>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
            _handlerCommands.Add(command);

        _session.RegisterHandler(command, (invocation, cancellationToken) => handler(invocation.Args, cancellationToken));
        _session.DeclaredCapabilities = DeclaredCapabilities;
    }

    /// <summary>
    ///     接入：可以用「地点ID + 配对码」两个参数，也可以把网页上那一整句直接粘进第一个参数
    ///     （<c>地点ID：SC-6M34RQ　配对码：K7Q2-9XMD　有效期 24 小时</c>）。
    ///     <para>
    ///         成功会写设备凭证（加密文件）并把一次性配对码写回空串；失败返回本地化错误键。
    ///         <b>不抛异常。</b>
    ///     </para>
    /// </summary>
    public async Task<(bool Ok, string ErrorKey)> EnrollAsync(
        string siteIdOrPastedSentence,
        string pairCode,
        string? deviceName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            SyncOptions();
            var (siteId, code) = ResolveEnrollmentInput(siteIdOrPastedSentence, pairCode);
            if (siteId is null || code is null)
                return (false, ControlEnrollErrorKeys.CodeInvalid);

            var result = await _enrollmentApi
                .EnrollAsync(siteId, code, deviceName ?? _config.DisplayName, DeclaredCapabilities, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Ok || result.Credential is null)
            {
                _logger?.LogWarning("集控接入失败：{ErrorKey} {Message}", result.ErrorKey, result.Message);
                return (false, result.ErrorKey ?? ControlEnrollErrorKeys.Unknown);
            }

            await _credentials.SaveAsync(result.Credential, cancellationToken).ConfigureAwait(false);
            ApplyEnrolledConfig(result.Credential);
            _logger?.LogInformation(
                "集控接入成功：site={SiteId} device={DeviceId}",
                result.Credential.SiteId,
                result.Credential.DeviceId);

            // 之前因为没凭证没连上：现在把连接循环踢起来（不阻塞接入调用）。
            if (_enabled && !_session.IsStarted)
                _ = StartSessionInBackgroundAsync();

            return (true, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return (false, ControlEnrollErrorKeys.Network);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "集控接入异常");
            return (false, ControlEnrollErrorKeys.Network);
        }
    }

    /// <summary>
    ///     忘记接入：清凭证文件、停止重连、回到「未接入」。地点 id 保留（重新接入时不用重填）。
    /// </summary>
    public async Task ForgetEnrollmentAsync()
    {
        try
        {
            await _session.StopAsync().ConfigureAwait(false);
            await _credentials.ClearAsync().ConfigureAwait(false);
            _config.PairCode = string.Empty;
            _config.SelectedClassId = string.Empty;
            SetState(ControlNodeLinkStatus.Unpaired, ControlDetailKeys.Unpaired);
            _logger?.LogInformation("集控设备凭证已清除");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "集控凭证清除失败");
        }
    }

    /// <summary>读设备结构树（年级 → 班级）。没接入或请求失败都返回空列表（不抛）。</summary>
    public async Task<IReadOnlyList<ControlGradeNode>> GetDeviceTreeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var credential = _credentials.Current ??
                             await _credentials.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (credential is null || !credential.IsComplete)
                return [];

            SyncOptions();
            var result = await _enrollmentApi.GetDeviceTreeAsync(credential, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                _logger?.LogWarning("集控读结构树失败：{ErrorKey} {Message}", result.ErrorKey, result.Message);
                return [];
            }

            return result.Grades;
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "集控读结构树异常");
            return [];
        }
    }

    /// <summary>占班（上课）。离线时只记住，重连后自动重占；服务端拒绝只记日志（不抛）。</summary>
    public async Task SelectClassAsync(string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
            return;

        try
        {
            await _session.SelectClassAsync(classId.Trim(), CancellationToken.None).ConfigureAwait(false);
            _config.SelectedClassId = classId.Trim();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "集控选班失败：{ClassId}", classId);
        }
    }

    /// <summary>释放班（下课）。不抛。</summary>
    public async Task ReleaseClassAsync()
    {
        try
        {
            await _session.ReleaseClassAsync(CancellationToken.None).ConfigureAwait(false);
            _config.SelectedClassId = string.Empty;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "集控释放班失败");
        }
    }

    /// <summary>把设置里的地址 / 设备名 / 开关同步进连接参数与会话。</summary>
    private void SyncOptions()
    {
        _options.ServerUrl = ControlCenterSettingsConfig.NormalizeServerUrl(_config.ServerUrl);
        _session.DeviceName = string.IsNullOrWhiteSpace(_config.DisplayName)
            ? Environment.MachineName
            : _config.DisplayName.Trim();
        _session.IsRemoteControlAllowed = _config.Enabled;
        _session.DeclaredCapabilities = DeclaredCapabilities;
    }

    /// <summary>接入成功后写回配置：地点 id 归一值 + 一次性配对码抹掉。</summary>
    private void ApplyEnrolledConfig(ControlNodeCredential credential)
    {
        _config.SiteId = credential.SiteId;
        _config.PairCode = string.Empty;
        if (!string.IsNullOrWhiteSpace(credential.ClassId))
            _config.SelectedClassId = credential.ClassId;
    }

    /// <summary>
    ///     解析接入输入：先试整句粘贴；整句只认出地点（返回 false 且两个 out 都是 null）时
    ///     再退回两个参数各自归一化。
    /// </summary>
    private static (string? SiteId, string? Code) ResolveEnrollmentInput(string siteText, string pairText)
    {
        if (NodeEnrollment.TryParseEnrollmentText(siteText, out var pastedSite, out var pastedCode))
            return (pastedSite, pastedCode);

        if (NodeEnrollment.TryParseEnrollmentText(pairText, out var pairSite, out var pairCode))
            return (pairSite, pairCode);

        return (NodeEnrollment.NormalizeSiteId(siteText), NodeEnrollment.NormalizePairCode(pairText));
    }

    private async Task StartSessionInBackgroundAsync()
    {
        try
        {
            var started = await _session.StartAsync().ConfigureAwait(false);
            if (!started)
                _logger?.LogInformation("集控后台连接未成功，会继续按退避重连");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "集控后台连接异常");
        }
    }

    private void OnSessionStateChanged(object? sender, ControlNodeLinkState state) => SetState(state);

    private void SetState(ControlNodeLinkStatus status, string? detailKey, string? selectedClassId = null) =>
        SetState(new ControlNodeLinkState(status, detailKey, null, selectedClassId ?? State.SelectedClassId));

    private void SetState(ControlNodeLinkState state)
    {
        lock (_gate)
        {
            if (state == _state)
                return;
            _state = state;
        }

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
}
