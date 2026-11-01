namespace Corkboard.Core.Display;

/// <summary>
///     看板模式全屏窗口的摆位尺寸 —— 纯常量，没有逻辑。
///     <para>
///         时钟的位置<b>是固定的</b>：永远在左上角（用户要求「时间的显示位置不能改，只能显示到左上角」），
///         所以这里只有「贴左边要补多少」这一个数，以前那套「版式（左上 / 右上 / 顶部居中 / 只有作业）」
///         已经整体下架，不要再加回来。
///     </para>
/// </summary>
public static class DisplayBoardMetrics
{
    /// <summary>
    ///     时钟贴左边时要补的那点缩进（DIP）：让大钟与日期的左边缘跟作业卡片的左边缘对齐。
    ///     <para>
    ///         作业板页面自己的内容还带了一圈 12 的内边距（<c>BoardPage.axaml</c> 里
    ///         <c>Margin="12 0 12 12"</c> 那两处），而全屏窗口只给外层网格留了 56，
    ///         两者差的就是这一截——不补的话时钟会比作业往左戳出去一块（用户反馈过
    ///         「左上角的时间日期和作业的组件左侧不对齐」）。
    ///     </para>
    ///     <para>
    ///         ⚠️ 这个数跟 <c>BoardPage.axaml</c> 里那两处 12 是同一个约定：改那边要一起改这里。
    ///     </para>
    /// </summary>
    public const double ClockLeftGutter = 12;

    /// <summary>
    ///     作业区底部与屏幕底下那一行一言之间留的空隙（DIP）。
    ///     <para>
    ///         看板模式把一言画在窗口最底下、盖在作业区上面，翻到最后一页时卡片会钻到那行字底下
    ///         （用户原话「它可能到一言的部分去了，它应该要有边界吧」）。所以窗口 code-behind 会量出
    ///         一言的高度 + 它自己的下边距，再<b>多留这么多</b>当作作业区的边界（<c>UpdateBoardBottomInset</c>）。
    ///         一言关掉时边界归零、作业区摊开。
    ///     </para>
    /// </summary>
    public const double BoardBottomGap = 8;
}
