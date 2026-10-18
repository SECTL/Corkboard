using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Models.SubConfigs.General;

/// <summary>
///     隐私设置：目前只有在线状态上报（SECTL 平台统计）。挂在 <c>general.privacy</c> 下，
///     由 <c>MainConfigModel.NormalizeLoaded</c> 在反序列化之后统一夹取。
/// </summary>
public partial class PrivacySettingsConfig : ObservableObject
{
    /// <summary>
    ///     在线状态上报模式，默认 <see cref="OnlineStatusMode.Full" />。
    ///     客户端只上报设备标识与设备类型，位置由服务端按访问地址统计；选 <see cref="OnlineStatusMode.Off" /> 则完全不上报。
    /// </summary>
    [ObservableProperty] private OnlineStatusMode _onlineStatusMode = OnlineStatusMode.Full;

    /// <summary>读盘后夹取非法枚举值。不要挪进设置页：后台服务在设置页打开之前就会读它。</summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(OnlineStatusMode))
            OnlineStatusMode = OnlineStatusMode.Full;
    }
}
