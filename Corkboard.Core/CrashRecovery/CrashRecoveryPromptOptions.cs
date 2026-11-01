namespace Corkboard.Core.CrashRecovery;

/// <summary>
///     崩溃恢复窗口的启动参数：报告在哪、要不要倒计时自动重启。
///     由崩溃前的那个进程用命令行传给新进程（<c>--crash-report &lt;路径&gt;</c> / <c>--crash-auto-restart</c>），
///     新进程一启动就据此先把崩溃窗口画出来，正常启动路径根本不会跑。
/// </summary>
public sealed record CrashRecoveryPromptOptions(string CrashReportPath, bool AutoRestart)
{
    /// <summary>崩溃报告路径参数名。值在下一个 token 上。</summary>
    public const string CrashReportArgument = "--crash-report";

    /// <summary>「倒计时自动重启」开关参数名（纯开关，不带值）。</summary>
    public const string CrashAutoRestartArgument = "--crash-auto-restart";

    /// <summary>
    ///     从命令行里认出崩溃恢复参数。没看到 <see cref="CrashReportArgument" />（或它后面没有值）就返回 <c>null</c>——
    ///     那是正常启动。参数非法不抛异常：崩溃恢复的这条路上不能再因为解析崩一次。
    /// </summary>
    public static CrashRecoveryPromptOptions? Parse(IReadOnlyList<string>? args)
    {
        if (args is null)
            return null;

        string? crashReportPath = null;
        var autoRestart = false;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, CrashReportArgument, StringComparison.Ordinal))
            {
                if (index + 1 < args.Count)
                {
                    crashReportPath = args[index + 1];
                    index++;
                }

                continue;
            }

            if (string.Equals(argument, CrashAutoRestartArgument, StringComparison.Ordinal))
                autoRestart = true;
        }

        return string.IsNullOrWhiteSpace(crashReportPath)
            ? null
            : new CrashRecoveryPromptOptions(crashReportPath, autoRestart);
    }

    /// <summary>
    ///     把崩溃恢复参数从命令行里剔掉（连同 <see cref="CrashReportArgument" /> 的值），
    ///     交给 Avalonia 的那份参数就干净了。解析不了的一律原样保留。
    /// </summary>
    public static string[] RemoveCrashRecoveryArguments(IReadOnlyList<string>? args)
    {
        if (args is null)
            return [];

        var filtered = new List<string>(args.Count);
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, CrashReportArgument, StringComparison.Ordinal))
            {
                if (index + 1 < args.Count)
                    index++;

                continue;
            }

            if (string.Equals(argument, CrashAutoRestartArgument, StringComparison.Ordinal))
                continue;

            filtered.Add(argument);
        }

        return [.. filtered];
    }
}
