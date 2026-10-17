

namespace Corkboard.Core.Board;

/// <summary>
///     作业板上**非内容文字**的字号约束：科目标题，以及作业条上的说明文字（类型 + 字段值，
///     比如「页数 P3–5」）与截止日期。作业内容本身的字号走 <see cref="RichTextStyle" />。
///     <para>
///         这两处原来一个跟着窗口默认值走、一个写死 12，后排根本看不见。默认值与上下限收到这里，
///         设置页与渲染两边就不会各写一组数字。
///     </para>
/// </summary>
public static class BoardTextStyle
{
    /// <summary>科目标题没设过时的字号。比正文大一点，隔几排也认得出这一块是什么科目。</summary>
    public const double DefaultSubjectFontSize = 16;

    /// <summary>作业条小字（说明文字与截止日期）没设过时的字号。</summary>
    public const double DefaultMetaFontSize = 14;

    /// <summary>二级分类小标题没设过时的字号。比科目小一号、比作业条小字大一点，一眼看得出层级。</summary>
    public const double DefaultSectionFontSize = 13;

    /// <summary>科目标题字号下限。</summary>
    public const double MinSubjectFontSize = 12;

    /// <summary>科目标题字号上限。</summary>
    public const double MaxSubjectFontSize = 48;

    /// <summary>作业条小字字号下限。</summary>
    public const double MinMetaFontSize = 10;

    /// <summary>作业条小字字号上限。</summary>
    public const double MaxMetaFontSize = 32;

    /// <summary>二级分类小标题字号下限。</summary>
    public const double MinSectionFontSize = 9;

    /// <summary>二级分类小标题字号上限。</summary>
    public const double MaxSectionFontSize = 32;

    /// <summary>把科目标题字号夹进合法区间。NaN / 无穷这类坏值退回默认字号，不让它流到渲染层。</summary>
    public static double ClampSubjectFontSize(double size)
    {
        return double.IsFinite(size) ? Math.Clamp(size, MinSubjectFontSize, MaxSubjectFontSize) : DefaultSubjectFontSize;
    }

    /// <summary>把作业条小字字号夹进合法区间。坏值同样退回默认字号。</summary>
    public static double ClampMetaFontSize(double size)
    {
        return double.IsFinite(size) ? Math.Clamp(size, MinMetaFontSize, MaxMetaFontSize) : DefaultMetaFontSize;
    }

    /// <summary>作业板卡片上快捷标签胶囊没设过时的字号。</summary>
    public const double DefaultTagFontSize = 12;

    /// <summary>快捷标签字号下限。</summary>
    public const double MinTagFontSize = 8;

    /// <summary>快捷标签字号上限。</summary>
    public const double MaxTagFontSize = 32;

    /// <summary>把快捷标签字号夹进合法区间。坏值退回默认字号。</summary>
    public static double ClampTagFontSize(double size)
    {
        return double.IsFinite(size) ? Math.Clamp(size, MinTagFontSize, MaxTagFontSize) : DefaultTagFontSize;
    }

    /// <summary>把二级分类小标题字号夹进合法区间。坏值同样退回默认字号。</summary>
    public static double ClampSectionFontSize(double size)
    {
        return double.IsFinite(size) ? Math.Clamp(size, MinSectionFontSize, MaxSectionFontSize) : DefaultSectionFontSize;
    }
}
