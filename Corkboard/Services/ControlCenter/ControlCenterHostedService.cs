using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Text.Json.Nodes;
using Corkboard.Core.ControlCenter;
using Corkboard.Core.Langs.Common;
using Corkboard.Core.Models.SubConfigs.ControlCenter;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Ui;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.ControlCenter;

/// <summary>
///     集控（SECTL control center）的应用层运行时服务。
///     <para>
///         Core 的 <see cref="ControlNodeClient" /> 只负责协议、连接与凭证；<b>用户可见的副作用</b>
///         （接入 / 配对的 toast、重载配置）都在这里通过 <see cref="ControlNodeClient.RegisterHandler" />
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

    /// <summary>唯一注册的命令：重载配置（不弹任何界面）。</summary>
    private const string CommandReloadConfig = "reloadConfig";

    private readonly MainConfigHandler _configHandler;
    private readonly IControlNodeCredentialProvider _credentials;
    private readonly IToastService _toastService;
    private readonly ILogger<ControlCenterHostedService> _logger;

    /// <summary>重建客户端 / 启动客户端的串行闸门：配置变化与重启请求不可能同时动同一个客户端。</summary>
    private readonly SemaphoreSlim _restartGate = new(1, 1);

    /// <summary>保护 <see cref="_client" /> 的读写；`EnrollAsync` 这类公开方法可能在托管服务启动前被调用。</summary>
    private readonly object _clientGate = new();

    /// <summary>400ms 防抖的取消源；每次新的地址 / 设备名变更都会替换它。</summary>
    private readonly object _debounceGate = new();

    private CancellationTokenSource? _pendingRestart;

    /// <summary>当前订阅着的子配置对象（<c>Reload()</c> 会整个换掉它，所以要跟着换订阅）。</summary>
    private ControlCenterSettingsConfig? _watchedConfig;

    private ControlNodeClient? _client;

    private ControlNodeLinkState _snapshot = ControlNodeLinkState.Initial;

    /// <summary>上一次看到的开关值（<c>PropertyChanged</c> 不告诉我们旧值，只能自己记）。</summary>
    private bool _lastEnabled;

    /// <summary>开关 + 地址 + 设备名的指纹，用于判断 <c>Reload()</c> 是否真的换了连接参数。</summary>
    private string _fingerprint = string.Empty;

    private bool _disposed;

    public ControlCenterHostedService(
        MainConfigHandler configHandler,
        IControlNodeCredentialProvider credentials,
        IToastService toastService,
        ILogger<ControlCenterHostedService> logger)
    {
        _configHandler = configHandler;
        _credentials = credentials;
        _toastService = toastService;
        _logger = logger;

        _configHandler.Reloaded += OnConfigReloaded;
        SubscribeControlCenter();
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

    /// <summary>占班（上课）：离线时只记住，重连后自动重占。</summary>
    public Task SelectClassAsync(string classId) => EnsureClient().SelectClassAsync(classId);

    /// <summary>释放班（下课）。</summary>
    public Task ReleaseClassAsync() => EnsureClient().ReleaseClassAsync();

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

        // 同步释放路径上不等网络：先摘掉引用，销毁在后台完成（客户端自己的 DisposeAsync 不带网络等待）。
        _ = StopAndDisposeClientAsync();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>构造客户端并挂上命令处理器（只有重载配置一条）与状态订阅。</summary>
    private ControlNodeClient CreateClient()
    {
        var client = new ControlNodeClient(
            _configHandler.Data.ControlCenter,
            logger: null,
            credentials: _credentials);

        client.RegisterHandler(CommandReloadConfig, HandleReloadConfigAsync);
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
                    _ = StopForDisabledAsync();
                }

                break;
            }
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