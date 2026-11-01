namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     全屏考试看板左下角那颗按钮「怎么最小化」：点一下就收起来，还是按住不放才收起来。
///     <para>
///         两种习惯都有人：鼠标党嫌按住 2 秒麻烦，投影笔 / 触屏党怕划过去就收起来。
///         所以做成设置里的一档下拉（见「设置 → 考试看板 → 最小化与浮窗」），两种都给。
///     </para>
///     <para>
///         枚举按数字落盘（配置管道没有字符串枚举转换器），只能在末尾追加成员。
///     </para>
/// </summary>
public enum ExamBoardMinimizeGesture
{
    /// <summary>单击就最小化（默认）。长按 2 秒仍然是退出，误触只会收起来、不会关掉看板。</summary>
    Click = 0,

    /// <summary>按住 2 秒才最小化：把它当成第二颗「退出键」，划过去不会动。</summary>
    Hold = 1
}
