using Corkboard.Core.Export;
using Corkboard.Core.Models.SubConfigs.Export;
using Corkboard.Core.Services.Config;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Export;

/// <summary>
///     导出设置页里的一个下拉项：值 + 显示名。
///     <para>
///         重写 <see cref="ToString" /> 让下拉框不需要 ItemTemplate 就能显示中文名：
///         选项类型是泛型记录，XAML 里写泛型 DataTemplate 要带 <c>x:TypeArguments</c>，反而更啰嗦。
///     </para>
/// </summary>
/// <param name="Value">落盘用的枚举 / 数字。</param>
/// <param name="DisplayName">界面上显示的文字。</param>
public sealed record ExportOption<T>(T Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
///     「导出」设置页：导出时要不要问格式、默认格式，以及 PDF 的纸张 / 方向 / 边距 / 列数 / 排序、
///     要不要每个科目另起一页、要不要页眉页脚。
///     <para>
///         下拉框都走「选中项 → 写回配置」：选项集合是固定的，配置里是越界值时由
///         <see cref="ExportSettingsConfig.Normalize" /> 在读盘时收拾掉，这里不用再兜底。
///     </para>
/// </summary>
public partial class ExportSettingsPageViewModel : ViewModelBase
{
    public ExportSettingsPageViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
    }

    /// <summary>导出设置本体，界面直接绑它的属性（自动落盘由配置管道负责）。</summary>
    public ExportSettingsConfig Settings => Config.ExportSettings;

    #region 选项集合

    public IReadOnlyList<ExportOption<ExportFormat>> FormatOptions { get; } =
    [
        new(ExportFormat.Image, CR.Export_FormatImage),
        new(ExportFormat.Pdf, CR.Export_FormatPdf)
    ];

    public IReadOnlyList<ExportOption<PdfPageSize>> PageSizeOptions { get; } =
    [
        new(PdfPageSize.A4, "A4"),
        new(PdfPageSize.A5, "A5"),
        new(PdfPageSize.Letter, "Letter")
    ];

    public IReadOnlyList<ExportOption<PdfOrientation>> OrientationOptions { get; } =
    [
        new(PdfOrientation.Auto, CR.Export_Orientation_Auto),
        new(PdfOrientation.Portrait, CR.Export_Orientation_Portrait),
        new(PdfOrientation.Landscape, CR.Export_Orientation_Landscape)
    ];

    public IReadOnlyList<ExportOption<PdfMargin>> MarginOptions { get; } =
    [
        new(PdfMargin.Narrow, CR.Export_Margin_Narrow),
        new(PdfMargin.Normal, CR.Export_Margin_Normal),
        new(PdfMargin.Wide, CR.Export_Margin_Wide)
    ];

    public IReadOnlyList<ExportOption<PdfSortMode>> SortOptions { get; } =
    [
        new(PdfSortMode.FollowBoard, CR.Export_Sort_FollowBoard),
        new(PdfSortMode.CreatedDescending, CR.Export_Sort_CreatedDescending),
        new(PdfSortMode.CreatedAscending, CR.Export_Sort_CreatedAscending),
        new(PdfSortMode.UpdatedDescending, CR.Export_Sort_UpdatedDescending),
        new(PdfSortMode.Subject, CR.Export_Sort_Subject),
        new(PdfSortMode.Fixed, CR.Export_Sort_Fixed)
    ];

    public IReadOnlyList<int> ColumnOptions { get; } = [1, 2, 3, 4];

    #endregion

    #region 选中项（写回配置）

    public ExportOption<ExportFormat> SelectedFormatOption
    {
        get => FormatOptions.First(option => option.Value == Settings.DefaultFormat);
        set
        {
            if (value is not null)
                Settings.DefaultFormat = value.Value;
        }
    }

    public ExportOption<PdfPageSize> SelectedPageSizeOption
    {
        get => PageSizeOptions.First(option => option.Value == Settings.PdfPageSize);
        set
        {
            if (value is not null)
                Settings.PdfPageSize = value.Value;
        }
    }

    public ExportOption<PdfOrientation> SelectedOrientationOption
    {
        get => OrientationOptions.First(option => option.Value == Settings.PdfOrientation);
        set
        {
            if (value is not null)
                Settings.PdfOrientation = value.Value;
        }
    }

    public ExportOption<PdfMargin> SelectedMarginOption
    {
        get => MarginOptions.First(option => option.Value == Settings.PdfMargin);
        set
        {
            if (value is not null)
                Settings.PdfMargin = value.Value;
        }
    }

    public ExportOption<PdfSortMode> SelectedSortOption
    {
        get => SortOptions.First(option => option.Value == Settings.PdfSortMode);
        set
        {
            if (value is not null)
                Settings.PdfSortMode = value.Value;
        }
    }

    public int SelectedColumns
    {
        get => PdfOptions.GetColumnCount(Settings.PdfColumns);
        set => Settings.PdfColumns = PdfOptions.GetColumnCount(value);
    }

    #endregion
}
