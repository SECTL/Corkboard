using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Export;

namespace Corkboard.Core.Models.SubConfigs.Export;

/// <summary>
///     导出设置：图片 / PDF 的格式选择、纸张与版式。
///     <para>
///         JSON 键由 <c>ConfigServiceBase.JsonOptions</c> 的 snake_case 规则生成
///         （<c>export_settings.ask_for_format</c> / <c>export_settings.pdf_page_size</c> 等），
///         枚举按数字落盘，所以只能在末尾追加枚举成员。
///     </para>
/// </summary>
public partial class ExportSettingsConfig : ObservableObject
{
    /// <summary>
    ///     点「导出」时先问一句导图片还是导 PDF。
    ///     默认开：两种格式用途差得远，不问一句很容易导错。
    /// </summary>
    [ObservableProperty] private bool _askForFormat = true;

    /// <summary>
    ///     不再询问时用的默认格式（或者用户记住的上一次选择）。
    ///     只有 <see cref="AskForFormat" /> 关掉时才会被直接用上。
    /// </summary>
    [ObservableProperty] private ExportFormat _defaultFormat = ExportFormat.Image;

    /// <summary>PDF 纸张大小。枚举按数字落盘。</summary>
    [ObservableProperty] private PdfPageSize _pdfPageSize = PdfPageSize.A4;

    /// <summary>
    ///     PDF 方向。默认自动：板子比窗口宽就横向，否则竖向——省得用户每次都要根据板子形状改。
    /// </summary>
    [ObservableProperty] private PdfOrientation _pdfOrientation = PdfOrientation.Auto;

    /// <summary>PDF 的列数（1..4）。读盘后由 <see cref="Normalize" /> 夹取。</summary>
    [ObservableProperty] private int _pdfColumns = PdfOptions.DefaultColumns;

    /// <summary>PDF 页边距档位。枚举按数字落盘。</summary>
    [ObservableProperty] private PdfMargin _pdfMargin = PdfMargin.Normal;

    /// <summary>PDF 里作业的排序方式。默认跟随板子当前的顺序。</summary>
    [ObservableProperty] private PdfSortMode _pdfSortMode = PdfSortMode.FollowBoard;

    /// <summary>
    ///     PDF 里每个科目标题是否另起一页。默认关：默认按列紧凑排，页数更少；
    ///     想要「一科一页」的人再打开。
    /// </summary>
    [ObservableProperty] private bool _pdfSubjectPageBreak;

    /// <summary>PDF 每页是否带页眉页脚（页码 / 作业板名称）。默认开。</summary>
    [ObservableProperty] private bool _pdfHeaderFooter = true;

    /// <summary>
    ///     读盘后收拾一遍：列数夹进 1..4，手改坏了的枚举值（越界数字）回落成默认值。
    ///     由 <c>MainConfigModel.NormalizeLoaded</c> 调用，导出之前配置就已经是干净的；不抛异常。
    /// </summary>
    public void Normalize()
    {
        PdfColumns = PdfOptions.GetColumnCount(PdfColumns);

        if (!Enum.IsDefined(DefaultFormat))
            DefaultFormat = ExportFormat.Image;
        if (!Enum.IsDefined(PdfPageSize))
            PdfPageSize = PdfPageSize.A4;
        if (!Enum.IsDefined(PdfOrientation))
            PdfOrientation = PdfOrientation.Auto;
        if (!Enum.IsDefined(PdfMargin))
            PdfMargin = PdfMargin.Normal;
        if (!Enum.IsDefined(PdfSortMode))
            PdfSortMode = PdfSortMode.FollowBoard;
    }
}
