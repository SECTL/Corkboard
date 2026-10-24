using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;

namespace Corkboard.Core.Time;

/// <summary>
///     应用里唯一的时间来源：把 NTP 校出来的偏差挂在系统时钟上。
///     <para>
///         <see cref="Offset" /> 的语义是「服务器 UTC − 本机 UTC」：正数表示本机慢了。
///         所有要「准」的时刻都该走 <see cref="UtcNow" /> / <see cref="NowLocal" />，不要直接
///         <c>DateTime.Now</c>——考试看板的「还有 12 分钟开考」全靠它。
///     </para>
///     <para>
///         同步失败（没网、源都不通）时**保留上一次成功的偏差**，不是清零：一次抖动不该让时钟跳回去。
///         关掉校时（<see cref="NtpSettingsConfig.Enabled" />）才把偏差归零、退回系统时钟。
///     </para>
/// </summary>
public sealed class NtpTimeService
{
    /// <summary>同一次同步只允许一条在路上（设置页的「立即同步」与后台定时可能撞在一起）。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly MainConfigHandler _config;
    private readonly NtpClient _client;
    private readonly ILogger<NtpTimeService>? _logger;

    public NtpTimeService(MainConfigHandler config, NtpClient client, ILogger<NtpTimeService>? logger = null)
    {
        _config = config;
        _client = client;
        _logger = logger;
    }

    /// <summary>校时设置（<c>settings.json</c> 的 <c>ntp</c> 段，现读，跟着配置变化走）。</summary>
    public NtpSettingsConfig Settings => _config.Data.Ntp;

    /// <summary>是否开着校时。</summary>
    public bool IsEnabled => Settings.Enabled;

    /// <summary>服务器 UTC 与本机 UTC 的偏差（正数 = 本机慢）。</summary>
    public TimeSpan Offset { get; private set; }

    /// <summary>最近一次同步的结果（成功或失败都留着，设置页据此显示状态）。</summary>
    public NtpSyncResult? LastResult { get; private set; }

    /// <summary>最近一次成功同步的时刻（UTC，未校正）。</summary>
    public DateTimeOffset? LastSuccessAt { get; private set; }

    /// <summary>此刻是不是有一条同步在路上。</summary>
    public bool IsSyncing { get; private set; }

    /// <summary>是否至少成功同步过一次（看板用它决定要不要标「已校时」）。</summary>
    public bool IsSynced => LastSuccessAt is not null && IsEnabled;

    /// <summary>状态变了（同步开始 / 结束 / 偏差变化）。设置页与看板据此刷新。</summary>
    public event EventHandler? StatusChanged;

    /// <summary>校正后的 UTC 时刻。</summary>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + Offset;

    /// <summary>校正后的本地时刻（看板时钟直接用它）。</summary>
    public DateTime NowLocal => UtcNow.ToLocalTime().DateTime;

    /// <summary>
    ///     立刻按当前配置的源同步一次。多个源按顺序试，第一个成功就用它。
    ///     已经在同步时直接返回上一次的结果（不排队、不重复发请求）。
    /// </summary>
    public async Task<NtpSyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return LastResult ?? NtpSyncResult.Failure(string.Empty, "busy");

        try
        {
            IsSyncing = true;
            RaiseStatusChanged();

            var servers = Settings.ResolveServers();
            var timeout = Settings.ResolveTimeout();

            var result = NtpSyncResult.Failure(servers[0], "no server");
            foreach (var server in servers)
            {
                result = await _client.QueryAsync(server, timeout, cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                    break;

                if (cancellationToken.IsCancellationRequested)
                    break;
            }

            LastResult = result;
            if (result.Succeeded)
            {
                Offset = result.Offset;
                LastSuccessAt = result.SyncedAtUtc;
                _logger?.LogInformation("NTP synced via {Server}: offset {Offset} ms, round trip {RoundTrip} ms",
                    result.Server, result.Offset.TotalMilliseconds, result.RoundTrip.TotalMilliseconds);
            }
            else
            {
                _logger?.LogWarning("NTP sync failed: {Error}", result.Error);
            }

            return result;
        }
        finally
        {
            IsSyncing = false;
            _gate.Release();
            RaiseStatusChanged();
        }
    }

    /// <summary>
    ///     按设置把偏差摆正：校时关着就把偏差归零（退回系统时钟），开着则原样保留。
    ///     设置页改完开关、以及后台轮询每一轮都会调它（幂等）。
    /// </summary>
    public void RefreshEnabledState()
    {
        if (Settings.Enabled || Offset == TimeSpan.Zero)
            return;

        Offset = TimeSpan.Zero;
        RaiseStatusChanged();
    }

    /// <summary>
    ///     状态事件一律在 UI 线程上发。
    ///     <para>
    ///         ⚠️ <see cref="SyncNowAsync" /> 全程 <c>ConfigureAwait(false)</c>（不能在 UI 线程上等网络），
    ///         所以它的 <c>finally</c> 里的这一发**落在​线程池线程上**；订阅者拿到事件就改绑定属性、
    ///         调 <c>NotifyCanExecuteChanged</c>，跨线程直接抛
    ///         「The calling thread cannot access this object because a different thread owns it」，
    ///         而它是在 <c>finally</c> 里抛的，会顺着 <c>AsyncRelayCommand</c> 冒到 Dispatcher 变成未处理崩溃
    ///         （「立即同步」按钮点一下就可能崩）。后台轮询那条路（<see cref="RefreshEnabledState" />）同理。
    ///     </para>
    ///     <para>
    ///         放在这里而不是各个订阅者里：订阅者有三个（时间同步页、自动放映页、全屏看板），
    ///         以后再挂新的还会踩同一个坑。事件是「给界面看的状态」，线程口径就该由服务自己保证。
    ///     </para>
    /// </summary>
    private void RaiseStatusChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        Dispatcher.UIThread.Post(() => StatusChanged?.Invoke(this, EventArgs.Empty));
    }
}
