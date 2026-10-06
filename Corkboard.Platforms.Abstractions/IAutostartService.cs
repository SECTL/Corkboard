namespace Corkboard.Platforms.Abstractions;

/// <summary>
///     开机自启契约：把「登录时自动启动本应用」这件事交给具体平台实现，
///     应用层只调用这个接口，不接触注册表、XDG 桌面项或 launchd。
/// </summary>
/// <remarks>
///     <para>
///         各平台都用<b>用户级</b>机制（Windows 的 HKCU Run 项、Linux 的
///         <c>~/.config/autostart</c>、macOS 的用户 LaunchAgent），不需要管理员权限，
///         也不改动系统级配置。
///     </para>
///     <para>
///         开启失败时实现负责把已经写下的痕迹清理掉，并把原因通过 <c>error</c> 返回；
///         调用方据此把设置回滚，绝不把失败当成成功落盘。
///     </para>
/// </remarks>
public interface IAutostartService
{
    /// <summary>当前平台是否实现了开机自启。</summary>
    bool IsSupported { get; }

    /// <summary>写入或移除登录自启项。失败时返回 <see langword="false" /> 并给出原因。</summary>
    bool TrySetEnabled(bool enabled, out string? error);
}
