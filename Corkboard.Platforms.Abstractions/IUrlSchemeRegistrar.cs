namespace Corkboard.Platforms.Abstractions;

/// <summary>
///     自定义 URL 协议（URL scheme）注册契约：把「让系统把 <c>corkboard://…</c> 交回本应用」
///     这件事交给具体平台实现，应用层只调用这个接口，不接触注册表、freedesktop 桌面项或 Info.plist。
/// </summary>
/// <remarks>
///     <para>
///         协议名就是 <c>Corkboard.Core.GlobalConstants.UrlProtocolScheme</c> 的值（<c>"corkboard"</c>），
///         由调用方作为参数传进来——平台工程不依赖 Core，接口里不写死这个名字。
///     </para>
///     <para>
///         注册必须指向<b>当前进程的可执行文件</b>（含启动参数），否则系统会把 URL 交给另一份安装；
///         命令行的最后要带一个字面量参数 <c>"%1"</c>，由系统在唤起时替换成完整的 URL。
///     </para>
///     <para>
///         Windows 写 <c>HKCU\Software\Classes\&lt;scheme&gt;</c>、Linux 写
///         <c>~/.local/share/applications</c> 下的桌面项并调 <c>xdg-mime</c>，两者都是<b>用户级</b>机制，
///         不需要管理员权限。macOS 的协议必须声明在应用包的 <c>Info.plist</c> 里，
///         运行中的进程改不了自己的 Info.plist，所以那里报不支持。
///     </para>
///     <para>
///         失败时实现负责把已经写下的痕迹清理掉，并把原因通过 <c>error</c> 返回；
///         调用方据此把设置回滚，绝不把失败当成成功落盘。
///     </para>
/// </remarks>
public interface IUrlSchemeRegistrar
{
    /// <summary>当前平台是否支持在运行期注册自定义 URL 协议。</summary>
    bool IsSupported { get; }

    /// <summary>注册或注销某个自定义协议。失败时返回 <see langword="false" /> 并给出原因。</summary>
    bool TrySetEnabled(string scheme, bool enabled, out string? error);
}
