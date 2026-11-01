using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Background;
using Corkboard.Core.Board;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam;

namespace Corkboard.Core.Models.SubConfigs.Exam;

/// <summary>
///     考试看板的**显示偏好**（<c>settings.json</c> 的 <c>exam_board</c> 段）。
///     <para>
///         这里是「怎么看」：阈值、时钟摆哪边、两种形态收不收列表，以及看板上**每一处文字的字号**。
///         标题、副标题与考试安排都跟着**看板文件**走（<see cref="Exam.ExamBoardDocument" />），
///         因为用户可能同时维护好几块看板，换看板就该整套内容一起换。
///     </para>
/// </summary>
public partial class ExamBoardSettingsConfig : BoardBackgroundSettings
{
    /// <summary>
    ///     ⚠️ 遗留字段：标题已经搬进看板文件（<see cref="Exam.ExamBoardDocument.Title" />）。
    ///     留着只为读老版本写在 <c>settings.json</c> 里的值，由 <see cref="Exam.ExamService" />
    ///     在建第一块看板时搬走并清空。界面不要再绑它。
    /// </summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>⚠️ 遗留字段，同 <see cref="Title" />。</summary>
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>⚠️ 遗留字段，同 <see cref="Title" />。</summary>
    [ObservableProperty] private Color? _subtitleColor;

    /// <summary>
    ///     「即将开始」的窗口（分钟）：开考前这么多分钟之内算「即将开始」。
    ///     读的时候一律过一遍 <see cref="ExamStatusPolicy.ClampUpcomingMinutes" />。
    /// </summary>
    [ObservableProperty] private int _upcomingThresholdMinutes = ExamStatusPolicy.DefaultUpcomingMinutes;

    /// <summary>时钟与考试列表谁在左（默认时钟在左）。</summary>
    [ObservableProperty] private ExamBoardClockSide _clockSide = ExamBoardClockSide.Left;

    /// <summary>
    ///     进入「即将开始」之后是否把右侧的完整考试表收起来、只留下一场。
    ///     默认开：开考前那一刻，用户要看的是「马上考什么」，不是整张表。
    /// </summary>
    [ObservableProperty] private bool _hideExamListWhenUpcoming = true;

    /// <summary>
    ///     考试进行中是否仍然显示右侧的考试信息。默认关——考试中看板只留一个大时钟，
    ///     免得一直盯着科目和卷面信息分心。
    /// </summary>
    [ObservableProperty] private bool _showExamListInProgress;

    /// <summary>
    ///     「剩余时间快到了」的门槛（分钟）：正在考剩多久、即将开始还有多久，少于这个数就转红。
    ///     默认 15 分钟；0 表示不提醒。读的时候一律过一遍
    ///     <see cref="ExamStatusPolicy.ClampRemainingWarningMinutes" />。
    /// </summary>
    [ObservableProperty] private int _remainingWarningMinutes = ExamStatusPolicy.DefaultRemainingWarningMinutes;

    /// <summary>
    ///     「剩余时间精确到秒」：开启后最后一小时里的倒计时带秒（「还剩 12 分 30 秒」），
    ///     一小时以上仍然只说小时/分钟（那时候秒没意义）。默认关。
    /// </summary>
    [ObservableProperty] private bool _remainingSecondsEnabled;

    /// <summary>
    ///     时钟下面那组考试信息（进行中 / 下一场是哪一科、时间范围、剩余时间、卷面）要不要显示。
    ///     默认开——它就是这个看板的信息主体；关掉之后只剩一个大时钟，适合只想看时间的场合。
    /// </summary>
    [ObservableProperty] private bool _showExamInfo = true;

    // ── 最小化与浮窗 ────────────────────────────────────────────────────────
    // 考试时经常要临时切出去做别的事（对答案、看通知），但看板是「摆给别人看」的，
    // 直接退掉就什么都没了、还得重新点进来。所以给它一条「收起来」的路：
    // 左下角那颗按钮改成最小化，屏幕上只剩一颗浮窗按钮，点一下回来、长按 2 秒才真的退出。

    /// <summary>
    ///     左下角那颗按钮**什么时候**变成「最小化」（见 <see cref="ExamBoardMinimizeMode" />）。
    ///     默认 <see cref="ExamBoardMinimizeMode.InProgress" />——用户的原话是「应该是在考试进行中才是最小化，
    ///     平常都是只有退出」：考完就没必要收起来了，那颗按钮那时就是退出键。
    /// </summary>
    [ObservableProperty] private ExamBoardMinimizeMode _minimizeMode = ExamBoardMinimizeMode.InProgress;

    /// <summary>
    ///     最小化的手势：单击还是按住 2 秒（见 <see cref="ExamBoardMinimizeGesture" />）。
    ///     只作用于最小化；长按 2 秒退出在两种手势下都仍然可用。
    /// </summary>
    [ObservableProperty] private ExamBoardMinimizeGesture _minimizeGesture = ExamBoardMinimizeGesture.Click;

    /// <summary>
    ///     最小化之后那颗浮窗按钮上的文字。空串表示用默认文案（「显示考试看板」），
    ///     这样改语言、云端恢复设置都不会把用户写的字弄丢。
    /// </summary>
    [ObservableProperty] private string _floatingButtonText = string.Empty;

    /// <summary>
    ///     浮窗按钮的缩放（百分比，60–200，默认 100）。读的时候一律过一遍
    ///     <see cref="ExamBoardFloatingStyle.ClampScalePercent" />。
    /// </summary>
    [ObservableProperty] private double _floatingScalePercent = ExamBoardFloatingStyle.DefaultScalePercent;

    /// <summary>
    ///     浮窗按钮停在屏幕的哪个位置（九宫格，默认底部居中——用户要的就是「屏幕中央底部」）。
    /// </summary>
    [ObservableProperty] private DockAnchor _floatingAnchor = DockAnchor.BottomCenter;

    /// <summary>
    ///     浮窗按钮上那行字的颜色（默认白字，压在强调色底上）。
    ///     按钮自己的底色不开放配置，走 XAML 上的 <c>Classes="accent"</c>（跟随主题强调色）。
    /// </summary>
    [ObservableProperty] private Color _floatingTextColor = Colors.White;

    /// <summary>
    ///     浮窗按钮上要不要画软件图标。默认开；关掉之后只剩一行字（
    ///     <see cref="FloatingIconOnly" /> 会跟着失效，否则整颗按钮就空了）。
    /// </summary>
    [ObservableProperty] private bool _showFloatingIcon = true;

    /// <summary>
    ///     自定义图标：图片文件的绝对路径。空串表示用软件自己的图标
    ///     （<c>AppIcon.LoadBitmap()</c> 那张 PNG）。文件读不出来时照样退回默认图标。
    /// </summary>
    [ObservableProperty] private string _floatingIconPath = string.Empty;

    /// <summary>无字模式：只画图标、不画文字（屏幕上只剩一颗方方正正的图章）。</summary>
    [ObservableProperty] private bool _floatingIconOnly;

    // ── 字号 ────────────────────────────────────────────────────────────────
    // 看板上的每一处文字各有一个字号，收在这里（而不是写死在窗口 XAML 里），
    // 设置页「看板字号」那一页改的就是这几个值。读的时候一律过 ExamBoardTextStyle 的夹取，
    // 手改过 settings.json 也不会把看板弄成「字小得看不见」或者「字大到顶出屏幕」。

    /// <summary>主标题（左上角那行大字）的字号。</summary>
    [ObservableProperty] private double _titleFontSize = ExamBoardTextStyle.DefaultTitleFontSize;

    /// <summary>副标题的字号。</summary>
    [ObservableProperty] private double _subtitleFontSize = ExamBoardTextStyle.DefaultSubtitleFontSize;

    /// <summary>时钟的字号（旁边有考试表时的这一档）。</summary>
    [ObservableProperty] private double _clockFontSize = ExamBoardTextStyle.DefaultClockFontSize;

    /// <summary>整块屏幕只剩时钟时的字号（列表收起、考试进行中）。</summary>
    [ObservableProperty] private double _clockAloneFontSize = ExamBoardTextStyle.DefaultClockAloneFontSize;

    /// <summary>时钟下面那行日期的字号。</summary>
    [ObservableProperty] private double _clockDateFontSize = ExamBoardTextStyle.DefaultClockDateFontSize;

    /// <summary>考试信息抬头（「下一场」「进行中」）的字号。</summary>
    [ObservableProperty] private double _infoCaptionFontSize = ExamBoardTextStyle.DefaultInfoCaptionFontSize;

    /// <summary>考试信息里科目那行大字的字号。</summary>
    [ObservableProperty] private double _infoSubjectFontSize = ExamBoardTextStyle.DefaultInfoSubjectFontSize;

    /// <summary>考试信息里日期与时间范围的字号。</summary>
    [ObservableProperty] private double _infoTimeFontSize = ExamBoardTextStyle.DefaultInfoTimeFontSize;

    /// <summary>考试信息里剩余时间（倒计时）的字号。</summary>
    [ObservableProperty] private double _infoRemainingFontSize = ExamBoardTextStyle.DefaultInfoRemainingFontSize;

    /// <summary>考试信息里卷面信息的字号。</summary>
    [ObservableProperty] private double _infoPaperFontSize = ExamBoardTextStyle.DefaultInfoPaperFontSize;

    /// <summary>考试表格表头的字号。</summary>
    [ObservableProperty] private double _tableHeaderFontSize = ExamBoardTextStyle.DefaultTableHeaderFontSize;

    /// <summary>考试表格数据行的字号（日期、时间、状态）。</summary>
    [ObservableProperty] private double _tableFontSize = ExamBoardTextStyle.DefaultTableFontSize;

    /// <summary>考试表格里科目列的字号（比同一行其它格大一号、还加粗）。</summary>
    [ObservableProperty] private double _tableSubjectFontSize = ExamBoardTextStyle.DefaultTableSubjectFontSize;

    /// <summary>「还没有考试安排」那句的字号。</summary>
    [ObservableProperty] private double _emptyFontSize = ExamBoardTextStyle.DefaultEmptyFontSize;

    /// <summary>「还没有考试安排」下面那句提示的字号。</summary>
    [ObservableProperty] private double _emptyHintFontSize = ExamBoardTextStyle.DefaultEmptyHintFontSize;

    /// <summary>左下角长按退出那颗图标的大小（它也是看板上唯一没绑字号的字形）。</summary>
    [ObservableProperty] private double _exitIconSize = ExamBoardTextStyle.DefaultExitIconSize;

    /// <summary>读盘后收拾一遍：阈值夹进合法区间、字号夹进各自区间、遗留的标题与副标题去首尾空白（手改的脏数据不进界面）。</summary>
    public void Normalize()
    {
        UpcomingThresholdMinutes = ExamStatusPolicy.ClampUpcomingMinutes(UpcomingThresholdMinutes);
        RemainingWarningMinutes = ExamStatusPolicy.ClampRemainingWarningMinutes(RemainingWarningMinutes);
        Title = Title?.Trim() ?? string.Empty;
        Subtitle = Subtitle?.Trim() ?? string.Empty;

        MinimizeMode = Enum.IsDefined(MinimizeMode) ? MinimizeMode : ExamBoardMinimizeMode.InProgress;
        MinimizeGesture = Enum.IsDefined(MinimizeGesture) ? MinimizeGesture : ExamBoardMinimizeGesture.Click;
        FloatingButtonText = ExamBoardFloatingStyle.NormalizeText(FloatingButtonText);
        FloatingScalePercent = ExamBoardFloatingStyle.ClampScalePercent(FloatingScalePercent);
        FloatingAnchor = DockAnchors.Normalize(FloatingAnchor);
        FloatingIconPath = FloatingIconPath?.Trim() ?? string.Empty;

        // 图标关掉时「无字模式」没有意义——那样整颗按钮上就什么都没有了。
        if (!ShowFloatingIcon)
            FloatingIconOnly = false;

        TitleFontSize = ExamBoardTextStyle.Clamp(TitleFontSize, ExamBoardTextStyle.DefaultTitleFontSize);
        SubtitleFontSize = ExamBoardTextStyle.Clamp(SubtitleFontSize, ExamBoardTextStyle.DefaultSubtitleFontSize);
        ClockFontSize = ExamBoardTextStyle.ClampClock(ClockFontSize, ExamBoardTextStyle.DefaultClockFontSize);
        ClockAloneFontSize = ExamBoardTextStyle.ClampClock(ClockAloneFontSize, ExamBoardTextStyle.DefaultClockAloneFontSize);
        ClockDateFontSize = ExamBoardTextStyle.Clamp(ClockDateFontSize, ExamBoardTextStyle.DefaultClockDateFontSize);
        InfoCaptionFontSize = ExamBoardTextStyle.Clamp(InfoCaptionFontSize, ExamBoardTextStyle.DefaultInfoCaptionFontSize);
        InfoSubjectFontSize = ExamBoardTextStyle.Clamp(InfoSubjectFontSize, ExamBoardTextStyle.DefaultInfoSubjectFontSize);
        InfoTimeFontSize = ExamBoardTextStyle.Clamp(InfoTimeFontSize, ExamBoardTextStyle.DefaultInfoTimeFontSize);
        InfoRemainingFontSize = ExamBoardTextStyle.Clamp(InfoRemainingFontSize, ExamBoardTextStyle.DefaultInfoRemainingFontSize);
        InfoPaperFontSize = ExamBoardTextStyle.Clamp(InfoPaperFontSize, ExamBoardTextStyle.DefaultInfoPaperFontSize);
        TableHeaderFontSize = ExamBoardTextStyle.Clamp(TableHeaderFontSize, ExamBoardTextStyle.DefaultTableHeaderFontSize);
        TableFontSize = ExamBoardTextStyle.Clamp(TableFontSize, ExamBoardTextStyle.DefaultTableFontSize);
        TableSubjectFontSize = ExamBoardTextStyle.Clamp(TableSubjectFontSize, ExamBoardTextStyle.DefaultTableSubjectFontSize);
        EmptyFontSize = ExamBoardTextStyle.Clamp(EmptyFontSize, ExamBoardTextStyle.DefaultEmptyFontSize);
        EmptyHintFontSize = ExamBoardTextStyle.Clamp(EmptyHintFontSize, ExamBoardTextStyle.DefaultEmptyHintFontSize);
        ExitIconSize = ExamBoardTextStyle.Clamp(ExitIconSize, ExamBoardTextStyle.DefaultExitIconSize);

        NormalizeBackground();
    }
}
