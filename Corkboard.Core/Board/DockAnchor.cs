namespace Corkboard.Core.Board;

/// <summary>
///     收纳起来之后，那颗「展开作业板」的小按钮贴在屏幕的哪个位置。
///     <para>
///         枚举按数字落盘，只能在末尾增成员。顺序是九宫格从左到右、从上到下，
///         设置页里那九个圆点也按这个顺序排，选中的数字直接当索引用。
///     </para>
/// </summary>
public enum DockAnchor
{
    TopLeft = 0,
    TopCenter = 1,
    TopRight = 2,
    MiddleLeft = 3,
    Center = 4,
    MiddleRight = 5,
    BottomLeft = 6,
    BottomCenter = 7,
    BottomRight = 8
}

/// <summary><see cref="DockAnchor" /> 的取值规则（纯逻辑、有单测）：把坏数据夹回合法的九个位置。</summary>
public static class DockAnchors
{
    /// <summary>不是九宫格里的位置（手改坏的数据、旧版本）时退回右下角。</summary>
    public const DockAnchor Default = DockAnchor.BottomRight;

    /// <summary>九宫格一共九个位置。</summary>
    public const int Count = 9;

    public static DockAnchor Normalize(DockAnchor anchor)
    {
        return Enum.IsDefined(anchor) ? anchor : Default;
    }

    /// <summary>这个位置在九宫格里的列（0 左 / 1 中 / 2 右）。</summary>
    public static int Column(DockAnchor anchor) => (int)Normalize(anchor) % 3;

    /// <summary>这个位置在九宫格里的行（0 上 / 1 中 / 2 下）。</summary>
    public static int Row(DockAnchor anchor) => (int)Normalize(anchor) / 3;

    /// <summary>水平方向是否靠中间（中间那列只按水平中心对齐，不吃左右偏移）。</summary>
    public static bool IsHorizontalCenter(DockAnchor anchor) => Column(anchor) == 1;

    /// <summary>垂直方向是否靠中间（中间那行只按垂直中心对齐，不吃上下偏移）。</summary>
    public static bool IsVerticalCenter(DockAnchor anchor) => Row(anchor) == 1;
}
