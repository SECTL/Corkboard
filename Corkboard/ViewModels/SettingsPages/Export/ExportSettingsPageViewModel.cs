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
///     「导出」设置页：导出时要不要问格式、默认格式，以及表格怎么排——按不按科目分块、
///     要不要带上已清理的作业、按什么顺序。
///     <para>
///         下拉框都走「选中项 → 写回配置」：选项集合是固定的，配置里是越界值时由
///         <see cref="ExportSettingsConfig.Normalize" /> 在读盘时收拾掉，这里不用再兜底。
///         两个开关直接在 XAML 里双向绑配置（布尔量是离散的，不存在「敲一个字落一次盘」的问题）。
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
        new(ExportFormat.Table, CR.Export_FormatTable)
    ];

    public IReadOnlyList<ExportOption<ExportSortMode>> SortOptions { get; } =
    [
        new(ExportSortMode.FollowBoard, CR.Export_Sort_FollowBoard),
        new(ExportSortMode.CreatedDescending, CR.Export_Sort_CreatedDescending),
        new(ExportSortMode.CreatedAscending, CR.Export_Sort_CreatedAscending),
        new(ExportSortMode.UpdatedDescending, CR.Export_Sort_UpdatedDescending),
        new(ExportSortMode.Subject, CR.Export_Sort_Subject),
        new(ExportSortMode.Fixed, CR.Export_Sort_Fixed)
    ];

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

    public ExportOption<ExportSortMode> SelectedSortOption
    {
        get => SortOptions.First(option => option.Value == Settings.TableSortMode);
        set
        {
            if (value is not null)
                Settings.TableSortMode = value.Value;
        }
    }

    #endregion
}
