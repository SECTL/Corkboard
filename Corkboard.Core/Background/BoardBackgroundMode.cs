namespace Corkboard.Core.Background;

/// <summary>
///     板子（考试看板 / 看板模式这类全屏窗口）的底子怎么画。
///     <para>
///         数值冻结：老配置里写的是数字，<b>只能在末尾追加</b>，不许插队、不许改已有数字。
///     </para>
/// </summary>
public enum BoardBackgroundMode
{
    /// <summary>跟随主题（默认）：用主题的底色，明暗切换自己跟着走。</summary>
    Theme = 0,

    /// <summary>纯色：用户挑的一个颜色。</summary>
    Solid = 1,

    /// <summary>双色渐变：起点色 + 终点色 + 角度。</summary>
    Gradient = 2,

    /// <summary>图片：铺满整屏（等比裁切），可调暗化与模糊。</summary>
    Image = 3
}

/// <summary>
///     背景模式的取值处理：脏值（负数、越界数字）一律折成默认，不让一块坏配置把整屏画黑。
/// </summary>
public static class BoardBackgroundModes
{
    /// <summary>默认背景模式。</summary>
    public const BoardBackgroundMode Default = BoardBackgroundMode.Theme;

    /// <summary>把任意值折成合法模式。</summary>
    public static BoardBackgroundMode Normalize(BoardBackgroundMode mode) =>
        Enum.IsDefined(mode) ? mode : Default;

    /// <summary>把任意整数折成合法模式（读盘容错用）。</summary>
    public static BoardBackgroundMode Normalize(int mode) =>
        Enum.IsDefined(typeof(BoardBackgroundMode), mode) ? (BoardBackgroundMode)mode : Default;
}
