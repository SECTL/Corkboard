using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Board;
using Corkboard.Core.Homework;
using Corkboard.Core.RichText;

namespace Corkboard.Core.Models.SubConfigs.Board;

/// <summary>
///     作业板模块的设置。便签本身不画背景色，所以这里没有便签配色；
///     内容的默认字号与默认颜色见 <see cref="DefaultContentFontSize" /> / <see cref="DefaultContentColor" />。
/// </summary>
public partial class BoardSettingsConfig : ObservableObject
{
    /// <summary>
    ///     作业板名称。留空表示用本地化资源里的默认名（见 <see cref="ResolveBoardName" />），
    ///     所以升级界面语言后没自定义过名称的用户会跟着语言走。
    /// </summary>
    [ObservableProperty] private string _boardName = string.Empty;

    /// <summary>
    ///     便签排布方式（展示设置）。只在「设置 → 作业板 → 排布」下拉框里改；
    ///     读的时候一律过一遍 <see cref="BoardLayoutModes.Normalize" />，老配置里的「通铺」按区块显示。
    /// </summary>
    [ObservableProperty] private BoardLayoutMode _layoutMode = BoardLayoutMode.SingleColumn;

    /// <summary>
    ///     区块排布里单个区块的最小宽度（DIP）。只在「设置 → 作业板 → 区块宽度」里改；
    ///     它决定一行能排几块，读的时候一律过一遍 <see cref="BoardBlockStyle.ClampMinItemWidth" />。
    /// </summary>
    [ObservableProperty] private double _blockMinWidth = BoardBlockStyle.DefaultMinItemWidth;

    /// <summary>
    ///     区块宽度是否自适应（对应「设置 → 作业板 → 区块宽度 → 区块宽度自适应」那个开关）。
    ///     开着时单块宽度随窗口伸缩，到上限才换行；关掉则每块固定为 <see cref="BlockMinWidth" />。
    ///     默认开：那条「随窗口伸缩」的行为是原来的表现，开关只是给了关掉它的办法。
    /// </summary>
    [ObservableProperty] private bool _blockWidthAdaptive = true;

    /// <summary>便签排序方式。改它的入口在主界面标题栏的排序按钮（设置里只有排布这类展示设置）。</summary>
    [ObservableProperty] private BoardSortMode _sortMode = BoardSortMode.CreatedDescending;

    /// <summary>删除便签前是否弹确认对话框。</summary>
    [ObservableProperty] private bool _confirmBeforeDelete = true;

    /// <summary>
    ///     标题栏右上角那几个动作按钮各显示不显示。默认全开；用户嫌哪颗多余就关掉哪颗
    ///     （「设置 → 作业板 → 顶部按钮」）。这些都是界面偏好，不影响数据。
    ///     <para>
    ///         ⚠️ 「锁定窗口」那颗不在这里管：它是点击穿透状态下唯一的解锁入口，
    ///         藏掉就再也点不回来了，必须常驻。
    ///     </para>
    /// </summary>
    [ObservableProperty] private bool _showTitleBarSettings = true;

    /// <inheritdoc cref="_showTitleBarSettings" />
    [ObservableProperty] private bool _showTitleBarSort = true;

    /// <inheritdoc cref="_showTitleBarSettings" />
    [ObservableProperty] private bool _showTitleBarAssign = true;

    /// <inheritdoc cref="_showTitleBarSettings" />
    [ObservableProperty] private bool _showTitleBarExport = true;

    /// <inheritdoc cref="_showTitleBarSettings" />
    [ObservableProperty] private bool _showTitleBarReplay = true;

    /// <summary>收纳按钮：还要 <see cref="DockEnabled" /> 也开着才显示。</summary>
    [ObservableProperty] private bool _showTitleBarDock = true;

    /// <summary>
    ///     是否按下面的规则自动清理过期作业。关掉之后只剩设置页里的「立即清理」这一个入口。
    ///     默认开：清理只是把作业从板子上拿掉，数据不删、随时能在时空回放里看到，
    ///     关着反而会让人以为功能坏了。
    /// </summary>
    [ObservableProperty] private bool _cleanupEnabled = true;

    /// <summary>清理频率。枚举按数字落盘，只能在末尾增成员。</summary>
    [ObservableProperty] private HomeworkCleanupFrequency _cleanupFrequency = HomeworkCleanupFrequency.Daily;

    /// <summary>每周清理时定在星期几动手（<see cref="HomeworkCleanupFrequency.Daily" /> 下忽略）。</summary>
    [ObservableProperty] private DayOfWeek _cleanupWeekday = DayOfWeek.Monday;

    /// <summary>动手的时刻（整点 + 分钟拆成两个字段存，免得为 <see cref="TimeOnly" /> 再加一个序列化器）。</summary>
    [ObservableProperty] private int _cleanupHour = HomeworkCleanupDefaults.DefaultHour;

    /// <summary>动手时刻的分钟部分。</summary>
    [ObservableProperty] private int _cleanupMinute = HomeworkCleanupDefaults.DefaultMinute;

    /// <summary>
    ///     作业内容的默认字号：新布置的作业没单独定「整篇字号」时用它，
    ///     局部字号标注则是在它之上的覆盖。
    /// </summary>
    [ObservableProperty] private double _defaultContentFontSize = RichTextStyle.DefaultFontSize;

    /// <summary>
    ///     作业内容的默认颜色。为空表示跟随主题前景色（深色模式下才看得清）；
    ///     局部颜色标注是在它之上的覆盖。
    /// </summary>
    [ObservableProperty] private Color? _defaultContentColor;

    /// <summary>
    ///     科目标题的字号（对应「设置 → 作业板 → 字号 → 科目标题字号」）。
    ///     读的时候一律过一遍 <see cref="BoardTextStyle.ClampSubjectFontSize" />。
    /// </summary>
    [ObservableProperty] private double _subjectFontSize = BoardTextStyle.DefaultSubjectFontSize;

    /// <summary>
    ///     作业条上小字（类型与字段值、截止日期）的字号。
    ///     读的时候一律过一遍 <see cref="BoardTextStyle.ClampMetaFontSize" />。
    /// </summary>
    [ObservableProperty] private double _noteMetaFontSize = BoardTextStyle.DefaultMetaFontSize;

    /// <summary>
    ///     作业板卡片上快捷标签胶囊的字号。读的时候一律过一遍 <see cref="BoardTextStyle.ClampTagFontSize" />。
    /// </summary>
    [ObservableProperty] private double _noteTagFontSize = BoardTextStyle.DefaultTagFontSize;

    /// <summary>
    ///     二级分类小标题的字号（科目下面那一层）。读的时候过一遍
    ///     <see cref="BoardTextStyle.ClampSectionFontSize" />。
    /// </summary>
    [ObservableProperty] private double _sectionFontSize = BoardTextStyle.DefaultSectionFontSize;

    /// <summary>
    ///     手写笔的默认宽度。读的时候过一遍 <see cref="Handwriting.HandwritingStyle.ClampPenWidth" />。
    /// </summary>
    [ObservableProperty] private double _handwritingPenWidth = Handwriting.HandwritingStyle.DefaultPenWidth;

    /// <summary>
    ///     手写橡皮的默认宽度。读的时候过一遍 <see cref="Handwriting.HandwritingStyle.ClampEraserWidth" />。
    /// </summary>
    [ObservableProperty] private double _handwritingEraserWidth = Handwriting.HandwritingStyle.DefaultEraserWidth;

    /// <summary>
    ///     手写笔的默认颜色。为空表示跟随主题前景色（深色主题下才看得清）。
    /// </summary>
    [ObservableProperty] private Color? _handwritingPenColor;

    /// <summary>
    ///     手写的那排常用色。读写都走 <see cref="ResolveHandwritingColors" /> /
    ///     <see cref="ApplyHandwritingColors" />（与内容色板同一套收拾规则）。
    /// </summary>
    public ObservableCollection<Color> HandwritingColors { get; set; } = [];

    /// <summary>取收拾干净的手写色板，给画布与设置页用。</summary>
    public List<Color> ResolveHandwritingColors() => RichTextPalette.Normalize(HandwritingColors);

    /// <summary>把手写色板原地替换成给定内容（就地增删改，别换集合，否则断了界面绑定）。</summary>
    public void ApplyHandwritingColors(IEnumerable<Color> colors)
    {
        var normalized = RichTextPalette.Normalize(colors);

        HandwritingColors.Clear();
        foreach (var color in normalized)
            HandwritingColors.Add(color);
    }

    /// <summary>
    ///     布置作业时可以一键点上的快捷标签（比如「背书」「默写」「订正」）。
    ///     空表表示没配过，布置作业表单上不显示这一行。
    /// </summary>
    public ObservableCollection<string> QuickTags { get; set; } = [];

    /// <summary>
    ///     主界面标题栏是否显示「收纳」按钮。关掉之后整块收纳功能都不露头，
    ///     已经收起来的按钮也会跟着展开主窗口——用户要的是「能藏干净」。
    /// </summary>
    [ObservableProperty] private bool _dockEnabled = true;

    /// <summary>
    ///     收纳起来之后那颗展开按钮贴在屏幕的哪个位置（九宫格）。读的时候过一遍
    ///     <see cref="DockAnchors.Normalize" />。
    /// </summary>
    [ObservableProperty] private DockAnchor _dockAnchor = DockAnchors.Default;

    /// <summary>展开按钮离选中那条边多远（DIP，右侧 / 下侧的位置往屏幕里收为正）。</summary>
    [ObservableProperty] private double _dockOffsetX = 24;

    /// <summary>展开按钮离选中那条边多远（DIP，右侧 / 下侧的位置往屏幕里收为正）。</summary>
    [ObservableProperty] private double _dockOffsetY = 24;

    /// <summary>展开按钮的大小（百分比，100 就是默认大小）。</summary>
    [ObservableProperty] private double _dockScalePercent = 100;

    /// <summary>
    ///     选中文字后弹出的工具浮窗上那排预设色。最多 <see cref="RichTextPalette.MaxColors" /> 个，
    ///     在「设置 → 作业板 → 内容格式」里增删改。
    ///     <para>
    ///         读写都该走 <see cref="ResolvePaletteColors" /> / <see cref="ApplyPaletteColors" />：
    ///         前者会把配置收拾干净（去透明、去重复、截断、空了回退默认），
    ///         后者负责把列表原地同步成给定内容。
    ///     </para>
    /// </summary>
    public ObservableCollection<Color> PaletteColors { get; set; } = [];

    /// <summary>取收拾干净的色板，给浮窗和设置页用。</summary>
    public List<Color> ResolvePaletteColors() => RichTextPalette.Normalize(PaletteColors);

    /// <summary>
    ///     把色板原地替换成给定内容。就地增删改而不是换一个新集合，
    ///     因为 <see cref="ObservableCollection{T}" /> 已经绑在设置页的列表上了，整个换掉会断掉绑定。
    /// </summary>
    public void ApplyPaletteColors(IEnumerable<Color> colors)
    {
        var normalized = RichTextPalette.Normalize(colors);

        PaletteColors.Clear();
        foreach (var color in normalized)
            PaletteColors.Add(color);
    }

    /// <summary>
    ///     读盘后和老配置迁移用：色板为空（老版本 settings.json 里没这个字段）或存了脏数据时，
    ///     在**原地**收拾成合法色板，让磁盘上的内容也是干净的一份。
    /// </summary>
    public void NormalizePalette()
    {
        var normalized = RichTextPalette.Normalize(PaletteColors);
        if (PaletteColors.Count == normalized.Count && PaletteColors.SequenceEqual(normalized))
            return;

        ApplyPaletteColors(normalized);
    }

    /// <summary>
    ///     读盘后和老配置迁移用：清理时刻夹进合法区间（老版本 settings.json 里没这个字段，
    ///     会直接落到默认值；手改过的脏数据则在这里被收拾干净）。
    /// </summary>
    public void NormalizeCleanup()
    {
        CleanupHour = HomeworkCleanupDefaults.ClampHour(CleanupHour);
        CleanupMinute = HomeworkCleanupDefaults.ClampMinute(CleanupMinute);
    }

    /// <summary>
    ///     读盘后和老配置迁移用：手写笔宽/橡皮宽、色板、快捷标签与收纳位置都收拾一遍，
    ///     手改的脏数据（宽度为负、位置编号越界）不会流到渲染层。
    /// </summary>
    public void NormalizeBoardExtras()
    {
        // 手写笔宽的历史默认值换过两次：4（细得像针）→ 10（太粗）→ 现在的 6。
        // 老配置读进来如果还停在某个历史默认值上，说明用户从没手动调过，这里跟着换成新默认值；
        // 用户真想要那个值，重新拖一次滑杆就好——比让所有人一打开就是旧默认值强。
        if (Math.Abs(HandwritingPenWidth - 4) < 1e-9 || Math.Abs(HandwritingPenWidth - 10) < 1e-9)
            HandwritingPenWidth = Handwriting.HandwritingStyle.DefaultPenWidth;

        SubjectFontSize = BoardTextStyle.ClampSubjectFontSize(SubjectFontSize);
        SectionFontSize = BoardTextStyle.ClampSectionFontSize(SectionFontSize);
        NoteMetaFontSize = BoardTextStyle.ClampMetaFontSize(NoteMetaFontSize);
        NoteTagFontSize = BoardTextStyle.ClampTagFontSize(NoteTagFontSize);

        HandwritingPenWidth = Handwriting.HandwritingStyle.ClampPenWidth(HandwritingPenWidth);
        HandwritingEraserWidth = Handwriting.HandwritingStyle.ClampEraserWidth(HandwritingEraserWidth);
        DockAnchor = DockAnchors.Normalize(DockAnchor);
        DockOffsetX = ClampDockOffset(DockOffsetX);
        DockOffsetY = ClampDockOffset(DockOffsetY);
        DockScalePercent = ClampDockScale(DockScalePercent);
    }

    /// <summary>收纳偏移的取值范围（DIP）：±2000 足够把按钮推到任何屏幕边上，又不至于跑进另一个宇宙。</summary>
    public const double MaxDockOffset = 2000;

    /// <summary>展开按钮缩放的取值范围（百分比）。</summary>
    public const double MinDockScalePercent = 60;

    public const double MaxDockScalePercent = 200;

    public static double ClampDockOffset(double offset)
    {
        return double.IsFinite(offset) ? Math.Clamp(offset, -MaxDockOffset, MaxDockOffset) : 0;
    }

    public static double ClampDockScale(double percent)
    {
        return double.IsFinite(percent) ? Math.Clamp(percent, MinDockScalePercent, MaxDockScalePercent) : 100;
    }

    /// <summary>
    ///     快捷标签收拾一遍：去空白、去重（忽略大小写）、截断到 <see cref="MaxQuickTags" /> 个。
    ///     与色板不同，这里空了就是空了（表单上不显示那一行），不塞默认标签。
    /// </summary>
    public void NormalizeQuickTags()
    {
        var normalized = NormalizeQuickTagList(QuickTags);
        if (QuickTags.SequenceEqual(normalized))
            return;

        QuickTags.Clear();
        foreach (var tag in normalized)
            QuickTags.Add(tag);
    }

    /// <summary>快捷标签最多几个：再多布置作业那一行就摆不下了。</summary>
    public const int MaxQuickTags = 16;

    /// <summary>把一串标签收拾成能用的样子（纯逻辑、有单测）。</summary>
    public static List<string> NormalizeQuickTagList(IEnumerable<string?>? tags)
    {
        var result = new List<string>();
        foreach (var tag in tags ?? [])
        {
            var name = tag?.Trim();
            if (string.IsNullOrEmpty(name))
                continue;

            if (result.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            result.Add(name);
            if (result.Count == MaxQuickTags)
                break;
        }

        return result;
    }

    /// <summary>实际显示用的名称：用户填了就用用户的，没填就回退到资源里的默认名。</summary>
    public string ResolveBoardName()
    {
        return string.IsNullOrWhiteSpace(BoardName)
            ? Langs.Common.Resources.Board_Title
            : BoardName.Trim();
    }
}
