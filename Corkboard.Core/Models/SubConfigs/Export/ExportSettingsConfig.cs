using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Export;

namespace Corkboard.Core.Models.SubConfigs.Export;

/// <summary>
///     导出设置：导图片还是导表格，以及表格怎么排。
///     <para>
///         JSON 键由 <c>ConfigServiceBase.JsonOptions</c> 的 snake_case 规则生成
///         （<c>export_settings.ask_for_format</c> / <c>export_settings.table_group_by_subject</c> 等），
///         枚举按数字落盘，所以只能在末尾追加枚举成员。
///     </para>
///     <para>
///         旧的 <c>pdf_*</c> 键已随「导出 PDF」一起删掉；读盘时它们不会被认，
///         留在文件里也只会被下一次落盘清掉（配置模型里没有对应属性）。
///     </para>
/// </summary>
public partial class ExportSettingsConfig : ObservableObject
{
    /// <summary>
    ///     点「导出」时先问一句导图片还是导表格。
    ///     默认开：两种格式用途差得远，不问一句很容易导错。
    /// </summary>
    [ObservableProperty] private bool _askForFormat = true;

    /// <summary>
    ///     不再询问时用的默认格式（或者用户记住的上一次选择）。
    ///     只有 <see cref="AskForFormat" /> 关掉时才会被直接用上。
    /// </summary>
    [ObservableProperty] private ExportFormat _defaultFormat = ExportFormat.Image;

    /// <summary>
    ///     表格是否按科目分块：每个科目先来一行组标题，再排这个科目的作业。
    ///     默认开——分块之后一眼能看出「哪个科目的还欠着」，比一条条混排好读。
    /// </summary>
    [ObservableProperty] private bool _tableGroupBySubject = true;

    /// <summary>
    ///     表格里要不要连已清理（收起来的）作业一起写。
    ///     默认关：导出的是「现在板上还剩什么」，翻旧账的人再打开。
    /// </summary>
    [ObservableProperty] private bool _tableIncludeCleaned;

    /// <summary>表格里作业的排序方式。默认跟随板子当前的顺序。</summary>
    [ObservableProperty] private ExportSortMode _tableSortMode = ExportSortMode.FollowBoard;

    /// <summary>
    ///     读盘后收拾一遍：手改坏了的枚举值（越界数字）回落成默认值。
    ///     由 <c>MainConfigModel.NormalizeLoaded</c> 调用，导出之前配置就已经是干净的；不抛异常。
    /// </summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(DefaultFormat))
            DefaultFormat = ExportFormat.Image;
        if (!Enum.IsDefined(TableSortMode))
            TableSortMode = ExportSortMode.FollowBoard;
    }
}
