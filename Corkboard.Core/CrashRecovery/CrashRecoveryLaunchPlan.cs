using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.CrashRecovery;

/// <summary>
///     按崩溃恢复模式算出「新进程该带哪些参数启动」。纯逻辑、无副作用（只返回参数表），便于单测。
/// </summary>
public static class CrashRecoveryLaunchPlan
{
    /// <summary>
    ///     生成重启参数。返回 <c>null</c> 表示这一档不重启（<see cref="CrashRecoveryMode.None" />，
    ///     或者 <see cref="CrashRecoveryMode.RestartOnly" /> 但两分钟内已经自动重启过一次）。
    ///     空数组表示直接重启、不弹窗口。
    /// </summary>
    /// <param name="mode">当前配置的崩溃恢复模式。</param>
    /// <param name="crashReportPath">已经写好的崩溃报告路径；<see cref="CrashRecoveryMode.RestartOnly" /> 可以是空串。</param>
    /// <param name="allowAutomaticRestart">是否允许这一次自动重启（退避守卫说了算）。</param>
    public static IReadOnlyList<string>? Create(CrashRecoveryMode mode, string crashReportPath, bool allowAutomaticRestart = true)
    {
        return mode switch
        {
            CrashRecoveryMode.None => null,
            CrashRecoveryMode.RestartOnly => allowAutomaticRestart ? [] : null,
            CrashRecoveryMode.PromptThenRestart => PromptArguments(crashReportPath, autoRestart: false),
            CrashRecoveryMode.PromptAndRestart => allowAutomaticRestart
                ? PromptArguments(crashReportPath, autoRestart: true)
                : PromptArguments(crashReportPath, autoRestart: false),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的崩溃恢复模式")
        };
    }

    private static string[] PromptArguments(string crashReportPath, bool autoRestart)
    {
        return autoRestart
            ?
            [
                CrashRecoveryPromptOptions.CrashReportArgument, crashReportPath,
                CrashRecoveryPromptOptions.CrashAutoRestartArgument
            ]
            : [CrashRecoveryPromptOptions.CrashReportArgument, crashReportPath];
    }
}
