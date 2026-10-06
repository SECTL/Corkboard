using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Shared.IPC;
using Corkboard4Ci.Interface.Enums;
using Corkboard4Ci.Interface.Models;
using Corkboard4Ci.Interface.Services;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using dotnetCampus.Ipc.Exceptions;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Ipc;

/// <summary>
/// ClassIsland IPC 通知通道的唯一连接入口：连上 ClassIsland 的 IPC 管道，探测 Corkboard4Ci
/// 插件可用性，然后把通用通知投递过去。
/// </summary>
/// <remarks>
/// <para>
/// 与上游参考实现（同名 <c>ClassIslandIpcConnection</c>）相比，这里**只**保留通知这一条能力：
/// 课程表/课程联动、抽取结果回传、按抽取类型的版本门槛分支都不在范围内。
/// </para>
/// <para>
/// 失败策略：所有失败路径都只反映在返回值（<see cref="IpcProbeResult"/> /
/// <see cref="NotificationSendResult"/>）与日志里，连接尝试在后台线程进行，
/// 因此不会在应用启动路径上抛出致命异常；调用方拿到失败结果后自行决定是否走内置回退通道。
/// </para>
/// </remarks>
public sealed class ClassIslandIpcConnection : IClassIslandIpcConnection
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PeerReadyDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IpcCallTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ServiceWaitTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>通知契约要求的最低插件版本；低于此版本的插件视为不可用。</summary>
    public static readonly Version MinimumPluginVersion = new(1, 0, 0, 0);

    private readonly ILogger<ClassIslandIpcConnection> _logger;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly object _stateLock = new();

    private IpcClient? _client;
    private ICorkboardService? _corkboardService;
    private Version? _pluginVersion;
    private DateTimeOffset _nextConnectAttempt = DateTimeOffset.MinValue;
    private TimeSpan _currentRetryDelay = MinRetryDelay;
    private bool _isDisposed;
    private volatile int _connectionState; // 0=disconnected, 1=connecting

    public ClassIslandIpcConnection(ILogger<ClassIslandIpcConnection> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsConnected
    {
        get
        {
            lock (_stateLock)
            {
                return _client is not null;
            }
        }
    }

    /// <inheritdoc />
    public bool IsPluginAvailable
    {
        get
        {
            lock (_stateLock)
            {
                return _corkboardService is not null;
            }
        }
    }

    /// <summary>最近一次成功探测到的插件版本；未探测到时为 <c>null</c>。</summary>
    public Version? PluginVersion
    {
        get
        {
            lock (_stateLock)
            {
                return _pluginVersion;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public async Task<IpcProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            return new IpcProbeResult(IpcConnectionState.Disconnected, Error: "IPC 连接已释放。");

        var service = await WaitForServiceAsync(ServiceWaitTimeout, cancellationToken).ConfigureAwait(false);
        if (service is not null)
            return new IpcProbeResult(IpcConnectionState.Available, PluginVersion);

        return IsConnected
            ? new IpcProbeResult(
                IpcConnectionState.PluginUnavailable,
                PluginVersion,
                "Corkboard4Ci 插件不可用（未安装、未启用或版本低于契约要求）。")
            : new IpcProbeResult(
                IpcConnectionState.Disconnected,
                Error: "未能连接到 ClassIsland IPC（ClassIsland 未运行，或正处于重试退避期）。");
    }

    /// <inheritdoc />
    public async Task<NotificationSendResult> SendNotificationAsync(
        NotificationData notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (_isDisposed)
            return NotificationSendResult.Unavailable("IPC 连接已释放。");

        var service = await WaitForServiceAsync(ServiceWaitTimeout, cancellationToken).ConfigureAwait(false);
        if (service is null)
        {
            var reason = IsConnected
                ? "Corkboard4Ci 插件不可用（未安装、未启用或版本低于契约要求）。"
                : "ClassIsland IPC 不可用（ClassIsland 未运行，或正处于重试退避期）。";
            _logger.LogDebug("未发送 ClassIsland 通知：{Reason}", reason);
            return NotificationSendResult.Unavailable(reason);
        }

        try
        {
            await InvokeIpcCallAsync(() => service.ShowNotification(notification), IpcCallTimeout, cancellationToken)
                .ConfigureAwait(false);
            return NotificationSendResult.Delivered;
        }
        catch (TimeoutException exception)
        {
            _logger.LogDebug(exception, "通过 Corkboard4Ci 插件发送 ClassIsland 通知超时。");
            return NotificationSendResult.TimedOut("通知投递超时（对端未在超时窗口内处理完）。", exception);
        }
        catch (IpcPeerConnectionBrokenException exception)
        {
            _logger.LogDebug(exception, "ClassIsland IPC 连接已断开，通知未投递。");
            InvalidateConnection();
            NextConnectAttempt = DateTimeOffset.MinValue;
            return NotificationSendResult.Unavailable("ClassIsland IPC 连接已断开。", exception);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "通过 Corkboard4Ci 插件发送 ClassIsland 通知失败。");
            return NotificationSendResult.Failed("通知投递失败。", exception);
        }
    }

    /// <inheritdoc />
    public Task<NotificationSendResult> SendNotificationAsync(
        string title,
        string body,
        double displayDuration = 5.0,
        NotificationLevel level = NotificationLevel.Unknown,
        string? boardId = null,
        IReadOnlyList<NotificationItem>? items = null,
        bool animation = true,
        CancellationToken cancellationToken = default)
    {
        var notification = new NotificationData
        {
            BoardId = boardId ?? string.Empty,
            Title = title ?? string.Empty,
            Body = body ?? string.Empty,
            Items = items is null ? new List<NotificationItem>() : [.. items],
            ItemCount = items is null || items.Count == 0 ? 1 : items.Count,
            DisplayDuration = Math.Clamp(displayDuration, 1, 60),
            Animation = animation,
            Level = level
        };

        return SendNotificationAsync(notification, cancellationToken);
    }

    /// <inheritdoc />
    public void Disconnect()
    {
        if (_isDisposed)
            return;

        InvalidateConnection();
        // 主动断开后允许下一次探测立刻重连
        NextConnectAttempt = DateTimeOffset.MinValue;
        _currentRetryDelay = MinRetryDelay;

        _logger.LogDebug("已断开 ClassIsland IPC 连接。");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        InvalidateConnection();
        // 不释放 _connectionGate：进行中的等待/连接可能仍持有它
    }

    /// <summary>
    /// 通知服务是否可用：插件必须自报存活，并且版本不低于通知契约的最低要求。
    /// </summary>
    public static bool IsNotificationServiceUsable(string? isAlive, Version? pluginVersion)
        => string.Equals(isAlive, "Yes", StringComparison.Ordinal)
           && pluginVersion is not null
           && pluginVersion >= MinimumPluginVersion;

    private ICorkboardService? CorkboardService
    {
        get
        {
            lock (_stateLock)
            {
                return _corkboardService;
            }
        }
    }

    private DateTimeOffset NextConnectAttempt
    {
        get
        {
            lock (_stateLock)
            {
                return _nextConnectAttempt;
            }
        }
        set
        {
            lock (_stateLock)
            {
                _nextConnectAttempt = value;
            }
        }
    }

    /// <summary>
    /// 通知路径只等一个很短的窗口：拿不到就立刻把「不可用」交给调用方走内置回退，
    /// 不把回退通知拖到超时之后。
    /// </summary>
    private async Task<ICorkboardService?> WaitForServiceAsync(TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        var service = CorkboardService;
        if (service is not null)
            return service;

        // 退避期内直接返回：否则每次调用都要白等一整个超时窗口
        if (_isDisposed || DateTimeOffset.UtcNow < NextConnectAttempt)
            return null;

        if (Interlocked.CompareExchange(ref _connectionState, 1, 0) == 0 && !_isDisposed)
            _ = Task.Run(TryConnectAsync);

        var deadline = DateTimeOffset.UtcNow + waitTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            service = CorkboardService;
            if (service is not null)
                return service;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task TryConnectAsync()
    {
        try
        {
            await EnsureConnectedAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "连接 ClassIsland IPC 时发生未处理异常。");
        }
        finally
        {
            Interlocked.Exchange(ref _connectionState, 0);
        }
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed || DateTimeOffset.UtcNow < NextConnectAttempt)
            return false;

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsPluginAvailable)
                return true;

            if (_isDisposed || DateTimeOffset.UtcNow < NextConnectAttempt)
                return false;

            // 插件缺失时旧管道仍然占着：先拆掉再重连，避免一条死管道把句柄和 StateChanged 一直挂着。
            InvalidateConnection();

            var client = new IpcClient();

            try
            {
                await client.Connect().WaitAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);
                // 对端注册 joint、管道清理都需要一拍时间；这一段等待与源实现保持一致。
                await Task.Delay(PeerReadyDelay, cancellationToken).ConfigureAwait(false);

                var peer = client.PeerProxy;
                if (peer is null)
                {
                    DisposeClient(client);
                    ScheduleRetry();
                    return false;
                }

                peer.PeerConnectionBroken += (_, _) => OnPeerConnectionBroken();

                // Corkboard4Ci 插件为可选依赖，但必须通过版本门槛（旧插件不满足通知契约）
                ICorkboardService? service = null;
                Version? pluginVersion = null;
                try
                {
                    var proxy = GeneratedIpcFactory.CreateIpcProxy<ICorkboardService>(client.Provider, peer);
                    var aliveProbe = await TryInvokeWithTimeoutAsync(proxy.IsAlive, IpcCallTimeout).ConfigureAwait(false);
                    var versionProbe = await TryInvokeWithTimeoutAsync(proxy.GetPluginVersion, IpcCallTimeout)
                        .ConfigureAwait(false);

                    var isAlive = aliveProbe.Success ? aliveProbe.Value : null;
                    pluginVersion = versionProbe.Success ? versionProbe.Value : null;
                    if (IsNotificationServiceUsable(isAlive, pluginVersion))
                    {
                        service = proxy;
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Corkboard4Ci 插件不可用或版本低于 {MinimumPluginVersion}：IsAlive={IsAlive}，版本={PluginVersion}。",
                            MinimumPluginVersion, isAlive, pluginVersion);
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "获取 Corkboard4Ci 通知服务代理失败。");
                }

                if (service is null)
                {
                    // 管道通了但插件不可用：保留管道（IsConnected 保持为真，便于诊断与后续复用），
                    // 并按退避节奏重新探测——插件后启动时能自动恢复。
                    lock (_stateLock)
                    {
                        _client = client;
                        _corkboardService = null;
                        _pluginVersion = pluginVersion;
                    }

                    ScheduleRetry();
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    return false;
                }

                lock (_stateLock)
                {
                    _client = client;
                    _corkboardService = service;
                    _pluginVersion = pluginVersion;
                }

                NextConnectAttempt = DateTimeOffset.MinValue;
                _currentRetryDelay = MinRetryDelay;

                _logger.LogInformation(
                    "已连接到 ClassIsland IPC：管道={PipeName}，Corkboard4Ci 插件版本={PluginVersion}。",
                    IpcClient.PipeName, pluginVersion);

                StateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "连接 ClassIsland IPC 失败，将在 {RetryDelay} 后重试。", _currentRetryDelay);
                DisposeClient(client);
                ScheduleRetry();
                return false;
            }
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <summary>
    /// 同步 IPC 调用不能无限等待：ClassIsland 卡住时按超时返回失败，让调用方走「不可用」分支。
    /// </summary>
    private static async Task<(bool Success, T Value)> TryInvokeWithTimeoutAsync<T>(Func<T> invoke, TimeSpan timeout)
    {
        try
        {
            return (true, await Task.Run(invoke).WaitAsync(timeout).ConfigureAwait(false));
        }
        catch (Exception)
        {
            return (false, default!);
        }
    }

    /// <summary>
    /// Corkboard4Ci 的代理方法是同步 IPC 调用，ClassIsland 卡住时不能无限等待。
    /// </summary>
    private static async Task InvokeIpcCallAsync(Action invoke, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var call = Task.Run(invoke);

        // 超时后这次调用仍在后台跑：挂一个观察者，避免未观察的异常在 GC 时冒泡。
        _ = call.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await call.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    private void OnPeerConnectionBroken()
    {
        if (_isDisposed)
            return;

        _logger.LogDebug("ClassIsland IPC 连接已断开，将尝试重连。");
        InvalidateConnection();
        // 断开后允许立即重连，但留一点间隔，避免与 ClassIsland 的广播/管道清理抢时序
        NextConnectAttempt = DateTimeOffset.MinValue;
        _currentRetryDelay = MinRetryDelay;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ReconnectDelay).ConfigureAwait(false);
                if (!_isDisposed)
                    await TryConnectAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "ClassIsland IPC 重连失败，等待下一次探测重试。");
            }
        });

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void InvalidateConnection()
    {
        IpcClient? client;
        lock (_stateLock)
        {
            client = _client;
            _client = null;
            _corkboardService = null;
            _pluginVersion = null;
        }

        DisposeClient(client);
    }

    private static void DisposeClient(IpcClient? client)
    {
        if (client is null)
            return;

        try
        {
            client.Provider.Dispose();
        }
        catch (Exception)
        {
            // 管道已经在拆的过程中：这里的异常对调用方没有意义。
        }
    }

    private void ScheduleRetry()
    {
        NextConnectAttempt = DateTimeOffset.UtcNow.Add(_currentRetryDelay);
        _currentRetryDelay = TimeSpan.FromSeconds(Math.Min(_currentRetryDelay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds));
    }
}
