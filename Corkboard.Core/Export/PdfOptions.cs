using Avalonia;

namespace Corkboard.Core.Export;

/// <summary>
///     PDF 纸张大小。枚举按数字落盘，只能在末尾追加成员。
/// </summary>
public enum PdfPageSize
{
    /// <summary>A4（210mm × 297mm）。</summary>
    A4 = 0,

    /// <summary>A5（148mm × 210mm）。</summary>
    A5 = 1,

    /// <summary>Letter（8.5in × 11in）。</summary>
    Letter = 2
}

/// <summary>
///     PDF 方向。<see cref="Auto" /> 时按板子的形状挑：板子比屏宽就用横向，否则竖向。
///     枚举按数字落盘，只能在末尾追加成员。
/// </summary>
public enum PdfOrientation
{
    /// <summary>跟着板子走：板子宽就横向，否则竖向。</summary>
    Auto = 0,

    /// <summary>强制竖向。</summary>
    Portrait = 1,

    /// <summary>强制横向。</summary>
    Landscape = 2
}

/// <summary>
///     PDF 页边距档位（等价 DIP 见 <see cref="PdfOptions.GetMargin" />）。
///     枚举按数字落盘，只能在末尾追加成员。
/// </summary>
public enum PdfMargin
{
    /// <summary>窄边距（24 DIP）。</summary>
    Narrow = 0,

    /// <summary>普通边距（36 DIP）。</summary>
    Normal = 1,

    /// <summary>宽边距（54 DIP）。</summary>
    Wide = 2
}

/// <summary>
///     PDF 里作业的排序方式。<see cref="FollowBoard" /> 表示照板子上当前的顺序，
///     其余几项按创建 / 更新 / 科目重排，用于「导出时我不想跟界面一样」的场合。
///     枚举按数字落盘，只能在末尾追加成员。
/// </summary>
public enum PdfSortMode
{
    /// <summary>跟随作业板当前的顺序。</summary>
    FollowBoard = 0,

    /// <summary>按创建时间从新到旧。</summary>
    CreatedDescending = 1,

    /// <summary>按创建时间从旧到新。</summary>
    CreatedAscending = 2,

    /// <summary>按更新时间从新到旧。</summary>
    UpdatedDescending = 3,

    /// <summary>按科目名排序。</summary>
    Subject = 4,

    /// <summary>保持用户固定的顺序（不重排）。</summary>
    Fixed = 5
}

/// <summary>
///     PDF 导出的纸张与版式换算：页面尺寸、边距、列数都在这里，界面的下拉框、配置读盘时的
///     兜底与排版引擎共用同一份数字，免得三边各写一套。
///     <para>
///         长度单位一律是 DIP（96 DPI）；写进 PDF 前由导出侧按 72 / 96 换算成 PDF point。
///     </para>
/// </summary>
public static class PdfOptions
{
    /// <summary>A4 的宽度（DIP）：210mm @ 96 DPI。</summary>
    public const double A4WidthDip = 793.7;

    /// <summary>A4 的高度（DIP）：297mm @ 96 DPI。</summary>
    public const double A4HeightDip = 1122.5;

    /// <summary>A5 的宽度（DIP）：148mm @ 96 DPI。</summary>
    public const double A5WidthDip = 559.4;

    /// <summary>A5 的高度（DIP）：210mm @ 96 DPI。</summary>
    public const double A5HeightDip = 793.7;

    /// <summary>Letter 的宽度（DIP）：8.5in @ 96 DPI。</summary>
    public const double LetterWidthDip = 816;

    /// <summary>Letter 的高度（DIP）：11in @ 96 DPI。</summary>
    public const double LetterHeightDip = 1056;

    /// <summary>窄边距（DIP）。</summary>
    public const double NarrowMarginDip = 24;

    /// <summary>普通边距（DIP），也是配置里的默认档。</summary>
    public const double NormalMarginDip = 36;

    /// <summary>宽边距（DIP）。</summary>
    public const double WideMarginDip = 54;

    /// <summary>配置里的默认列数。</summary>
    public const int DefaultColumns = 2;

    /// <summary>列数下限：至少一列，否则没地方排版。</summary>
    public const int MinColumns = 1;

    /// <summary>列数上限：A4 上四列已经挤得看不清，再多没有意义。</summary>
    public const int MaxColumns = 4;

    /// <summary>
    ///     按纸张与方向算页面尺寸。方向是 <see cref="PdfOrientation.Auto" /> 时看板子形状：
    ///     <paramref name="boardIsWide" /> 为真用横向（宽高对调），否则竖向。
    /// </summary>
    /// <param name="pageSize">纸张大小。</param>
    /// <param name="orientation">方向；越界值按竖向处理。</param>
    /// <param name="boardIsWide">板子是否比窗口宽（只在自动方向时用）。</param>
    public static Size GetPageSize(PdfPageSize pageSize, PdfOrientation orientation, bool boardIsWide)
    {
        var (width, height) = pageSize switch
        {
            PdfPageSize.A5 => (A5WidthDip, A5HeightDip),
            PdfPageSize.Letter => (LetterWidthDip, LetterHeightDip),
            _ => (A4WidthDip, A4HeightDip)
        };

        var landscape = orientation switch
        {
            PdfOrientation.Landscape => true,
            PdfOrientation.Portrait => false,
            _ => boardIsWide
        };

        return landscape ? new Size(height, width) : new Size(width, height);
    }

    /// <summary>把边距档位换成 DIP；越界值按普通边距处理。</summary>
    public static double GetMargin(PdfMargin margin)
    {
        return margin switch
        {
            PdfMargin.Narrow => NarrowMarginDip,
            PdfMargin.Wide => WideMarginDip,
            _ => NormalMarginDip
        };
    }

    /// <summary>
    ///     把列数夹进 <see cref="MinColumns" />..<see cref="MaxColumns" />。
    ///     配置里的默认列数是 <see cref="DefaultColumns" />，手改坏了的 0 / 负数在这里被夹成 1。
    /// </summary>
    public static int GetColumnCount(int columns)
    {
        return Math.Clamp(columns, MinColumns, MaxColumns);
    }
}
