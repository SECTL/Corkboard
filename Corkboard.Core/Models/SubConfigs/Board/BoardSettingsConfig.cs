using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.Board;

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

    /// <summary>便签排序方式。改它的入口在主界面标题栏的排序按钮（设置里只有排布这类展示设置）。</summary>
    [ObservableProperty] private BoardSortMode _sortMode = BoardSortMode.CreatedDescending;

    /// <summary>删除便签前是否弹确认对话框。</summary>
    [ObservableProperty] private bool _confirmBeforeDelete = true;

    /// <summary>
    ///     是否按下面的规则自动清理过期作业。关掉之后只剩设置页里的「立即清理」这一个入口。
    ///     默认开：清理只是把作业从板子上拿掉，数据不删、随时能在时空回放里看到，
    ///     关着反而会让人以为功能坏了。
    /// </summary>
    [ObservableProperty] private bool _cleanupEnabled = true;

    /// <summary>清理频率。枚举按数字落盘，只能在末尾增成员。</summary>
    [ObservableProperty] private BoardCleanupFrequency _cleanupFrequency = BoardCleanupFrequency.Daily;

    /// <summary>每周清理时定在星期几动手（<see cref="BoardCleanupFrequency.Daily" /> 下忽略）。</summary>
    [ObservableProperty] private DayOfWeek _cleanupWeekday = DayOfWeek.Monday;

    /// <summary>动手的时刻（整点 + 分钟拆成两个字段存，免得为 <see cref="TimeOnly" /> 再加一个序列化器）。</summary>
    [ObservableProperty] private int _cleanupHour = BoardCleanupDefaults.DefaultHour;

    /// <summary>动手时刻的分钟部分。</summary>
    [ObservableProperty] private int _cleanupMinute = BoardCleanupDefaults.DefaultMinute;

    /// <summary>
    ///     作业内容的默认字号：新布置的作业没单独定「整篇字号」时用它，
    ///     局部字号标注则是在它之上的覆盖。
    /// </summary>
    [ObservableProperty] private double _defaultContentFontSize = BoardContentStyle.DefaultFontSize;

    /// <summary>
    ///     作业内容的默认颜色。为空表示跟随主题前景色（深色模式下才看得清）；
    ///     局部颜色标注是在它之上的覆盖。
    /// </summary>
    [ObservableProperty] private Color? _defaultContentColor;

    /// <summary>
    ///     选中文字后弹出的工具浮窗上那排预设色。最多 <see cref="BoardPalette.MaxColors" /> 个，
    ///     在「设置 → 作业板 → 内容格式」里增删改。
    ///     <para>
    ///         读写都该走 <see cref="ResolvePaletteColors" /> / <see cref="ApplyPaletteColors" />：
    ///         前者会把配置收拾干净（去透明、去重复、截断、空了回退默认），
    ///         后者负责把列表原地同步成给定内容。
    ///     </para>
    /// </summary>
    public ObservableCollection<Color> PaletteColors { get; set; } = [];

    /// <summary>取收拾干净的色板，给浮窗和设置页用。</summary>
    public List<Color> ResolvePaletteColors() => BoardPalette.Normalize(PaletteColors);

    /// <summary>
    ///     把色板原地替换成给定内容。就地增删改而不是换一个新集合，
    ///     因为 <see cref="ObservableCollection{T}" /> 已经绑在设置页的列表上了，整个换掉会断掉绑定。
    /// </summary>
    public void ApplyPaletteColors(IEnumerable<Color> colors)
    {
        var normalized = BoardPalette.Normalize(colors);

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
        var normalized = BoardPalette.Normalize(PaletteColors);
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
        CleanupHour = BoardCleanupDefaults.ClampHour(CleanupHour);
        CleanupMinute = BoardCleanupDefaults.ClampMinute(CleanupMinute);
    }

    /// <summary>实际显示用的名称：用户填了就用用户的，没填就回退到资源里的默认名。</summary>
    public string ResolveBoardName()
    {
        return string.IsNullOrWhiteSpace(BoardName)
            ? Langs.Common.Resources.Board_Title
            : BoardName.Trim();
    }
}
