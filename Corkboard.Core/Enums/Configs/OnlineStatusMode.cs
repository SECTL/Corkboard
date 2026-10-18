namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     在线状态上报模式。枚举值直接按数字落盘（见 <c>ConfigServiceBase.JsonOptions</c>），
///     只能在末尾增删成员；读盘后由 <c>PrivacySettingsConfig.Normalize</c> 把未定义值收回 <see cref="Full" />。
/// </summary>
public enum OnlineStatusMode
{
    /// <summary>正常上报：只发平台标识、设备标识与设备类型。</summary>
    Full,

    /// <summary>匿名上报：与正常上报发送同一组字段，只是从隐私层面表明不参与更多统计。</summary>
    Anonymous,

    /// <summary>停止上报：客户端完全不上报在线状态，也不查在线人数。</summary>
    Off
}
