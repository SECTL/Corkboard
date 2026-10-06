using System.Runtime.Versioning;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms.Linux;

/// <summary>
///     Linux 开机自启：往 <c>~/.config/autostart</c> 写一份 freedesktop 桌面项。
///     遵循 <c>XDG_CONFIG_HOME</c>，没有该变量时退回 <c>~/.config</c>。
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxAutostartService : IAutostartService
{
    private const string DesktopFileName = "corkboard.desktop";

    /// <summary>桌面项显示名。平台工程不依赖 Core，所以这里写死再与 <c>GlobalConstants.AppName</c> 对齐。</summary>
    private const string ApplicationName = "Corkboard";

    public bool IsSupported => OperatingSystem.IsLinux();

    public bool TrySetEnabled(bool enabled, out string? error)
    {
        var path = Path.Combine(ResolveConfigHome(), "autostart", DesktopFileName);
        try
        {
            if (!enabled)
            {
                if (File.Exists(path))
                    File.Delete(path);
                error = null;
                return true;
            }

            var command = PlatformLaunchCommand.Resolve();
            if (command is null)
                throw new InvalidOperationException("无法解析当前进程的可执行文件路径");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join('\n',
            [
                "[Desktop Entry]",
                "Type=Application",
                $"Name={ApplicationName}",
                $"Exec={PlatformLaunchCommand.ToDesktopExec(command.Value.FileName, command.Value.Arguments)}",
                "X-GNOME-Autostart-enabled=true"
            ]) + '\n');

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            // 开启失败时清掉半成品，免得下次登录拿到一条坏命令。
            if (enabled)
                TryDelete(path);
            error = exception.Message;
            return false;
        }
    }

    private static string ResolveConfigHome()
    {
        return Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 原始失败原因对调用方更有价值，清理保持尽力而为。
        }
    }
}
