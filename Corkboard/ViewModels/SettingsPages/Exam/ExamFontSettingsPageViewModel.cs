using System.ComponentModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Exam;
using Corkboard.Core.Icons;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Exam;

/// <summary>
///     「看板字号」设置页：全屏考试看板上**每一处文字**各有一根滑块，外加「恢复默认字号」。
///     <para>
///         16 项字号按看板上的区域分成四组（<see cref="ExamFontGroup" />），界面画成四张可折叠卡、
///         卡里一项一条 <see cref="ExamFontSizeItem" />——不给每项单写一遍 XAML
///         （那样加一项要改三处：resx、Designer、XAML，还容易漏）。
///         表里的 <c>Id</c> 同时是资源键的后半截（<c>Settings_Exam_Font_&lt;Id&gt;</c>）与写回配置的入口，
///         **三处必须对得上**，对不上会被 <c>ExamFontSizeTests</c> 钉住。
///     </para>
///     <para>
///         值是草稿（<c>decimal?</c>），拖动过程中只改草稿、松手（或离开页面）才提交 ——
///         <see cref="MainConfigHandler" /> 每变一次属性就落一次盘，绑到配置上等于拖一路写几十遍
///         <c>settings.json</c>。夹取区间与渲染都读 <see cref="ExamBoardTextStyle" />，界面这里不再另写数字。
///     </para>
/// </summary>
public partial class ExamFontSettingsPageViewModel : ViewModelBase
{
    private bool _isAttached;

    public ExamFontSettingsPageViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        FontSizes = BuildItems();
        FontSizeGroups = BuildGroups(FontSizes);
        ResetDrafts();
    }

    public ExamBoardSettingsConfig Settings => Config.ExamBoard;

    /// <summary>时钟字号的上限（比一般文字大得多），输入框直接绑它。</summary>
    public double MinFontSize => ExamBoardTextStyle.MinFontSize;

    /// <summary>一般字号的上下限。</summary>
    public double MaxFontSize => ExamBoardTextStyle.MaxFontSize;

    /// <summary>时钟那两档的上限。</summary>
    public double MaxClockFontSize => ExamBoardTextStyle.MaxClockFontSize;

    /// <summary>看板上所有可调的字号，按「从上到下、从大到小」的视觉顺序排。</summary>
    public IReadOnlyList<ExamFontSizeItem> FontSizes { get; }

    /// <summary>
    ///     同一批字号按看板上的**区域**分成四组（标题与副标题 / 时钟 / 时钟下面的考试信息 / 考试表格与空状态）。
    ///     界面照这张表画四张可折叠卡，默认全部收起——十几项平铺一页要滚半天才找得到要改的那一项。
    /// </summary>
    public IReadOnlyList<ExamFontGroup> FontSizeGroups { get; }

    /// <summary>
    ///     这 16 项就是看板上全部的字（外加左下角那颗退出图标），顺序即界面顺序。
    ///     <c>Id</c> 决定资源键与读写的配置属性，**不要**随手改（改了老配置里对应那项就回默认值）。
    /// </summary>
    private IReadOnlyList<ExamFontSizeItem> BuildItems() =>
    [
        // 顶部：主标题与副标题。
        new("Title", CR.Settings_Exam_Font_MainTitle, CR.Settings_Exam_Font_MainTitleDescription,
            ExamBoardTextStyle.DefaultTitleFontSize, false,
            () => Settings.TitleFontSize, value => Settings.TitleFontSize = value),

        new("Subtitle", CR.Settings_Exam_Font_Subtitle, CR.Settings_Exam_Font_SubtitleDescription,
            ExamBoardTextStyle.DefaultSubtitleFontSize, false,
            () => Settings.SubtitleFontSize, value => Settings.SubtitleFontSize = value),

        // 时钟那一组：两个档位，旁边有表 vs 只剩时钟。
        new("Clock", CR.Settings_Exam_Font_Clock, CR.Settings_Exam_Font_ClockDescription,
            ExamBoardTextStyle.DefaultClockFontSize, true,
            () => Settings.ClockFontSize, value => Settings.ClockFontSize = value),

        new("ClockAlone", CR.Settings_Exam_Font_ClockAlone, CR.Settings_Exam_Font_ClockAloneDescription,
            ExamBoardTextStyle.DefaultClockAloneFontSize, true,
            () => Settings.ClockAloneFontSize, value => Settings.ClockAloneFontSize = value),

        new("ClockDate", CR.Settings_Exam_Font_ClockDate, CR.Settings_Exam_Font_ClockDateDescription,
            ExamBoardTextStyle.DefaultClockDateFontSize, false,
            () => Settings.ClockDateFontSize, value => Settings.ClockDateFontSize = value),

        // 时钟下面那组考试信息。
        new("InfoCaption", CR.Settings_Exam_Font_InfoCaption, CR.Settings_Exam_Font_InfoCaptionDescription,
            ExamBoardTextStyle.DefaultInfoCaptionFontSize, false,
            () => Settings.InfoCaptionFontSize, value => Settings.InfoCaptionFontSize = value),

        new("InfoSubject", CR.Settings_Exam_Font_InfoSubject, CR.Settings_Exam_Font_InfoSubjectDescription,
            ExamBoardTextStyle.DefaultInfoSubjectFontSize, false,
            () => Settings.InfoSubjectFontSize, value => Settings.InfoSubjectFontSize = value),

        new("InfoTime", CR.Settings_Exam_Font_InfoTime, CR.Settings_Exam_Font_InfoTimeDescription,
            ExamBoardTextStyle.DefaultInfoTimeFontSize, false,
            () => Settings.InfoTimeFontSize, value => Settings.InfoTimeFontSize = value),

        new("InfoRemaining", CR.Settings_Exam_Font_InfoRemaining, CR.Settings_Exam_Font_InfoRemainingDescription,
            ExamBoardTextStyle.DefaultInfoRemainingFontSize, false,
            () => Settings.InfoRemainingFontSize, value => Settings.InfoRemainingFontSize = value),

        new("InfoPaper", CR.Settings_Exam_Font_InfoPaper, CR.Settings_Exam_Font_InfoPaperDescription,
            ExamBoardTextStyle.DefaultInfoPaperFontSize, false,
            () => Settings.InfoPaperFontSize, value => Settings.InfoPaperFontSize = value),

        // 右侧那张考试表。
        new("TableHeader", CR.Settings_Exam_Font_TableHeader, CR.Settings_Exam_Font_TableHeaderDescription,
            ExamBoardTextStyle.DefaultTableHeaderFontSize, false,
            () => Settings.TableHeaderFontSize, value => Settings.TableHeaderFontSize = value),

        new("Table", CR.Settings_Exam_Font_Table, CR.Settings_Exam_Font_TableDescription,
            ExamBoardTextStyle.DefaultTableFontSize, false,
            () => Settings.TableFontSize, value => Settings.TableFontSize = value),

        new("TableSubject", CR.Settings_Exam_Font_TableSubject, CR.Settings_Exam_Font_TableSubjectDescription,
            ExamBoardTextStyle.DefaultTableSubjectFontSize, false,
            () => Settings.TableSubjectFontSize, value => Settings.TableSubjectFontSize = value),

        // 一场都没排时的空状态。
        new("Empty", CR.Settings_Exam_Font_Empty, CR.Settings_Exam_Font_EmptyDescription,
            ExamBoardTextStyle.DefaultEmptyFontSize, false,
            () => Settings.EmptyFontSize, value => Settings.EmptyFontSize = value),

        new("EmptyHint", CR.Settings_Exam_Font_EmptyHint, CR.Settings_Exam_Font_EmptyHintDescription,
            ExamBoardTextStyle.DefaultEmptyHintFontSize, false,
            () => Settings.EmptyHintFontSize, value => Settings.EmptyHintFontSize = value),

        // 左下角长按退出那颗图标：它不是字，是字形，但同属「看板上这块东西多大」。
        new("ExitIcon", CR.Settings_Exam_Font_ExitIcon, CR.Settings_Exam_Font_ExitIconDescription,
            ExamBoardTextStyle.DefaultExitIconSize, false,
            () => Settings.ExitIconSize, value => Settings.ExitIconSize = value)
    ];

    /// <summary>
    ///     把 <see cref="FontSizes" /> 按看板上的区域切成四组，顺序就是看板上从上到下的顺序。
    ///     <para>
    ///         分组的**依据是「在看板上的位置」**，不是「字体大小」——用户找的是「时钟下面那块字」，
    ///         所以每组只能收同一区域的那几项；<see cref="ExamFontSizeItem.Id" /> 写错一个字，
    ///         <c>ExamFontSizeTests</c> 就会红（它比对两边合起来是不是正好 16 项）。
    ///     </para>
    /// </summary>
    private static IReadOnlyList<ExamFontGroup> BuildGroups(IReadOnlyList<ExamFontSizeItem> items)
    {
        var byId = items.ToDictionary(item => item.Id, StringComparer.Ordinal);

        ExamFontSizeItem[] Pick(params string[] ids) => ids.Select(id => byId[id]).ToArray();

        return
        [
            new ExamFontGroup(
                CR.Settings_Exam_Font_GroupTitle,
                CR.Settings_Exam_Font_GroupTitleDescription,
                Pick("Title", "Subtitle")),

            new ExamFontGroup(
                CR.Settings_Exam_Font_GroupClock,
                CR.Settings_Exam_Font_GroupClockDescription,
                Pick("Clock", "ClockAlone", "ClockDate")),

            new ExamFontGroup(
                CR.Settings_Exam_Font_GroupInfo,
                CR.Settings_Exam_Font_GroupInfoDescription,
                Pick("InfoCaption", "InfoSubject", "InfoTime", "InfoRemaining", "InfoPaper")),

            new ExamFontGroup(
                CR.Settings_Exam_Font_GroupTable,
                CR.Settings_Exam_Font_GroupTableDescription,
                Pick("TableHeader", "Table", "TableSubject", "Empty", "EmptyHint", "ExitIcon"))
        ];
    }

    /// <summary>挂上配置变化订阅（页面进入可视树时调）。重复调用不会重复挂。</summary>
    public void Attach()
    {
        // 页面是 transient、配置是单例：订阅必须成对，见 Detach。
        if (_isAttached)
            return;

        _isAttached = true;
        Settings.PropertyChanged += OnSettingsChanged;
        ResetDrafts();
    }

    /// <summary>断开订阅（页面离开可视树时调）。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        Settings.PropertyChanged -= OnSettingsChanged;
    }

    /// <summary>把还压在草稿里的字号立刻写进配置（失焦 / 回车 / 离开页面时调）。</summary>
    public void CommitAll()
    {
        foreach (var item in FontSizes)
            item.Commit();
    }

    /// <summary>
    ///     「恢复默认字号」：把这一页的十几项一次性写回默认值并落盘一次。
    ///     <para>
    ///         不做二次确认——它就是一组数字，改乱了再点一次也回得来；
    ///         真要撤回还有云同步那边的设置备份。
    ///     </para>
    /// </summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        foreach (var item in FontSizes)
        {
            item.Draft = (decimal)item.DefaultValue;
            item.Commit();
        }
    }

    /// <summary>把草稿整体同步成配置里的值（首次进页面、云端恢复设置之后）。</summary>
    private void ResetDrafts()
    {
        foreach (var item in FontSizes)
            item.ResetDraft();
    }

    /// <summary>配置被外部改了（云端恢复设置 / 点了恢复默认）：把草稿重新对齐，免得界面停在旧值上。</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => ResetDrafts();
}

/// <summary>
///     看板上的**一处**文字：资源文案、默认字号、上限档位（一般 / 时钟），以及读写配置的那两个委托。
///     <para>
///         界面只认这一张表，所以加一项新文字只要在 <see cref="ExamFontSettingsPageViewModel" /> 的表里加一行、
///         再补三个资源键即可，不用动 XAML。
///     </para>
/// </summary>
public partial class ExamFontSizeItem : ObservableObject
{
    /// <summary>
    ///     拖动浮窗里那句预览文字**最多按多大渲染**。看板字号能到 480（时钟那两档），
    ///     照实数渲染会把浮窗撑出屏幕；数值本身照实显示，超过这一档只在浮窗里补一句「预览已缩小」。
    /// </summary>
    public const double PreviewMaxFontSize = 96;

    private readonly Func<double> _read;
    private readonly Action<double> _write;

    public ExamFontSizeItem(
        string id,
        string name,
        string description,
        double defaultValue,
        bool isClock,
        Func<double> read,
        Action<double> write)
    {
        Id = id;
        Name = name;
        Description = description;
        DefaultValue = defaultValue;
        IsClock = isClock;
        _read = read;
        _write = write;
        _draft = (decimal)defaultValue;
    }

    /// <summary>这一项的标识：资源键的后半截，同时决定它读写哪个配置属性。</summary>
    public string Id { get; }

    /// <summary>设置卡上的名称（「时钟」这种）。</summary>
    public string Name { get; }

    /// <summary>名称下面那句简介：说清这一项是哪块字、改了会怎样。</summary>
    public string Description { get; }

    /// <summary>默认字号，「恢复默认字号」与坏值兜底都用它。</summary>
    public double DefaultValue { get; }

    /// <summary>是不是时钟那两档（上限更大）。</summary>
    public bool IsClock { get; }

    /// <summary>滑块下限。</summary>
    public double Minimum => ExamBoardTextStyle.MinFontSize;

    /// <summary>滑块上限：时钟用 <see cref="ExamBoardTextStyle.MaxClockFontSize" />，其余用一般上限。</summary>
    public double Maximum => IsClock ? ExamBoardTextStyle.MaxClockFontSize : ExamBoardTextStyle.MaxFontSize;

    /// <summary>界面上的草稿；拖动滑块的过程中只改它，松手（<see cref="Commit" />）才写进配置。</summary>
    [ObservableProperty]
    private decimal? _draft;

    /// <summary>滑块是不是正在被按住拖（拖动期间浮窗开着）。由页面的指针处理器置位。</summary>
    [ObservableProperty]
    private bool _isDragging;

    /// <summary>
    ///     滑块的滑块值（<see cref="double" />，因为 Avalonia 的 <c>Slider</c> 只吃 double）。
    ///     <para>
    ///         它只是 <see cref="Draft" /> 的一层适配：写进来先取整、再夹进本项区间，
    ///         存回去还是草稿。**不要**在这里直接写配置——拖动一路会写几十次 <c>settings.json</c>。
    ///     </para>
    /// </summary>
    public double SliderValue
    {
        get => (double)(Draft ?? (decimal)DefaultValue);
        set
        {
            var rounded = Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), Minimum, Maximum);
            if (Draft is { } draft && (double)draft == rounded)
                return;

            Draft = (decimal)rounded;
        }
    }

    /// <summary>滑块右边那行字（「48 号」/「48 pt」），跟随界面语言。</summary>
    public string ValueText => $"{SliderValue:0} {CR.Settings_Exam_Font_Unit}";

    /// <summary>
    ///     浮窗里的预览文字：就用这一项自己的名字（「主标题」「只有时钟时」），
    ///     所以不用为 16 项各写一句示例文案，改名时预览自动跟着改。
    /// </summary>
    public string SampleText => Name;

    /// <summary>
    ///     退出图标那一项预览的不是字、是字形，这里给出它的字形；
    ///     其余项为 <c>null</c>（预览走文字那条路）。
    /// </summary>
    public string? SampleGlyph => Id == "ExitIcon" ? FluentIcons.ArrowExitFilled : null;

    /// <summary>预览用字形画（退出图标），还是用文字画。</summary>
    public bool IsIcon => SampleGlyph is not null;

    /// <summary>看板上这一处本来就是粗体的那几项，预览也跟着粗一点。</summary>
    public FontWeight PreviewFontWeight =>
        Id is "Title" or "Clock" or "ClockAlone" or "TableHeader" ? FontWeight.SemiBold : FontWeight.Normal;

    /// <summary>浮窗里预览文字的字号：照当前值，但最多 <see cref="PreviewMaxFontSize" />。</summary>
    public double PreviewFontSize => Math.Min(SliderValue, PreviewMaxFontSize);

    /// <summary>值超过了预览上限（浮窗里那句「预览已缩小」就靠它显隐）。</summary>
    public bool IsPreviewScaled => SliderValue > PreviewMaxFontSize;

    /// <summary>草稿一变，滑块位置、右边那行数值、浮窗里的预览与提示全要跟着刷。</summary>
    partial void OnDraftChanged(decimal? value)
    {
        OnPropertyChanged(nameof(SliderValue));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(PreviewFontSize));
        OnPropertyChanged(nameof(IsPreviewScaled));
    }

    /// <summary>把草稿夹进合法区间并写回配置；夹过的值同步回界面，免得显示的和落盘的不是一个数。</summary>
    public void Commit()
    {
        var current = _read();
        var value = Draft is { } draft
            ? (IsClock ? ExamBoardTextStyle.ClampClock((double)draft, DefaultValue)
                       : ExamBoardTextStyle.Clamp((double)draft, DefaultValue))
            : (IsClock ? ExamBoardTextStyle.ClampClock(current, DefaultValue)
                       : ExamBoardTextStyle.Clamp(current, DefaultValue));

        if (Math.Abs(current - value) > double.Epsilon)
            _write(value);

        var normalized = (decimal)value;
        if (Draft != normalized)
            Draft = normalized;
    }

    /// <summary>把草稿重新对齐到配置里的值（夹取后）。</summary>
    public void ResetDraft()
    {
        var value = IsClock
            ? ExamBoardTextStyle.ClampClock(_read(), DefaultValue)
            : ExamBoardTextStyle.Clamp(_read(), DefaultValue);

        var normalized = (decimal)value;
        if (Draft != normalized)
            Draft = normalized;
    }
}

/// <summary>
///     看板上的**一个区域**：一张可折叠设置卡的标题、简介与它收着的那几项字号。
///     <para>
///         分组只影响界面的折叠，不改变数据的读写——每一项仍然各自读写自己那个配置属性
///         （<see cref="ExamFontSizeItem" /> 里的两个委托），所以改分组、拆分组都不用动配置。
///     </para>
/// </summary>
public sealed class ExamFontGroup
{
    public ExamFontGroup(string title, string description, IReadOnlyList<ExamFontSizeItem> items)
    {
        Title = title;
        Description = description;
        Items = items;
    }

    /// <summary>折叠卡的标题（「时钟」这种区域名）。</summary>
    public string Title { get; }

    /// <summary>标题下面那句简介：说清这一组是哪块地方的字。</summary>
    public string Description { get; }

    /// <summary>这一组里的字号，顺序即界面顺序。</summary>
    public IReadOnlyList<ExamFontSizeItem> Items { get; }
}
