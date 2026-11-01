using System.Diagnostics;
using System.Text.Json;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.CrashRecovery;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models;
using Corkboard.Services.Desktop;
using Corkboard.Shared;
using CRV = Corkboard.Langs.CrashRecovery.Resources;

namespace Corkboard.Services.CrashRecovery;

/// <summary>
///     崩溃恢复的运行期：遇到未处理异常时写崩溃报告、按配置决定重启还是弹窗口，
///     以及「新进程带着 <c>--crash-report</c> 起来」这条路要用到的重启 / 读报告 / 反馈入口。
///     全部照上游 SecRandom-C 的 <c>SecRandom/Services/CrashRecovery/CrashRecoveryRuntime.cs</c> 移植，
///     **不要再各写一套**：<c>App</c>（Dispatcher 异常）、<c>Corkboard.Desktop/Program.cs</c>（AppDomain 异常与启动期异常）
///     与崩溃窗口三处都走这里。
/// </summary>
public static class CrashRecoveryRuntime
{
    /// <summary>崩溃报告超过这个大小就不往窗口里读了（防止把 GB 级日志读进内存）。</summary>
    public const int MaxCrashReportBytes = 1024 * 1024;

    /// <summary>「反馈问题」打开的仓库地址（键跟着仓库走，换仓库要一起改）。</summary>
    public const string FeedbackUrl = "https://github.com/SECTL/Corkboard/issues/new";

    private const string DesktopAssemblyFileName = "Corkboard.Desktop.dll";

    private const string DesktopExecutableFileName = "Corkboard.Desktop.exe";

    /// <summary>崩溃报告的正文里最多带多少字（GitHub 的 URL 装不下更多）。</summary>
    private const int MaxFeedbackReportLength = 6000;

    private static int _fatalExceptionHandled;

    /// <summary>
    ///     本次进程是「上一次崩溃后重启起来的」时，命令行里带的提示选项；正常启动是 <c>null</c>。
    ///     由 <c>Corkboard.Desktop/Program.cs</c> 在一切之前用 <see cref="SetStartupArguments" /> 填好。
    /// </summary>
    public static CrashRecoveryPromptOptions? StartupPromptOptions { get; private set; }

    private static string DesktopExecutableFileNameForCurrentPlatform =>
        OperatingSystem.IsWindows() ? DesktopExecutableFileName : "Corkboard.Desktop";

    /// <summary>记下命令行里的崩溃恢复参数（只留报告路径与自动重启标记，其余仍交给 Avalonia）。</summary>
    public static void SetStartupArguments(IReadOnlyList<string>? args)
    {
        StartupPromptOptions = CrashRecoveryPromptOptions.Parse(args);
    }

    /// <summary>只给单测用：把「这一次的致命异常已经处理过」的标志放回去。</summary>
    public static void ResetFatalExceptionHandledForTests()
    {
        Interlocked.Exchange(ref _fatalExceptionHandled, 0);
        StartupPromptOptions = null;
    }

    /// <summary>崩溃报告默认落在哪：数据目录下的 <c>crashes</c>，其次临时目录（数据目录可能不可写）。</summary>
    public static IReadOnlyList<string> DefaultCrashReportDirectories =>
    [
        Utils.GetDirectoryPath("crashes"),
        Path.Combine(Path.GetTempPath(), "Corkboard", "crashes")
    ];

    /// <summary>自动重启退避状态文件（临时目录，跨进程共享）。</summary>
    public static string DefaultRestartGuardPath =>
        Path.Combine(Path.GetTempPath(), "Corkboard", "crash-restart-guard.json");

    /// <summary>
    ///     运行期崩溃：算出一份提示选项（报告已经写好）。返回 <c>null</c> 表示这一档不该弹窗口
    ///     （配置是「不处理 / 直接重启」，或者本次进程本来就是带着启动提示起来的）。
    /// </summary>
    public static CrashRecoveryPromptOptions? TryCreateCurrentProcessPromptOptions(Exception exception)
    {
        try
        {
            // 已经是「崩溃后重启起来、正在显示提示」的那个进程：别再套一层。
            if (StartupPromptOptions is not null)
                return null;

            var mode = LoadConfiguredMode();
            if (mode is CrashRecoveryMode.None or CrashRecoveryMode.RestartOnly)
                return null;

            var autoRestart = mode == CrashRecoveryMode.PromptAndRestart &&
                              CrashRecoveryRestartGuard.CanBeginAutomaticRestart(DefaultRestartGuardPath,
                                  DateTimeOffset.Now);
            return new CrashRecoveryPromptOptions(TryWriteCrashReport(exception) ?? string.Empty, autoRestart);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     致命异常的最后一条路（弹窗口都失败了、或者压根没有 UI 线程时）。返回 <c>true</c> 表示已经处理，
    ///     调用方应当请求退出；返回 <c>false</c> 就让异常继续往上抛。
    /// </summary>
    /// <param name="exception">抛出来的异常。</param>
    /// <param name="startLaunchPlan">
    ///     真正去拉起新进程（<c>Corkboard.Desktop/Program.cs</c> 传 <see cref="StartLaunchPlan" />，App 传自己的重启入口）。
    ///     为 <c>null</c> 时不会启动任何进程。
    /// </param>
    public static bool TryHandleFatalException(Exception exception, Func<IReadOnlyList<string>?, bool>? startLaunchPlan = null)
    {
        try
        {
            var mode = LoadConfiguredMode();
            if (mode == CrashRecoveryMode.None)
                return false;

            // 一次崩溃只处理一次：同一条异常可能在 Dispatcher 与 AppDomain 两条路上都冒出来。
            if (Interlocked.Exchange(ref _fatalExceptionHandled, 1) != 0)
                return true;

            var now = DateTimeOffset.Now;
            var allowAutomaticRestart = mode == CrashRecoveryMode.PromptAndRestart
                ? CrashRecoveryRestartGuard.CanBeginAutomaticRestart(DefaultRestartGuardPath, now)
                : CrashRecoveryRestartGuard.TryBeginAutomaticRestart(DefaultRestartGuardPath, now);

            var crashReportPath = mode == CrashRecoveryMode.RestartOnly
                ? string.Empty
                : TryWriteCrashReport(exception) ?? string.Empty;

            var launchPlan = CrashRecoveryLaunchPlan.Create(mode, crashReportPath, allowAutomaticRestart);
            if (launchPlan is null && mode == CrashRecoveryMode.RestartOnly)
            {
                // 「直接重启」这一档退不出来：报告补一份，改成带提示的重启，至少让用户知道发生了什么。
                launchPlan = CrashRecoveryLaunchPlan.Create(CrashRecoveryMode.PromptThenRestart,
                    crashReportPath.Length > 0 ? crashReportPath : TryWriteCrashReport(exception) ?? string.Empty);
            }

            if (launchPlan is null || startLaunchPlan is null)
                return false;

            if (startLaunchPlan(launchPlan))
                return true;

            Interlocked.Exchange(ref _fatalExceptionHandled, 0);
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>崩溃窗口都没能显示出来时的兜底：报告已经写过，直接按「只重启」走一次。</summary>
    public static bool TryHandlePromptDisplayFailure(Exception exception, Func<IReadOnlyList<string>?, bool>? startLaunchPlan)
    {
        try
        {
            var crashReportPath = TryWriteCrashReport(exception) ?? string.Empty;
            var allowAutomaticRestart =
                CrashRecoveryRestartGuard.TryBeginAutomaticRestart(DefaultRestartGuardPath, DateTimeOffset.Now);
            var launchPlan = CrashRecoveryLaunchPlan.Create(CrashRecoveryMode.RestartOnly, crashReportPath,
                allowAutomaticRestart);
            return launchPlan is not null && startLaunchPlan is not null && startLaunchPlan(launchPlan);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    ///     读崩溃报告给窗口显示。路径必须落在允许的根目录下（防止被命令行引到别处的任意文件），
    ///     且不超过 <see cref="MaxCrashReportBytes" />；读不出来返回一句可显示的说明，**不抛异常**。
    /// </summary>
    public static string ReadCrashReport(string? crashReportPath, IReadOnlyList<string>? allowedRoots = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(crashReportPath))
                return string.Format(CRV.ReportUnavailable, crashReportPath ?? string.Empty);

            var fullPath = Path.GetFullPath(crashReportPath);
            if (!IsAllowedCrashReportPath(fullPath, allowedRoots ?? DefaultCrashReportDirectories))
                return string.Format(CRV.ReportUnavailable, crashReportPath);

            var fileInfo = new FileInfo(fullPath);
            if (!fileInfo.Exists || fileInfo.Length > MaxCrashReportBytes)
                return string.Format(CRV.ReportUnavailable, crashReportPath);

            return File.ReadAllText(fullPath);
        }
        catch (Exception)
        {
            return string.Format(CRV.ReportUnavailable, crashReportPath ?? string.Empty);
        }
    }

    /// <summary>写一份崩溃报告，返回文件路径；一个目录都写不进去就返回 <c>null</c>。</summary>
    public static string? TryWriteCrashReport(Exception exception, IReadOnlyList<string>? directories = null)
    {
        try
        {
            var content = CreateCrashReport(exception);
            var fileName = $"crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.txt";
            foreach (var directory in directories ?? DefaultCrashReportDirectories)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, fileName);
                    File.WriteAllText(path, content);
                    return path;
                }
                catch (Exception)
                {
                    // 换下一个目录。
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     拼出「重启本程序」要用的启动信息（按可行性排序，调用方逐个试）。
    ///     单元测试与冒烟都靠它，**不要在别处另拼一套命令行**。
    /// </summary>
    public static IReadOnlyList<ProcessStartInfo> CreateRestartStartInfos(IReadOnlyList<string>? arguments)
    {
        var startInfos = new List<ProcessStartInfo>();
        var baseDirectory = AppContext.BaseDirectory;

        // 1) 当前进程自己就是一个 apphost（发布版）——直接复制它。
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            !string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase) &&
            !processPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            AddStartInfo(startInfos, processPath, baseDirectory, arguments);

        // 2) 启动目录里的 apphost（aspnet/dotnet run 时 Environment.ProcessPath 可能是 dotnet）。
        var appHostPath = Path.Combine(baseDirectory, DesktopExecutableFileNameForCurrentPlatform);
        if (File.Exists(appHostPath))
            AddStartInfo(startInfos, appHostPath, baseDirectory, arguments);

        // 3) 最后一条路：dotnet Corkboard.Desktop.dll。
        var assemblyPath = Path.Combine(baseDirectory, DesktopAssemblyFileName);
        if (File.Exists(assemblyPath))
            AddStartInfo(startInfos, "dotnet", baseDirectory, arguments, assemblyPath);

        return startInfos;
    }

    /// <summary>
    ///     真正去拉起新进程：先让出单实例（<see cref="SingleInstanceGate.Release" />），
    ///     否则新进程会把命令行转发给这个正在退出的旧进程、然后自己退出。
    ///     启动成功后**不在这里请求退出**——由调用方（Dispatcher 那条路）决定什么时候关掉旧进程。
    /// </summary>
    public static bool StartLaunchPlan(IReadOnlyList<string>? arguments)
    {
        SingleInstanceGate.Release();
        return TryRestartCurrentApp(CreateRestartStartInfos(arguments), static () => { });
    }

    /// <summary>逐个试启动；成功一个就调用 <paramref name="requestShutdown" /> 并返回 <c>true</c>。</summary>
    public static bool TryRestartCurrentApp(IReadOnlyList<ProcessStartInfo> startInfos, Action requestShutdown)
    {
        foreach (var startInfo in startInfos)
        {
            try
            {
                if (Process.Start(startInfo) is null)
                    continue;

                requestShutdown();
                return true;
            }
            catch (Exception)
            {
                // 换下一个候选。
            }
        }

        return false;
    }

    /// <summary>把崩溃报告带到 GitHub 新建 issue 的页面上（交给系统浏览器打开）。</summary>
    public static bool OpenFeedbackIssue(string? crashReport)
    {
        try
        {
            var report = crashReport ?? string.Empty;
            if (report.Length > MaxFeedbackReportLength)
                report = report[..MaxFeedbackReportLength];

            var body = string.Format(CRV.FeedbackBody, report);
            var url =
                $"{FeedbackUrl}?title={Uri.EscapeDataString(CRV.FeedbackTitle)}&body={Uri.EscapeDataString(body)}";

            // Windows 上 UseShellExecute 能把 URL 直接交给默认浏览器；Linux / macOS 走各自的开命令。
            var startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo { FileName = url, UseShellExecute = true }
                : new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsMacOS() ? "open" : "xdg-open",
                    UseShellExecute = false
                };
            if (!OperatingSystem.IsWindows())
                startInfo.ArgumentList.Add(url);

            return Process.Start(startInfo) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    ///     直接读 settings.json 拿崩溃恢复模式（不经过 Host / 配置服务）：崩溃可能发生在配置服务建好之前，
    ///     也可能发生在它已经被拆掉之后。读不出来就用默认模型的值。
    /// </summary>
    public static CrashRecoveryMode LoadConfiguredMode()
    {
        try
        {
            var defaultMode = new MainConfigModel().General.CrashRecovery.Mode;
            var configFilePath = new MainConfigModel().ConfigFilePath;
            if (!File.Exists(configFilePath))
                return defaultMode;

            var json = File.ReadAllText(configFilePath);
            var model = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);
            return model?.General?.CrashRecovery?.Mode ?? defaultMode;
        }
        catch (Exception)
        {
            return CrashRecoveryMode.PromptAndRestart;
        }
    }

    private static void AddStartInfo(List<ProcessStartInfo> startInfos, string fileName, string workingDirectory,
        IReadOnlyList<string>? arguments, string? leadingArgument = null)
    {
        foreach (var existing in startInfos)
        {
            if (!string.Equals(existing.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (existing.ArgumentList.Count == (arguments?.Count ?? 0) + (leadingArgument is null ? 0 : 1))
                return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        if (leadingArgument is not null)
            startInfo.ArgumentList.Add(leadingArgument);
        if (arguments is not null)
        {
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
        }

        startInfos.Add(startInfo);
    }

    private static string CreateCrashReport(Exception exception)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("Corkboard Crash Report");
        builder.AppendLine($"Time: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Version: {GlobalConstants.VersionLong}");
        builder.AppendLine($"Process: {Environment.ProcessId}");
        builder.AppendLine();
        AppendException(builder, exception, 0);
        return builder.ToString();
    }

    private static void AppendException(System.Text.StringBuilder builder, Exception? exception, int depth)
    {
        while (exception is not null)
        {
            builder.AppendLine($"Exception {depth}: {exception.GetType().FullName}");
            builder.AppendLine($"Message: {exception.Message}");
            builder.AppendLine(string.IsNullOrWhiteSpace(exception.StackTrace)
                ? CRV.NoStackTrace
                : exception.StackTrace);
            builder.AppendLine();

            exception = exception.InnerException;
            depth++;
        }
    }

    private static bool IsAllowedCrashReportPath(string fullPath, IReadOnlyList<string> allowedRoots)
    {
        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            string rootPath;
            try
            {
                rootPath = Path.GetFullPath(root);
            }
            catch (Exception)
            {
                continue;
            }

            if (!rootPath.EndsWith(Path.DirectorySeparatorChar))
                rootPath += Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
