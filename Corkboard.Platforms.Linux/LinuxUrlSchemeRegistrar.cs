using System.Runtime.Versioning;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms.Linux;

/// <summary>
///     Linux 自定义 URL 协议：在 <c>~/.local/share/applications</c> 下写一份
///     <c>x-scheme-handler/&lt;scheme&gt;</c> 桌面项，再用 <c>xdg-mime default</c> 把它设为该协议的默认处理程序。
///     用户级，不需要 root。
/// </summary>
/// <remarks>
///     <para>
///         freedesktop 把「哪个程序处理 <c>corkboard://</c>」拆成两件事：桌面项自己声明
///         <c>MimeType=x-scheme-handler/&lt;scheme&gt;;</c>，默认关联则由
///         <c>mimeapps.list</c> 记录。前者我们直接写文件，后者只能交给 <c>xdg-mime</c>，
///         自己拼 <c>mimeapps.list</c> 会在各发行版的不同位置（<c>~/.config</c> /
///         <c>~/.local/share/applications</c>）上出错。
///     </para>
///     <para>
///         <c>Exec=</c> 里放的是 <c>%u</c> 而不是 <c>%1</c>：freedesktop 的占位符只有
///         <c>%f</c>/<c>%F</c>/<c>%u</c>/<c>%U</c> 这几种，单个 URL 用 <c>%u</c>。
///     </para>
///     <para>
///         外部命令一律走 <see cref="PlatformLaunchCommand.TryRun" />（有超时、会终止子进程），
///         见 <c>Platforms.Abstractions/AGENTS.md</c> 对「有界输出」的要求。
///     </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxUrlSchemeRegistrar : IUrlSchemeRegistrar
{
    /// <summary>桌面项显示名。平台工程不依赖 Core，所以这里写死再与 <c>GlobalConstants.AppName</c> 对齐。</summary>
    private const string ApplicationName = "Corkboard";

    private static readonly TimeSpan XdgMimeTimeout = TimeSpan.FromSeconds(10);

    public bool IsSupported => OperatingSystem.IsLinux();

    public bool TrySetEnabled(string scheme, bool enabled, out string? error)
    {
        if (!IsValidScheme(scheme, out error))
            return false;

        var path = Path.Combine(ResolveApplicationsHome(), scheme + "-url-handler.desktop");
        try
        {
            if (!enabled)
            {
                // 解不掉 mimeapps.list 里那条默认关联（xdg-mime 没有 unset），
                // 但桌面项一没了这条关联就指向空，所以只删文件即可，按成功返回。
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
                $"Exec={PlatformLaunchCommand.ToDesktopExec(command.Value.FileName, ["%u"])}",
                $"MimeType=x-scheme-handler/{scheme};",
                "NoDisplay=true",
                "Terminal=false"
            ]) + '\n');

            // 桌面项写好了，再把默认关联指过去；这一步失败就整条回滚，不留半套注册。
            if (!PlatformLaunchCommand.TryRun("xdg-mime",
                    ["default", Path.GetFileName(path), "x-scheme-handler/" + scheme],
                    XdgMimeTimeout))
                throw new InvalidOperationException("xdg-mime 设置默认处理程序失败（可能未安装 xdg-utils）");

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            // 开启失败时清掉半成品，免得系统把 URL 交给一份不完整的桌面项。
            if (enabled)
                TryDelete(path);
            error = exception.Message;
            return false;
        }
    }

    private static string ResolveApplicationsHome()
    {
        // 与 XDG_DATA_HOME 的默认值一致：没设变量时就是 ~/.local/share。
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(dataHome))
            return Path.Combine(dataHome, "applications");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");
    }

    /// <summary>
    ///     协议名只允许字母、数字与 <c>+</c> <c>-</c> <c>.</c>（RFC 3986 的 scheme 文法）。
    ///     名字会拼进文件名，非法输入直接拒绝，不要让它抛到调用方。
    /// </summary>
    private static bool IsValidScheme(string scheme, out string? error)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            error = "协议名不能为空。";
            return false;
        }

        if (!scheme.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '.'))
        {
            error = $"协议名 “{scheme}” 只能包含字母、数字与 + - . 这四个符号。";
            return false;
        }

        error = null;
        return true;
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
