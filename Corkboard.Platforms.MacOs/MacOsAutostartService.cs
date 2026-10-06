using System.Runtime.Versioning;
using System.Security;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms.MacOs;

/// <summary>
///     macOS 开机自启：往 <c>~/Library/LaunchAgents</c> 写一份用户 LaunchAgent 并交给
///     <c>launchctl</c> 加载。用户级，不需要管理员权限。
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOsAutostartService : IAutostartService
{
    private const string Label = "cn.sectl.corkboard";
    private const string ApplicationName = "Corkboard";
    private static readonly TimeSpan LaunchctlTimeout = TimeSpan.FromSeconds(5);

    public bool IsSupported => OperatingSystem.IsMacOS();

    public bool TrySetEnabled(bool enabled, out string? error)
    {
        var path = Path.Combine(ResolveLaunchAgentsDirectory(), $"{Label}.plist");
        try
        {
            if (!enabled)
            {
                if (File.Exists(path) && !PlatformLaunchCommand.TryRun("launchctl", ["unload", path], LaunchctlTimeout))
                    throw new InvalidOperationException("launchctl 无法卸载用户 LaunchAgent");

                if (File.Exists(path))
                    File.Delete(path);
                error = null;
                return true;
            }

            var command = PlatformLaunchCommand.Resolve();
            if (command is null)
                throw new InvalidOperationException("无法解析当前进程的可执行文件路径");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, CreateLaunchAgentPlist(command.Value.FileName, command.Value.Arguments));
            if (!PlatformLaunchCommand.TryRun("launchctl", ["load", path], LaunchctlTimeout))
            {
                File.Delete(path);
                throw new InvalidOperationException("launchctl 无法加载用户 LaunchAgent");
            }

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

    private static string CreateLaunchAgentPlist(string fileName, IReadOnlyList<string> arguments)
    {
        var argumentXml = string.Concat(
            new[] { fileName }.Concat(arguments).Select(argument => $"<string>{SecurityElement.Escape(argument)}</string>"));

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
               + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
               + "<plist version=\"1.0\"><dict>"
               + $"<key>Label</key><string>{Label}</string>"
               + $"<key>ProgramArguments</key><array>{argumentXml}</array>"
               + "<key>RunAtLoad</key><true/>"
               + $"<key>ProcessType</key><string>Interactive</string>"
               + $"</dict></plist>\n"
               + $"<!-- {ApplicationName} -->\n";
    }

    private static string ResolveLaunchAgentsDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents");
    }

    private static void TryDelete(string path)
    {
        try
        {
            PlatformLaunchCommand.TryRun("launchctl", ["unload", path], LaunchctlTimeout);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 原始失败原因对调用方更有价值，清理保持尽力而为。
        }
    }
}
