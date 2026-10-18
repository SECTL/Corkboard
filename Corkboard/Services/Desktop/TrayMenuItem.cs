namespace Corkboard.Services.Desktop;

/// <summary>
///     托盘菜单里的一项：<paramref name="Header" /> 是文案，<paramref name="Action" /> 是点下去要做的事。
/// </summary>
/// <remarks>
///     <para>
///         托盘菜单要出两份：右键走平台原生菜单（<c>NativeMenu</c> / <c>NativeMenuItem</c>），
///         左键走我们自己画的菜单（Windows 的原生托盘不给左键菜单，见
///         <c>PlatformCapabilities.SupportsTrayMenuOnLeftClick</c>）。两份必须是<b>同一份定义</b>，
///         否则迟早出现「左键少一项 / 右键文案对不上」——所以先列成这张表，再分别映射过去。
///     </para>
///     <para>
///         <see cref="Action" /> 为空表示这是一条分隔线。
///     </para>
/// </remarks>
/// <param name="Header">菜单项文案；分隔线为 <see langword="null" />。</param>
/// <param name="Action">点下去要做的事；分隔线为 <see langword="null" />。</param>
public sealed record TrayMenuItem(string? Header, Action? Action)
{
    /// <summary>一条分隔线。</summary>
    public static TrayMenuItem Separator { get; } = new(null, null);

    /// <summary>这一项是不是分隔线。</summary>
    public bool IsSeparator => Action is null;
}
