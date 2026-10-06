using System.Diagnostics;

namespace Corkboard.Platforms;

/// <summary>
///     解析「重新启动本应用」要执行的命令行。开机自启项（注册表 / 桌面项 / LaunchAgent）
///     必须记住与当前进程一致的启动方式，因此统一从这里取，不在各平台实现里各写一遍。
/// </summary>
internal static class PlatformLaunchCommand
{
    /// <summary>当前可执行文件与其参数；拿不到可执行文件路径时返回 <see langword="null" />。</summary>
    public static (string FileName, IReadOnlyList<string> Arguments)? Resolve()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return null;

        return (executable, []);
    }

    /// <summary>Windows 命令行：空格的参数用双引号包起来（自启项的路径几乎总是含空格）。</summary>
    public static string ToWindowsCommandLine(string fileName, IReadOnlyList<string> arguments)
    {
        return string.Join(' ', new[] { QuoteWindows(fileName) }.Concat(arguments.Select(QuoteWindows)));
    }

    /// <summary>freedesktop <c>Exec=</c> 的值：路径含空格时用双引号包起来。</summary>
    public static string ToDesktopExec(string fileName, IReadOnlyList<string> arguments)
    {
        return string.Join(' ', new[] { QuoteDesktop(fileName) }.Concat(arguments.Select(QuoteDesktop)));
    }

    private static string QuoteWindows(string value)
    {
        if (value.Length > 0 && value.All(character => !char.IsWhiteSpace(character) && character != '"'))
            return value;

        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static string QuoteDesktop(string value)
    {
        return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
    }

    /// <summary>跑一条外部命令并等待结束，超时后终止子进程（见 Platforms.Abstractions/AGENTS.md）。</summary>
    public static bool TryRun(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return PlatformProcessRunner.TryGetOutput(startInfo, timeout, out _);
    }
}
