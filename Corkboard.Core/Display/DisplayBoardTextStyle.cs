namespace Corkboard.Core.Display;

/// <summary>
///     看板模式全屏窗口**每一处文字**的默认字号与派生比例，全在这一个地方。
///     <para>
///         它跟考试看板（<see cref="Exam.ExamBoardTextStyle" />）共用同一套上下限与夹取函数，
///         但默认值不是一套：考试看板的时钟独占大半块屏幕（220 号），看板模式的时钟是**左上角一小块**，
///         220 号会把作业挤成一条缝（用户反馈过「一大块大块」「上面太空」）。
///     </para>
///     <para>
///         设置页滑块的默认档、全屏窗口的渲染、配置归一化时的兜底值，三处都读这里，
///         **不要**在 XAML 或 ViewModel 里另写一组数字。
///     </para>
/// </summary>
public static class DisplayTextStyle
{
    /// <summary>左上角那块大时钟的默认字号。</summary>
    public const double DefaultClockFontSize = 96;

    /// <summary>时钟下面那行日期的默认字号。</summary>
    public const double DefaultClockDateFontSize = 22;

    /// <summary>作业区那行小标题的默认字号（标题本身已按用户要求从屏幕上撤掉，字号留着给别处复用）。</summary>
    public const double DefaultTitleFontSize = 22;

    /// <summary>左下角长按退出那颗图标的大小（它是字形，不是字，但也叫「字号」）。</summary>
    public const double DefaultExitIconSize = 24;

    /// <summary>
    ///     右上角那块天气里「温度大字」的默认字号（用户可改，设置 → 看板模式 → 字号）。
    ///     <para>
    ///         它是天气这一块的**基准**：图标、「多云」那行现象、底下一排小字全由它折算，
    ///         所以设置页只给这一个滑块，不再让用户逐个去调那三档。
    ///     </para>
    /// </summary>
    public const double DefaultWeatherFontSize = 22;

    /// <summary>天气字号的下限：再小远看就读不出来了。</summary>
    public const double MinWeatherFontSize = 18;

    /// <summary>天气字号的上限：它只是屏幕角上的一块，不该长成半屏广告。</summary>
    public const double MaxWeatherFontSize = 96;

    /// <summary>屏幕最底下那行一言的默认字号（用户可改）。</summary>
    public const double DefaultQuoteFontSize = 18;

    /// <summary>一言字号的下限（再小就真读不见了）。</summary>
    public const double MinQuoteFontSize = 14;

    /// <summary>一言字号的上限。</summary>
    public const double MaxQuoteFontSize = 72;

    /// <summary>时钟 / 日期下面那行「下一场考试还有多久」的默认字号（用户可改）。</summary>
    /// <remarks>
    ///     它跟天气、一言一样是**自己一项配置**，不跟着时钟字号走：这块信息常常要有自己的一档
    ///     大小（离屏远近不同，跟日期一样大未必看得清）。
    /// </remarks>
    public const double DefaultExamCountdownFontSize = 22;

    /// <summary>考试倒计时字号的下限：再小远看就读不出来了。</summary>
    public const double MinExamCountdownFontSize = 14;

    /// <summary>考试倒计时字号的上限：它只是时钟下面的一行字，不该长成半屏标语。</summary>
    public const double MaxExamCountdownFontSize = 96;

    /// <summary>
    ///     秒数相对主时间的比例——「小秒」那一档：秒在视觉上是主时间的尾巴，
    ///     同号会跟分针抢注意力，所以照大钟表的习惯缩到四成左右。
    /// </summary>
    public const double ClockSecondsScale = 0.42;

    /// <summary>
    ///     秒数相对主时间的比例——「大秒」那一档（默认）：与主时间同号。
    ///     <para>
    ///         远看的时候四成大小根本读不出来是几秒，用户明确要求「秒不要小字」。
    ///         同号之后整块时钟一眼读得出「几点几分几秒」。
    ///     </para>
    /// </summary>
    public const double ClockSecondsSameSizeScale = 1.0;

    /// <summary>「小秒」时秒数要往下压多少（乘主字号），用来跟主时间的基线对齐。</summary>
    public const double ClockSecondsBaselineRatio = 0.18;

    /// <summary>天气图标相对<b>温度大字</b>的倍率：图标比温度大一圈，跟真实天气卡一样「图比字抢眼一点」。</summary>
    public const double WeatherIconToTemperatureRatio = 2.5;

    /// <summary>天气图标的最小边长（再小就看不出是太阳还是云）。</summary>
    public const double MinWeatherIconSize = 32;

    /// <summary>天气图标的最大边长（时钟调到 480 时不该长成一整块海报）。</summary>
    public const double MaxWeatherIconSize = 140;

    /// <summary>「多云」这类现象文字相对<b>温度大字</b>的比例。</summary>
    public const double WeatherConditionToTemperatureRatio = 0.62;

    /// <summary>现象文字的下限。</summary>
    public const double MinWeatherConditionFontSize = 15;

    /// <summary>现象文字的上限。</summary>
    public const double MaxWeatherConditionFontSize = 40;

    /// <summary>城市 / 湿度 / 空气那一排小字相对<b>温度大字</b>的比例。</summary>
    public const double WeatherFactToTemperatureRatio = 0.48;

    /// <summary>那排小字的下限。</summary>
    public const double MinWeatherFactFontSize = 13;

    /// <summary>那排小字的上限。</summary>
    public const double MaxWeatherFactFontSize = 34;

    /// <summary>
    ///     夹取天气那一块「温度大字」的字号（设置里的「天气字号」）。
    ///     <para>
    ///         它是这一块的基准：图标、现象与小字都由它再折算，三档的上下限也是**从小到大排好**的
    ///         （18 &gt; 15 &gt; 13），所以无论用户把天气调多大都不会三档挤成一样大。
    ///         坏值（NaN / 无穷）退回默认字号，而不是夹到边上——它不是用户输的数字。
    ///     </para>
    /// </summary>
    public static double WeatherFontSize(double configured)
    {
        if (!double.IsFinite(configured))
            return DefaultWeatherFontSize;

        return Math.Clamp(configured, MinWeatherFontSize, MaxWeatherFontSize);
    }

    /// <summary>按「天气字号」算天气图标的边长（夹在上下限之间）。</summary>
    public static double WeatherIconSize(double weatherFontSize) => Math.Clamp(
        WeatherFontSize(weatherFontSize) * WeatherIconToTemperatureRatio,
        MinWeatherIconSize,
        MaxWeatherIconSize);

    /// <summary>按「天气字号」算「多云」那行现象文字的字号。</summary>
    public static double WeatherConditionFontSize(double weatherFontSize) => Math.Clamp(
        WeatherFontSize(weatherFontSize) * WeatherConditionToTemperatureRatio,
        MinWeatherConditionFontSize,
        MaxWeatherConditionFontSize);

    /// <summary>按「天气字号」算那一排小字（城市 / 今天高低 / 湿度 / 空气）的字号。</summary>
    public static double WeatherFactFontSize(double weatherFontSize) => Math.Clamp(
        WeatherFontSize(weatherFontSize) * WeatherFactToTemperatureRatio,
        MinWeatherFactFontSize,
        MaxWeatherFactFontSize);

    /// <summary>夹取一言那一行的字号（设置里的「一言字号」）。坏值同样退回默认值。</summary>
    public static double QuoteFontSize(double configured)
    {
        if (!double.IsFinite(configured))
            return DefaultQuoteFontSize;

        return Math.Clamp(configured, MinQuoteFontSize, MaxQuoteFontSize);
    }

    /// <summary>
    ///     夹取时钟下面那行「下一场考试还有多久」的字号（设置里的「考试倒计时字号」）。
    ///     坏值（NaN / 无穷）退回默认字号，而不是夹到边上。
    /// </summary>
    public static double ExamCountdownFontSize(double configured)
    {
        if (!double.IsFinite(configured))
            return DefaultExamCountdownFontSize;

        return Math.Clamp(configured, MinExamCountdownFontSize, MaxExamCountdownFontSize);
    }

    /// <summary>按主时钟字号算出秒数那一截该用多大（默认「大秒」那一档）。</summary>
    public static double ClockSecondsFontSize(double clockFontSize) =>
        ClockSecondsFontSize(clockFontSize, sameSize: true);

    /// <summary>按主时钟字号与「秒数是否与主时间同字号」算出秒数那一截该用多大。</summary>
    public static double ClockSecondsFontSize(double clockFontSize, bool sameSize)
    {
        var scale = sameSize ? ClockSecondsSameSizeScale : ClockSecondsScale;
        return IsUsable(clockFontSize) ? clockFontSize * scale : DefaultClockFontSize * scale;
    }

    /// <summary>
    ///     秒数那截要往下压多少边距。
    ///     <para>
    ///         同字号时不用压（两段字号一样，底边对齐就是基线对齐）；小字号时要压一截，
    ///         比例跟着主字号走，用户改字号不用另调。
    ///     </para>
    /// </summary>
    public static double ClockSecondsBaselineOffset(double clockFontSize, bool sameSize)
    {
        if (sameSize)
            return 0;

        return (IsUsable(clockFontSize) ? clockFontSize : DefaultClockFontSize) * ClockSecondsBaselineRatio;
    }

    private static bool IsUsable(double fontSize) => double.IsFinite(fontSize) && fontSize > 0;
}
