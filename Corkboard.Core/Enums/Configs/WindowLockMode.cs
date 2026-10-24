namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     标题栏上那颗锁定按钮点下去之后做什么。
///     <para>
///         两种「锁」的用户预期不一样：有人要的是<b>别再误拖窗口</b>（位置钉死、照样点得动），
///         有人要的是<b>别挡住后面的东西</b>（点不到、鼠标穿过去）。所以让用户自己选。
///     </para>
///     <para>
///         ⚠️ 枚举按数字落盘（见 <c>ConfigServiceBase.JsonOptions</c>），
///         成员只能在末尾追加，不能调顺序、不能改名。
///     </para>
/// </summary>
public enum WindowLockMode
{
    /// <summary>锁定 = 点击穿透（默认，保持改动之前那颗按钮的老行为）。</summary>
    ClickThrough = 0,

    /// <summary>锁定 = 固定窗口：位置与大小都锁住，但里面的内容照常点得动。</summary>
    FixedPosition = 1
}
