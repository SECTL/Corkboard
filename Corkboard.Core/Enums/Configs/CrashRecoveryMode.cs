namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     崩溃恢复策略：程序遇到未处理异常时怎么处理。枚举值直接按数字落盘（见 <c>ConfigServiceBase.JsonOptions</c>），
///     只能在末尾增删成员；读盘后由 <c>CrashRecoverySettingsConfig.Normalize</c> 把未定义值收回
///     <see cref="PromptAndRestart" />。顺序与设置页下拉框一致，别调换。
/// </summary>
public enum CrashRecoveryMode
{
    /// <summary>不处理：异常照旧崩掉，不写报告、不重启。</summary>
    None,

    /// <summary>直接重启：写一份崩溃报告藏着，然后悄悄把程序拉起来（两分钟内只自动重启一次）。</summary>
    RestartOnly,

    /// <summary>先提示再重启：弹出崩溃窗口，用户看完再自己点重启（不自动倒计时）。</summary>
    PromptThenRestart,

    /// <summary>提示并重启：弹出崩溃窗口并倒计时 10 秒自动重启，用户可以点「忽略」留下来。</summary>
    PromptAndRestart
}
