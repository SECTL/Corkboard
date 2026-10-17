using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Shared.Updates;

namespace Corkboard.Core.Models.SubConfigs.Update;

/// <summary>
///     自动更新设置：检查策略、更新通道与上次检查时间。
/// </summary>
/// <remarks>
///     从上游 SecRandom-C 的 <c>UpdateSettingsConfig</c> 搬来，JSON 键名沿用 Corkboard 的
///     snake_case 规则（<c>auto_update_mode</c> / <c>update_mode_version</c> / <c>update_channel</c> /
///     <c>last_check_time</c>）。
///     <para>
///         <see cref="AutoUpdateMode" /> 是<b>数字</b>而不是枚举：0 关闭、1 只通知、2 通知并下载、
///         3 通知并安装（上游同一套取值，升级时不用改界面）。
///     </para>
/// </remarks>
public partial class UpdateSettingsConfig : ObservableObject, IJsonOnDeserialized
{
    /// <summary>历史上「没检查过」被写成 1970-01-01，读到它等于没检查过。</summary>
    private static readonly DateOnly LegacyUnsetLastCheckDate = new(1970, 1, 1);

    [JsonIgnore] private bool _updateModeVersionLoaded;

    /// <summary>
    ///     自动更新模式：0 关闭、1 只通知、2 通知并下载、3 通知并安装。
    ///     默认 3（发现新版本就自动装），与上游一致；想安静点的用户在设置里往下调。
    /// </summary>
    [ObservableProperty] private int _autoUpdateMode = 3;

    /// <summary>
    ///     自动更新模式这套取值的版本号，目前只有 <c>1</c>。
    ///     <para>
    ///         它只用来判断「这份配置是按新模式写的吗」：旧版本里的非零值表示「定时检查」，
    ///         读盘时统一收敛成 1（只通知），不把用户悄悄升级成自动安装（见 <see cref="OnDeserialized" />）。
    ///     </para>
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private int _updateModeVersion = 1;

    /// <summary>更新通道：稳定版 / 测试版 / 开发版。清单里的通道与之对应。</summary>
    [ObservableProperty] private UpdateChannel _updateChannel = UpdateChannel.Release;

    /// <summary>最近一次检查更新的时间；没检查过为空。</summary>
    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private DateTime? _lastCheckTime;

    /// <summary>
    ///     JSON 里的 <c>update_mode_version</c>。
    /// </summary>
    /// <remarks>
    ///     单独开一个读写属性只为了<b>可靠地知道这个键在文件里存不存在</b>：如果直接靠
    ///     <see cref="UpdateModeVersion" /> 的 setter 记标志，值正好等于默认值 1 时生成代码不会调 setter，
    ///     标志永远立不起来，于是每次启动都会把用户选好的「自动下载 / 自动安装」重新压回「只通知」。
    ///     反序列化只要命中这个键就一定会走这里，旧文件（没有这个键）则不会，迁移判据因此是准的。
    /// </remarks>
    [JsonPropertyName("update_mode_version")]
    public int SerializedUpdateModeVersion
    {
        get => UpdateModeVersion;
        set
        {
            _updateModeVersionLoaded = true;
            UpdateModeVersion = value;
        }
    }

    partial void OnLastCheckTimeChanged(DateTime? value)
    {
        if (value.HasValue && DateOnly.FromDateTime(value.Value) == LegacyUnsetLastCheckDate)
            LastCheckTime = null;
    }

    /// <summary>
    ///     旧配置迁移：文件里没有 <c>update_mode_version</c> 说明是旧版本写的
    ///     （旧版本拿非零值表示「定时检查」），统一改成「只通知」并补上版本号。
    /// </summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        if (_updateModeVersionLoaded)
            return;

        // 旧的非零值原本表示「到点检查」，保住这个行为，但不替用户打开自动安装。
        AutoUpdateMode = AutoUpdateMode == 0 ? 0 : 1;
        UpdateModeVersion = 1;
    }

    /// <summary>
    ///     读盘后收拾一遍：手改过的脏数据（模式越界、通道编号不认识、1970 的上次检查时间）在这里夹回合法值。
    ///     由 <c>MainConfigModel.NormalizeLoaded</c> 调用，设置页打开之前就已经是干净的。
    /// </summary>
    public void Normalize()
    {
        AutoUpdateMode = Math.Clamp(AutoUpdateMode, 0, 3);
        if (!Enum.IsDefined(UpdateChannel))
            UpdateChannel = UpdateChannel.Release;

        if (LastCheckTime is { } lastCheck && DateOnly.FromDateTime(lastCheck) == LegacyUnsetLastCheckDate)
            LastCheckTime = null;
    }
}
