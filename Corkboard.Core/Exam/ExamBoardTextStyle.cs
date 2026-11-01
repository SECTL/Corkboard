namespace Corkboard.Core.Exam;

/// <summary>
///     全屏考试看板**每一处文字**的字号：默认值、上下限与夹取，全在这一个地方。
///     <para>
///         看板是一整块没有滚动条的屏幕，字号既不能小到后排看不见，也不能大到把表格顶出屏幕，
///         所以每一项都有自己的区间；设置页的输入框上下限与看板的渲染都读这里的常量，
///         **不要**在 XAML 里另写一组数字（那样设置说「最大 96」、实际却画成别的样子）。
///     </para>
///     <para>
///         时钟有**两档**：旁边有考试表时是一个号，整块屏幕只剩时钟时是另一个号（默认大得多）。
///         以前这两个数写死在 ViewModel 的常量里，用户改不了，大屏上嫌小、小屏上嫌大。
///     </para>
/// </summary>
public static class ExamBoardTextStyle
{
    /// <summary>主标题（看板左上角那行大字）的默认字号。</summary>
    public const double DefaultTitleFontSize = 34;

    /// <summary>副标题的默认字号：主标题的六成左右。</summary>
    public const double DefaultSubtitleFontSize = 22;

    /// <summary>时钟旁边有考试表时的默认字号。</summary>
    public const double DefaultClockFontSize = 108;

    /// <summary>整块屏幕只剩时钟时的默认字号。</summary>
    public const double DefaultClockAloneFontSize = 220;

    /// <summary>时钟下面那行日期（「2026年10月18日 周日」）的默认字号。</summary>
    public const double DefaultClockDateFontSize = 22;

    /// <summary>时钟下面那组考试信息的抬头（「下一场 / 进行中」）默认字号。</summary>
    public const double DefaultInfoCaptionFontSize = 18;

    /// <summary>考试信息里的科目默认字号。</summary>
    public const double DefaultInfoSubjectFontSize = 54;

    /// <summary>考试信息里的日期与时间范围默认字号。</summary>
    public const double DefaultInfoTimeFontSize = 26;

    /// <summary>考试信息里的剩余时间（「还剩 40 分钟」）默认字号。</summary>
    public const double DefaultInfoRemainingFontSize = 32;

    /// <summary>考试信息里的卷面信息默认字号。</summary>
    public const double DefaultInfoPaperFontSize = 19;

    /// <summary>考试表格表头的默认字号。</summary>
    public const double DefaultTableHeaderFontSize = 15;

    /// <summary>考试表格的数据行默认字号（日期、时间、状态）。</summary>
    public const double DefaultTableFontSize = 26;

    /// <summary>考试表格的科目列默认字号：比同一行的其它格大一号、还加粗。</summary>
    public const double DefaultTableSubjectFontSize = 30;

    /// <summary>一场都没排时那句「还没有考试安排」的默认字号。</summary>
    public const double DefaultEmptyFontSize = 26;

    /// <summary>「还没有考试安排」下面那句提示的默认字号。</summary>
    public const double DefaultEmptyHintFontSize = 17;

    /// <summary>左下角长按退出那颗图标的默认大小（它不是字，是字形，所以也叫「字号」）。</summary>
    public const double DefaultExitIconSize = 24;

    /// <summary>各项字号的绝对下限（比它还小的字在看板上等于没写）。</summary>
    public const double MinFontSize = 8;

    /// <summary>
    ///     一般文字的上限。时钟那两档另有上限——它们是整块屏幕上最大的字，
    ///     跟表头用同一个天花板就没有意义了。
    /// </summary>
    public const double MaxFontSize = 120;

    /// <summary>时钟（有考试表 / 只剩时钟）的上限：再大就顶出屏幕了。</summary>
    public const double MaxClockFontSize = 480;

    /// <summary>把一般字号夹进 <see cref="MinFontSize" />–<see cref="MaxFontSize" />。坏值退回 <paramref name="fallback" />。</summary>
    public static double Clamp(double size, double fallback)
    {
        return double.IsFinite(size) ? Math.Clamp(size, MinFontSize, MaxFontSize) : fallback;
    }

    /// <summary>把时钟字号夹进 <see cref="MinFontSize" />–<see cref="MaxClockFontSize" />。坏值退回 <paramref name="fallback" />。</summary>
    public static double ClampClock(double size, double fallback)
    {
        return double.IsFinite(size) ? Math.Clamp(size, MinFontSize, MaxClockFontSize) : fallback;
    }
}
