using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Time;

namespace Corkboard.Core.Models.SubConfigs.Exam;

/// <summary>
///     NTP 校时设置（<c>settings.json</c> 的 <c>ntp</c> 段）。
///     <para>
///         看板上的时钟要准到「还有几分钟开考」这种粒度，本机时间飘了就会误事，
///         所以时间走 NTP 同步；同步失败时自动退回本机时间，不影响看板使用。
///     </para>
/// </summary>
public partial class NtpSettingsConfig : ObservableObject
{
    /// <summary>是否用 NTP 校时。关掉就看本机时间（本机时间本来就准的话没必要开）。</summary>
    [ObservableProperty] private bool _enabled = true;

    /// <summary>
    ///     同步源（按先后顺序尝试，第一个成功的就用它）。默认是国内几个公共 NTP 服务，
    ///     读写都走 <see cref="ResolveServers" /> / <see cref="ApplyServers" />——
    ///     空表会被填回默认源，否则永远同步不上。
    /// </summary>
    public ObservableCollection<string> Servers { get; set; } = [];

    /// <summary>两次同步之间隔多少分钟。读的时候过一遍 <see cref="ClampIntervalMinutes" />。</summary>
    [ObservableProperty] private int _syncIntervalMinutes = DefaultIntervalMinutes;

    /// <summary>单个源等回应多久（毫秒）。读的时候过一遍 <see cref="ClampTimeoutMilliseconds" />。</summary>
    [ObservableProperty] private int _timeoutMilliseconds = DefaultTimeoutMilliseconds;

    /// <summary>默认同步间隔：半小时一次足够，NTP 请求本身很小。</summary>
    public const int DefaultIntervalMinutes = 30;

    public const int MinIntervalMinutes = 1;

    public const int MaxIntervalMinutes = 24 * 60;

    /// <summary>默认单源超时：3 秒。太短会在网络抖一下时白白失败，太长会把界面上的「立即同步」拖住。</summary>
    public const int DefaultTimeoutMilliseconds = 3000;

    public const int MinTimeoutMilliseconds = 500;

    public const int MaxTimeoutMilliseconds = 15000;

    public static int ClampIntervalMinutes(int minutes) =>
        Math.Clamp(minutes, MinIntervalMinutes, MaxIntervalMinutes);

    public static int ClampTimeoutMilliseconds(int milliseconds) =>
        Math.Clamp(milliseconds, MinTimeoutMilliseconds, MaxTimeoutMilliseconds);

    /// <summary>取收拾干净的同步源清单（去空白、去重、截断；空表回退默认源）。</summary>
    public List<string> ResolveServers() => NtpServerCatalog.Normalize(Servers);

    /// <summary>把清单原地换成给定内容（就地增删改，别换集合，否则断了设置页的绑定）。</summary>
    public void ApplyServers(IEnumerable<string?>? servers)
    {
        var normalized = NtpServerCatalog.Normalize(servers);

        Servers.Clear();
        foreach (var server in normalized)
            Servers.Add(server);
    }

    /// <summary>读盘后收拾一遍：间隔与超时夹进区间、源清单换成干净的（空表落回默认源）。</summary>
    public void Normalize()
    {
        SyncIntervalMinutes = ClampIntervalMinutes(SyncIntervalMinutes);
        TimeoutMilliseconds = ClampTimeoutMilliseconds(TimeoutMilliseconds);

        var normalized = NtpServerCatalog.Normalize(Servers);
        if (Servers.Count == normalized.Count && Servers.SequenceEqual(normalized))
            return;

        ApplyServers(normalized);
    }

    /// <summary>同步间隔对应的 <see cref="TimeSpan" />（已夹取）。</summary>
    public TimeSpan ResolveInterval() => TimeSpan.FromMinutes(ClampIntervalMinutes(SyncIntervalMinutes));

    /// <summary>单源超时对应的 <see cref="TimeSpan" />（已夹取）。</summary>
    public TimeSpan ResolveTimeout() => TimeSpan.FromMilliseconds(ClampTimeoutMilliseconds(TimeoutMilliseconds));
}
