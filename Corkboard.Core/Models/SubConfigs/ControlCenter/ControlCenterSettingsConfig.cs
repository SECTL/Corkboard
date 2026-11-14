using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.ControlCenter;

namespace Corkboard.Core.Models.SubConfigs.ControlCenter;

/// <summary>
///     集控（远程控制）客户端的设置。
///     <para>
///         <b>安全边界（必须守住）：</b>
///     </para>
///     <list type="bullet">
///         <item>
///             导入 <c>settings.json</c> <b>不能悄悄打开远程控制</b>——<see cref="Enabled" /> 只在
///             用户自己在设置页里打开时为真，导入流程不许把它改成 <c>true</c>。
///         </item>
///         <item>
///             设备凭据（device_id + secret）<b>不进 settings.json</b>：它们在数据根下单独的
///             AES-GCM 加密文件里（<c>data/config/security/control-node.json</c>），
///             设置文件会被导出、被云同步、被用户随手转贴。
///         </item>
///         <item>
///             <see cref="PairCode" /> 是<b>一次性</b>的：接入成功后必须立刻写回空串，
///             配对码留在配置里等于把接入权一直挂在硬盘上。
///         </item>
///     </list>
/// </summary>
public partial class ControlCenterSettingsConfig : ObservableObject
{
    /// <summary>站点默认地址。</summary>
    public const string DefaultServerUrl = "https://sectl.cn";

    /// <summary>是否启用集控（默认关；导入设置不许替用户打开）。</summary>
    [ObservableProperty] private bool _enabled;

    /// <summary>
    ///     作业自动备份（默认开）：作业板有变化就把只读快照传到集控，老师那边能看到历史备份。
    ///     <para>
    ///         它<b>不</b>等于远程控制：只是把板子的一份只读副本朝外传，服务端没法借此改本机任何东西，
    ///         所以默认值是开——但导入设置时仍然<b>不要</b>替用户强制打开或关闭它，
    ///         缺键时保留这里的默认值就够了。
    ///     </para>
    /// </summary>
    [ObservableProperty] private bool _homeworkBackupEnabled = true;

    /// <summary>集控中心地址（缺 <c>https://</c> 时由 <see cref="Normalize" /> 补上）。</summary>
    [ObservableProperty] private string _serverUrl = DefaultServerUrl;

    /// <summary>这台设备在网页上显示的名字（空 = 用主机名）。</summary>
    [ObservableProperty] private string _displayName = string.Empty;

    /// <summary>地点 id（<c>SC-XXXXXX</c>）；接入成功后写回归一化后的值。</summary>
    [ObservableProperty] private string _siteId = string.Empty;

    /// <summary>一次性配对码（<c>ABCD-7KQ2</c>）；接入成功后必须写回空串。</summary>
    [ObservableProperty] private string _pairCode = string.Empty;

    /// <summary>本机当前占着的班（由选班 / 释放班写回，重启后据此自动重占）。</summary>
    [ObservableProperty] private string _selectedClassId = string.Empty;

    /// <summary>
    ///     读盘后收敛：地址缺协议补 <c>https://</c> 并去掉尾部的 <c>/</c>，地点 id 归一成
    ///     <c>SC-XXXXXX</c>。由 <c>MainConfigModel.NormalizeLoaded()</c> 统一调用。
    /// </summary>
    public void Normalize()
    {
        ServerUrl = NormalizeServerUrl(ServerUrl);
        SiteId = NormalizeSiteId(SiteId);
    }

    /// <summary>地址归一：空 → 默认值；没协议 → 补 <c>https://</c>；去掉尾部斜杠。</summary>
    public static string NormalizeServerUrl(string? value)
    {
        var url = value?.Trim();
        if (string.IsNullOrEmpty(url))
            return DefaultServerUrl;

        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;

        return url.TrimEnd('/');
    }

    /// <summary>地点 id 归一；认不出来时只做大写与去空白（不把用户输入整段丢掉）。</summary>
    public static string NormalizeSiteId(string? value)
    {
        var normalized = NodeEnrollment.NormalizeSiteId(value);
        if (normalized is not null)
            return normalized;

        return value?.Trim().ToUpperInvariant() ?? string.Empty;
    }
}
