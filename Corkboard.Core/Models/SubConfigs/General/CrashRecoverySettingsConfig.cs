using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Models.SubConfigs.General;

/// <summary>
///     崩溃恢复设置：遇到未处理异常时是直接重启、先弹窗口还是什么都不做。挂在 <c>general.crash_recovery</c> 下，
///     由 <c>MainConfigModel.NormalizeLoaded</c> 在反序列化之后统一夹取。
///     ⚠️ 这份配置在「程序已经崩了」的那条路上会被 <c>CrashRecoveryRuntime</c> 绕过 Host 直接读盘，
///     所以不要给它加构造函数依赖，也不要挪到别处。
/// </summary>
public partial class CrashRecoverySettingsConfig : ObservableObject
{
    /// <summary>
    ///     崩溃恢复模式，默认 <see cref="CrashRecoveryMode.PromptAndRestart" />（弹窗口 + 倒计时自动重启）。
    /// </summary>
    [ObservableProperty] private CrashRecoveryMode _mode = CrashRecoveryMode.PromptAndRestart;

    /// <summary>读盘后夹取非法枚举值。不要挪进设置页：崩溃处理发生在任何页面之前。</summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(Mode))
            Mode = CrashRecoveryMode.PromptAndRestart;
    }
}
