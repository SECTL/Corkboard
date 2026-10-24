namespace Corkboard.Core.Icons;

/// <summary>
///     Fluent System Icons 字形表。
///     <para>
///         上游 SecRandom-C 用 <c>IconsMappingGenerator</c> 源生成器从
///         <c>Assets/FluentSystemIcons-Resizable.json</c> 生成这张表；Corkboard 骨架阶段先手写
///         当前用到的子集，接入生成器后这个文件应被生成产物取代（对外 API 形状保持不变）。
///     </para>
///     <para>
///         码位来自官方图标映射，格式为 <c>\uXXXX</c>，只有配合
///         <see cref="GlobalConstants.FluentIconsFontFamily" /> 才有意义。
///     </para>
/// </summary>
public static class FluentIcons
{
    public const string BoardFilled = "\uE20C";
    public const string HomeFilled = "\uE993";
    public const string SettingsFilled = "\uEF26";
    public const string InfoFilled = "\uE9E3";
    public const string AddFilled = "\uE00C";
    public const string DeleteFilled = "\uE61C";
    public const string ArrowLeftFilled = "\uE108";
    public const string PinFilled = "\uEDA0";
    public const string NoteFilled = "\uEBE9";
    public const string SaveFilled = "\uEEB4";
    public const string DocumentFilled = "\uE686";
    public const string ColorFilled = "\uE51D";
    public const string SyncFilled = "\uE160";

    /// <summary>设置里的更新页。码位来自上游图标映射表 <c>arrow_sync_20_filled</c>（与 <see cref="SyncFilled" /> 同码位）。</summary>
    public const string ArrowSyncFilled = "\uE160";

    /// <summary>「需要重启」提示按钮用。码位来自上游图标映射表 <c>arrow_counterclockwise_20_filled</c>。</summary>
    public const string ArrowCounterclockwiseFilled = "\uE0BC";

    /// <summary>时空回放入口。码位来自上游图标映射表 <c>history_20_filled</c>。</summary>
    public const string HistoryFilled = "\uE98F";

    /// <summary>退出回放。码位来自上游图标映射表 <c>arrow_exit_20_filled</c>。</summary>
    public const string ArrowExitFilled = "\uE0DE";

    /// <summary>收起 / 最小化（收纳按钮与「收纳」设置页用）。</summary>
    public const string ArrowMinimizeFilled = "\uE10E";

    /// <summary>下拉箭头（收纳位置选择那种「点开才展开」的入口用）。</summary>
    public const string ChevronDownFilled = "\uE447";

    /// <summary>未锁定（可以点得动）。码位来自上游图标映射表 <c>lock_open_20_filled</c>。</summary>
    public const string LockOpenFilled = "\uEAF7";

    /// <summary>已锁定（主窗口不再接收鼠标输入）。码位来自上游图标映射表 <c>lock_closed_20_filled</c>。</summary>
    public const string LockClosedFilled = "\uEAEF";

    /// <summary>编辑已有作业。码位来自上游图标映射表 <c>edit_20_filled</c>。</summary>
    public const string EditFilled = "\uE7C8";

    /// <summary>科目区块的排序手柄。码位来自上游图标映射表 <c>re_order_dots_vertical_20_filled</c>。</summary>
    public const string ReOrderDotsVerticalFilled = "\uEE46";

    /// <summary>关于页里「会用系统程序打开」的整行链接。码位来自上游图标映射表 <c>open_20_filled</c>。</summary>
    public const string OpenFilled = "\uEC2D";

    /// <summary>主界面标题栏的排序入口。码位来自上游图标映射表 <c>arrow_sort_20_filled</c>。</summary>
    public const string ArrowSortFilled = "\uE13E";

    /// <summary>设置里的账号页，兼作未登录时的头像占位。码位来自上游图标映射表 <c>person_20_filled</c>。</summary>
    public const string PersonFilled = "\uECE4";

    /// <summary>设置里的云同步页。码位来自上游图标映射表 <c>cloud_20_filled</c>。</summary>
    public const string CloudFilled = "\uE4DB";

    /// <summary>「考试看板」分组与考试安排页。码位来自上游图标映射表 <c>calendar_ltr_20_filled</c>。</summary>
    public const string CalendarFilled = "\uE32F";

    /// <summary>考试看板上的时钟与「看板显示」页。码位来自上游图标映射表 <c>clock_20_filled</c>。</summary>
    public const string ClockFilled = "\uE4C3";

    /// <summary>「时间同步」页的立即同步按钮。码位来自上游图标映射表 <c>timer_20_filled</c>。</summary>
    public const string TimerFilled = "\uF360";

    /// <summary>「时间同步」页的服务器列表。码位来自上游图标映射表 <c>globe_20_filled</c>。</summary>
    public const string GlobeFilled = "\uE904";

    /// <summary>「打开考试看板」入口。码位来自上游图标映射表 <c>full_screen_maximize_20_filled</c>。</summary>
    public const string FullScreenMaximizeFilled = "\uE8D0";

    /// <summary>设置里的「内容与样式」页。码位来自上游图标映射表 <c>text_font_20_filled</c>。</summary>
    public const string TextFontFilled = "\uF26E";

    /// <summary>设置里的「窗口与收纳」页。码位来自上游图标映射表 <c>window_20_filled</c>。</summary>
    public const string WindowFilled = "\uF482";
}
