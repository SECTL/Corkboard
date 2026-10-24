namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     窗口怎么拖（以及能不能拖）。
///     <para>
///         这是「设置 → 作业板 → 窗口与收纳」里那个下拉框的三个档位。它<b>不落盘</b>：
///         真正落盘的仍是 <c>BasicSettingsConfig.HoldToDragWindow</c> 与 <c>WindowFixed</c>
///         这两个老键（键名冻结，老配置读出来还是它原本的意思），两者的组合由
///         <c>Corkboard.Core.Helpers.WindowDragModes</c> 折算成这里的成员，反过来也一样。
///     </para>
///     <para>
///         ⚠️ 枚举按数字落盘（见 <c>ConfigServiceBase.JsonOptions</c>），成员只能在末尾追加，
///         不能调顺序、不能改名——即使当前这一份还没写进配置，也照这条规矩来。
///     </para>
/// </summary>
public enum WindowDragMode
{
    /// <summary>长按拖动（默认）：按住窗口任意位置约半秒就能拖，短按仍归控件。</summary>
    HoldAnywhere = 0,

    /// <summary>只能拖标题栏：窗口空白处按住不再拖窗口。</summary>
    TitleBarOnly = 1,

    /// <summary>固定窗口：位置与大小都锁住，四边也拉不动，但里面的按钮照常点得动。</summary>
    Fixed = 2
}
