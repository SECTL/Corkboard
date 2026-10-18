using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.Core.Models.SubConfigs.General;

/// <summary>通用设置分组。<c>MainConfigModel.Basic</c> 是它内部 <see cref="BasicSettingsConfig" /> 的转发属性。</summary>
public partial class GeneralSettingsConfig : ObservableObject
{
    [ObservableProperty] private BasicSettingsConfig _basic = new();

    /// <summary>隐私设置（在线状态上报）。单独一份子配置，不往基础设置里堆统计开关。</summary>
    [ObservableProperty] private PrivacySettingsConfig _privacySettings = new();
}
