namespace Corkboard.Core.Board;

/// <summary>
///     多列排布（<see cref="BoardLayoutMode.Columns" />）的栏宽约定。
///     <para>
///         每一栏都是一条窄阅读栏：最宽 720 DIP（约 40 个中文字一行，和原来「单列」的限宽一样），
///         窄到 520 DIP 就再并一栏进来。窗口宽度落在两者之间时栏宽跟着均分，所以宽窗口是两栏、
///         三栏并排，窄窗口自己收回一栏——**不再是一列死排到底**。
///     </para>
///     <para>
///         这两个值交给 <c>AdaptiveWrapPanel</c> 的 <c>MinItemWidth</c> / <c>MaxItemWidth</c>，
///         真正算几栏的是 <see cref="Controls.AdaptiveGridMetrics.Resolve" />，别在页面里另写一套。
///     </para>
/// </summary>
public static class BoardColumnStyle
{
    /// <summary>单栏的宽度下限：再窄下去一行放不下几个字，不如少排一栏。</summary>
    public const double MinColumnWidth = 520;

    /// <summary>单栏的宽度上限：超过这个宽度一行太长，读起来费劲（约 40 个中文字）。</summary>
    public const double MaxColumnWidth = 720;
}
