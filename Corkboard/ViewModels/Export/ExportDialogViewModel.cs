using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Export;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Ui;

namespace Corkboard.ViewModels.Export;

/// <summary>
///     导出对话框返回给调用方的选择。
///     <para>
///         <paramref name="NoteIds" /> 目前恒为 <c>null</c>（只导出全部作业）；保留这个字段是为了以后
///         真需要「只导出一部分」时不用再改调用方。
///     </para>
/// </summary>
/// <param name="Format">图片还是 PDF。</param>
/// <param name="NoteIds">要导出的作业；<c>null</c> 表示全部。</param>
/// <param name="CopyToClipboard">只把图片复制到剪贴板、不落盘（用户点的是「复制图片」）。</param>
public sealed record ExportDialogResult(
    ExportFormat Format,
    IReadOnlyList<Guid>? NoteIds,
    bool CopyToClipboard = false);

/// <summary>
///     「导出作业板」对话框的 ViewModel：只问导图片还是 PDF（范围固定全部作业）。
///     <para>
///         对话框只负责问，不负责导出：用户点「下一步」后把 <see cref="ExportDialogResult" />
///         交给作业板页面，由它去排版 / 渲染 / 弹保存框。
///     </para>
///     <para>
///         「以后直接导出这个格式」勾上时把 <c>ExportSettings.AskForFormat</c> 关掉并记住这次格式，
///         下次点导出就不再问格式。
///     </para>
/// </summary>
public partial class ExportDialogViewModel : ViewModelBase
{
    private readonly PageOverlayService _overlay;
    private readonly TaskCompletionSource<ExportDialogResult?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>对话框关掉时完成；<c>null</c> 表示用户取消。</summary>
    public Task<ExportDialogResult?> Completion => _completion.Task;

    /// <summary>「以后直接导出这个格式」。</summary>
    [ObservableProperty] private bool _rememberChoice;

    /// <summary>当前选中的格式。</summary>
    [ObservableProperty] private ExportFormat _format;

    /// <summary>图片格式是否被选中（模板用它标高亮）。</summary>
    [ObservableProperty] private bool _isImageSelected;

    /// <summary>PDF 格式是否被选中。</summary>
    [ObservableProperty] private bool _isPdfSelected;

    /// <summary>构造：默认格式取设置里记的那个。</summary>
    /// <param name="configHandler">主配置（只用来读 / 写导出设置）。</param>
    /// <param name="overlay">弹层宿主，关闭对话框用。</param>
    public ExportDialogViewModel(MainConfigHandler configHandler, PageOverlayService overlay)
        : base(configHandler)
    {
        _overlay = overlay;

        Format = Config.ExportSettings.DefaultFormat;
        ApplyFormat(Format);
    }

    /// <summary>切换格式：图片。</summary>
    [RelayCommand]
    private void SelectImage() => ApplyFormat(ExportFormat.Image);

    /// <summary>切换格式：PDF。</summary>
    [RelayCommand]
    private void SelectPdf() => ApplyFormat(ExportFormat.Pdf);

    private void ApplyFormat(ExportFormat format)
    {
        Format = format;
        IsImageSelected = format == ExportFormat.Image;
        IsPdfSelected = format == ExportFormat.Pdf;
    }

    /// <summary>点「下一步」：把格式交给调用方（范围固定全部作业）。</summary>
    [RelayCommand]
    private void Confirm() => Complete(BuildResult(Format, copyToClipboard: false));

    /// <summary>点「复制图片」：按全部作业渲染成图片放进剪贴板，不落盘。</summary>
    [RelayCommand]
    private void CopyImage() => Complete(BuildResult(ExportFormat.Image, copyToClipboard: true));

    private ExportDialogResult BuildResult(ExportFormat format, bool copyToClipboard)
    {
        // 只要这次点的是某个格式，就把它记成下次打开对话框时的默认格式
        // （用户选了图片、下次再点导出却还是 PDF，就是因为之前只在勾「以后直接导出」时才记）。
        Config.ExportSettings.DefaultFormat = format;

        if (RememberChoice)
            Config.ExportSettings.AskForFormat = false;

        return new ExportDialogResult(format, NoteIds: null, copyToClipboard);
    }

    private void Complete(ExportDialogResult result)
    {
        _overlay.Close();
        _completion.TrySetResult(result);
    }

    /// <summary>点「取消」：什么都不导出。</summary>
    [RelayCommand]
    private void Cancel()
    {
        _overlay.Close();
        _completion.TrySetResult(null);
    }

    /// <summary>弹层被宿主关掉（比如按 Esc）时把任务收掉，别让调用方一直等。</summary>
    public void OnClosedByHost()
    {
        _completion.TrySetResult(null);
    }
}
