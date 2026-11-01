using Corkboard.Core.CrashRecovery;

namespace Corkboard.Services.CrashRecovery;

/// <summary>
///     Dispatcher 未处理异常的决策层：先看能不能弹崩溃窗口，弹不出来再退到「直接重启」，
///     两条都走不了就把异常还给运行时（<c>Handled = false</c>，程序照常崩掉）。
///     纯粹是流程编排，不碰 Avalonia，方便照上游原样对齐与单测。
/// </summary>
public static class DispatcherCrashRecovery
{
    /// <summary>只有「直接重启」这一条路时的简化版：处理成功才请求退出。</summary>
    public static bool TryRecover(Exception exception, Func<Exception, bool> handleFatalException,
        Action requestShutdown)
    {
        if (!handleFatalException(exception))
            return false;

        requestShutdown();
        return true;
    }

    /// <summary>
    ///     完整版。<paramref name="showPrompt" /> 显示成功就直接返回 <c>true</c>——**不请求退出**：
    ///     窗口还开着，用户点「重启」或「关闭」时由窗口自己请求退出（否则窗口刚弹出来程序就没了）。
    /// </summary>
    public static bool TryRecover(Exception exception,
        Func<Exception, CrashRecoveryPromptOptions?> createPromptOptions,
        Func<CrashRecoveryPromptOptions, bool> showPrompt,
        Func<Exception, bool> handlePromptDisplayFailure,
        Func<Exception, bool> handleFatalException,
        Action requestShutdown)
    {
        CrashRecoveryPromptOptions? promptOptions;
        try
        {
            promptOptions = createPromptOptions(exception);
        }
        catch (Exception)
        {
            promptOptions = null;
        }

        if (promptOptions is not null)
        {
            var shown = false;
            try
            {
                shown = showPrompt(promptOptions);
            }
            catch (Exception)
            {
                shown = false;
            }

            if (shown)
                return true;

            if (handlePromptDisplayFailure(exception))
            {
                requestShutdown();
                return true;
            }

            return false;
        }

        if (!handleFatalException(exception))
            return false;

        requestShutdown();
        return true;
    }
}
