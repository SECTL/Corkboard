namespace Corkboard.Core.Display;

/// <summary>
///     看板模式「作业超一屏时自动翻页」的纯逻辑：下一页落点怎么算、什么时候算到底了。
///     <para>
///         没有 UI、没有计时器——翻页的节拍在窗口 code-behind（<c>DisplayBoardWindow</c>），
///         这里只有算式，所以能单测。以前这一段是「当前偏移 + 视口 × 0.9」直接写死在 code-behind 里，
///         结果翻到哪儿纯看运气：一张卡片可能被从中间切开，最后那点内容也常常看不到
///         （用户反馈「检测的也不太好，它应该要有边界吧」）。
///     </para>
///     <para>
///         现在的规矩是：<b>能在这一屏里对齐到某张区块卡片的顶边，就对齐过去</b>——
///         每翻一页，最上面那张卡片总是完整的；对齐不到就退回按比例翻，保证每次都有进展、
///         也不留「两页之间漏掉一条带」的空档（落点永远不超过「当前偏移 + 一屏」）。
///     </para>
/// </summary>
public static class DisplayCarousel
{
    /// <summary>
    ///     内容比视口高出这么多（DIP）以内就当「一屏放得下」，不翻页。
    ///     留一点余量是因为亚像素舍入会让 extent 比 viewport 大零点几。
    /// </summary>
    public const double BottomSlack = 8;

    /// <summary>一页最多走视口高度的这么多（留一成重叠，读着连贯，不会正好切在卡片中间）。</summary>
    public const double PageRatio = 0.9;

    /// <summary>
    ///     一次翻页至少要走视口高度的这么多。
    ///     有的边界离得很近（多列排布时几栏的顶边差不了几十像素），照着它对齐就成了「原地挪两下」。
    /// </summary>
    public const double MinProgressRatio = 0.35;

    /// <summary>
    ///     指针动过之后先别再翻页：用户在操作（点作业、拖动排序、滚轮看内容），
    ///     这时页面自己翻走最讨人嫌。
    ///     <para>
    ///         ⚠️ 这是「动过之后停一会儿」，<b>不是</b>「指针停在上面就一直停」——
    ///         以前用的是 PointerEntered / PointerExited 的悬停暂停，而作业区几乎占满整个屏幕，
    ///         鼠标只要停在板子上（看板模式那块屏基本都这样）轮播就<b>永远</b>不开工，
    ///         用户看到的就是「轮播功能没有」。
    ///     </para>
    /// </summary>
    public static readonly TimeSpan PauseAfterActivity = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     算下一页的滚动偏移。
    /// </summary>
    /// <param name="currentOffset">当前纵向偏移（一般就是 <c>ScrollViewer.Offset.Y</c>）。</param>
    /// <param name="viewport">视口高度（<c>ScrollViewer.Viewport.Height</c>）。</param>
    /// <param name="extent">内容高度（<c>ScrollViewer.Extent.Height</c>）。</param>
    /// <param name="boundaries">
    ///     可对齐的边界（区块卡片的顶边，相对滚动内容、升序、允许乱序传入）。
    ///     空表或都没落在这一屏里时就按 <see cref="PageRatio" /> 翻。
    /// </param>
    /// <returns>要滚到的偏移；<c>0</c> 表示回顶部（放得下 / 已经是最后一页）。</returns>
    public static double NextOffset(
        double currentOffset,
        double viewport,
        double extent,
        IReadOnlyList<double>? boundaries = null)
    {
        if (double.IsNaN(currentOffset) || double.IsNaN(viewport) || double.IsNaN(extent))
            return 0;

        if (viewport <= 0 || currentOffset < 0)
            return 0;

        // 一屏放得下：什么也不做（回 0 是因为这个高度下也没有可滚的余地）。
        var maxOffset = extent - viewport;
        if (maxOffset <= BottomSlack)
            return 0;

        // 已经贴在底上了：下一拍回顶部，从头再轮一遍。
        if (currentOffset >= maxOffset - BottomSlack)
            return 0;

        var desired = currentOffset + viewport * PageRatio;

        // 再往前就到底了：直接贴底，别让最后那截内容永远露不出来。
        if (desired >= maxOffset - BottomSlack)
            return maxOffset;

        // 对齐到「这一屏之内、且已经走得够远」的最靠下的那张卡片顶边。
        var minProgress = currentOffset + viewport * MinProgressRatio;
        var target = desired;
        if (boundaries is not null)
        {
            foreach (var boundary in boundaries)
            {
                if (double.IsNaN(boundary) || boundary < minProgress || boundary > desired)
                    continue;

                target = boundary;
            }
        }

        return Math.Min(target, maxOffset);
    }
}
