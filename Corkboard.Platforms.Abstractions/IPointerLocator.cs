namespace Corkboard.Platforms.Abstractions;

/// <summary>
///     指针定位契约：把「鼠标此刻在屏幕的哪个位置」这件事交给具体平台。
/// </summary>
/// <remarks>
///     <para>
///         应用层<b>拿不到全局指针位置</b>：Avalonia 的 <c>MouseDevice</c> 只报「指针在某个 TopLevel 里的
///         本地坐标」，而且那个坐标要等指针事件才会刷新——窗口没被指针划过时读到的是上一次的旧值。
///         托盘菜单要贴着鼠标弹出来（Windows 的托盘只把左键当 <c>Clicked</c>，菜单得我们自己画），
///         所以必须有平台那条能直接问系统的路。
///     </para>
///     <para>
///         没有实现（或实现不了）的平台返回 <see langword="false" />，调用方自己退回一个兜底位置，
///         <b>不要</b>因此把菜单藏起来——托盘菜单在哪个平台都得能用。
///     </para>
/// </remarks>
public interface IPointerLocator
{
    /// <summary>取当前指针的屏幕物理坐标。平台拿不到时返回 <see langword="false" />。</summary>
    bool TryGetCursorPosition(out ScreenPoint position);
}
