using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Attributes;

namespace Corkboard.Core.Models.SubConfigs.Cloud;

/// <summary>
///     云同步（思拓创联云存储）设置。
///     <para>
///         云同步**默认关闭**：每台设备各自决定，登录后不会自动打开；
///         这一整段也不会跟着同步走（标了 <see cref="LocalOnlyAttribute" />），
///         否则一台设备打开就会替所有设备打开、还没法单独关掉。
///     </para>
/// </summary>
[LocalOnly]
public partial class CloudSyncSettingsConfig : ObservableObject
{
    /// <summary>是否开启云同步。</summary>
    [ObservableProperty] private bool _enabled;

    /// <summary>同步作业板内容（<c>data/board/</c>）。</summary>
    [ObservableProperty] private bool _syncBoard = true;

    /// <summary>同步设置（窗口尺寸位置、开机自启等本机专属项除外）。</summary>
    [ObservableProperty] private bool _syncSettings = true;

    /// <summary>本机没有未同步的改动时，自动拉取云端的更新。</summary>
    [ObservableProperty] private bool _autoPull = true;

    /// <summary>
    ///     只把最近这么多天的作业传上云端（按作业的创建时间算，窗口是滚动的）。
    ///     <para>
    ///         默认 <c>0</c>：全部传，历史作业一份不落。设成 7 就只传最近一周，
    ///         窗口外那些老作业留在本机：不传上去，也不会被云端那份覆盖。
    ///     </para>
    /// </summary>
    [ObservableProperty] private int _syncDays;

    /// <summary>本机上一次成功同步的时间。</summary>
    [ObservableProperty] private DateTimeOffset? _lastSyncedAt;

    /// <summary>
    ///     上一次同步上去的设置内容摘要（排除本机专属项之后的 JSON 的 SHA-256）。
    ///     <para>
    ///         判断设置有没有改动只能靠内容比对：窗口位置这类**被排除**的项一移动就会写配置文件，
    ///         用文件写入时间当依据的话，界面永远显示「本机有改动」、再也不敢自动拉取云端备份。
    ///     </para>
    /// </summary>
    [ObservableProperty] private string? _lastSettingsHash;
}
