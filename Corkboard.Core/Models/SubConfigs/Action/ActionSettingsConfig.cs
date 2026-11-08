using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Models.SubConfigs.Action;

/// <summary>
///     行动窗口 / 日程中心的总开关（挂在 <see cref="MainConfigModel.ActionSettings" /> 上）。
///     <para>
///         关掉只停自动执行与日程视图，已存的规则与执行记录都不删 —— 用户重新打开时原样还在。
///     </para>
/// </summary>
public partial class ActionSettingsConfig : ObservableObject
{
    /// <summary>行动窗口总开关。默认开。</summary>
    [ObservableProperty] private bool _enabled = true;

    /// <summary>
    ///     读盘后的归一化。布尔没有越界值，目前只是兜底形状（保证 <c>NormalizeLoaded</c> 里每个子配置都过一遍
    ///     <c>Normalize()</c>，以后加数值项时不用改调用方）。
    /// </summary>
    public void Normalize()
    {
        // 暂时没有需要夹取的字段；保留方法让 Configured 管道与其它子配置一致。
    }
}
