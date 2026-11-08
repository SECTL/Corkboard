using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.DutyRoster;
using Corkboard.Core.DutyRoster.Export;
using Corkboard.Core.DutyRoster.Import;
using Corkboard.Core.DutyRoster.Transfer;

namespace Corkboard.ViewModels.Duty;

/// <summary>导出方式（与 SecRandom-C 的四档对齐）。</summary>
public enum DutyExportMode
{
    File = 0,
    QuickQr = 1,
    OfflineQr = 2,
    SessionCode = 3
}

/// <summary>导出方式下拉的一行。</summary>
public sealed class DutyExportModeOption
{
    public DutyExportMode Mode { get; init; }

    public string Label { get; init; } = string.Empty;

    public override string ToString() => Label;
}

/// <summary>文件格式。</summary>
public enum DutyExportFormat
{
    Csv = 0,
    Xlsx = 1
}

/// <summary>文件格式下拉的一行。</summary>
public sealed class DutyExportFormatOption
{
    public DutyExportFormat Format { get; init; }

    public string Label { get; init; } = string.Empty;

    public string Extension => Format == DutyExportFormat.Xlsx ? ".xlsx" : ".csv";

    public override string ToString() => Label;
}

/// <summary>
///     名单导出抽屉：导出方式（文件 / 在线二维码 / 离线二维码 / 会话码）→ 文件格式、二维码帧或云传输状态。
///     <para>
///         只管数据与状态：选保存路径的对话框在抽屉 code-behind 里弹，自动播放的
///         <see cref="Avalonia.Threading.DispatcherTimer" /> 也在界面层——VM 只回答「下一帧是哪个」。
///         写文件、生成二维码 PNG 字节都调 <see cref="DutyRosterExporter" />，云传输调
///         <see cref="DutySyncTransferClient" />，保持可单测。
///     </para>
/// </summary>
public sealed partial class DutyExportViewModel : ObservableObject
{
    private readonly DutyRosterService _service;
    private readonly DutySyncTransferClient _cloud;
    private readonly Dictionary<int, Bitmap> _qrFrameImages = [];
    private IReadOnlyList<string> _qrFrames = [];
    private DutyCloudTransferInfo? _cloudInfo;
    private CancellationTokenSource? _cloudCts;

    public DutyExportViewModel(DutyRosterService service, DutySyncTransferClient cloud)
    {
        _service = service;
        _cloud = cloud;

        ExportModes.Add(new DutyExportModeOption { Mode = DutyExportMode.File, Label = "导出为文件" });
        ExportModes.Add(new DutyExportModeOption { Mode = DutyExportMode.QuickQr, Label = "二维码导出（在线）" });
        ExportModes.Add(new DutyExportModeOption { Mode = DutyExportMode.OfflineQr, Label = "二维码导出（离线）" });
        ExportModes.Add(new DutyExportModeOption { Mode = DutyExportMode.SessionCode, Label = "会话码导出" });
        _selectedExportMode = ExportModes[0];

        FileFormats.Add(new DutyExportFormatOption { Format = DutyExportFormat.Csv, Label = "CSV" });
        FileFormats.Add(new DutyExportFormatOption { Format = DutyExportFormat.Xlsx, Label = "XLSX" });
        _selectedFileFormat = FileFormats[0];
    }

    /// <summary>文件导出后让抽屉收起来；二维码导出不触发（用户还要继续扫码）。</summary>
    public event Action? Completed;

    /// <summary>「导出文件」按钮 → code-behind 弹保存对话框。</summary>
    public event Action? FileSaveRequested;

    /// <summary>复制会话码 / 配对链接：剪贴板是界面能力，VM 只把文本交出去。</summary>
    public event Action<string>? CopyTextRequested;

    public string Title => "导出成员名单";

    /// <summary>目标名单：空名单名显示「当前名单」。</summary>
    public string RosterName => DutyRosterExporter.ResolveRosterName(_service.Document.Source.RosterName);

    public string TargetListDescription => $"目标名单：{RosterName}";

    public string MemberSummary => $"共 {_service.Document.Members.Count} 人";

    // ---- 导出方式 ----

    public ObservableCollection<DutyExportModeOption> ExportModes { get; } = [];

    [ObservableProperty] private DutyExportModeOption _selectedExportMode;

    partial void OnSelectedExportModeChanged(DutyExportModeOption value)
    {
        ErrorText = string.Empty;
        CancelCloud(revoke: true);
        ResetCloudState();
        ResetQrState();

        switch (value?.Mode)
        {
            case DutyExportMode.OfflineQr:
                StartQrSession();
                break;
            case DutyExportMode.QuickQr:
                StatusText = "正在生成配对二维码…";
                _ = StartCloudTransferAsync(DutyCloudTransferMode.QuickQr);
                break;
            case DutyExportMode.SessionCode:
                StatusText = "正在生成会话码…";
                _ = StartCloudTransferAsync(DutyCloudTransferMode.SessionCode);
                break;
            default:
                StatusText = "选好格式，导出成表格文件";
                break;
        }

        OnPropertyChanged(nameof(IsFileMode));
        OnPropertyChanged(nameof(IsQrMode));
        OnPropertyChanged(nameof(IsCloudMode));
        OnPropertyChanged(nameof(IsQuickQrMode));
        OnPropertyChanged(nameof(IsSessionCodeMode));
    }

    public bool IsFileMode => SelectedExportMode?.Mode == DutyExportMode.File;

    /// <summary>离线二维码（逐帧显示，不需要网络）。</summary>
    public bool IsQrMode => SelectedExportMode?.Mode == DutyExportMode.OfflineQr;

    public bool IsQuickQrMode => SelectedExportMode?.Mode == DutyExportMode.QuickQr;

    public bool IsSessionCodeMode => SelectedExportMode?.Mode == DutyExportMode.SessionCode;

    public bool IsCloudMode => IsQuickQrMode || IsSessionCodeMode;

    // ---- 通用状态 ----

    [ObservableProperty] private string _statusText = "选择导出方式，然后导出名单";

    [ObservableProperty] private string _errorText = string.Empty;

    // ---- 文件 ----

    public ObservableCollection<DutyExportFormatOption> FileFormats { get; } = [];

    [ObservableProperty] private DutyExportFormatOption _selectedFileFormat;

    partial void OnSelectedFileFormatChanged(DutyExportFormatOption value)
    {
        OnPropertyChanged(nameof(SuggestedFileName));
    }

    /// <summary>保存对话框里的默认文件名（名单名 + 当前格式后缀）。</summary>
    public string SuggestedFileName => $"{SanitizeFileName(RosterName)}{SelectedFileFormat?.Extension ?? ".csv"}";

    // ---- 二维码 ----

    [ObservableProperty] private string _qrSessionId = string.Empty;

    [ObservableProperty] private int _qrFrameCount;

    [ObservableProperty] private int _frameIndex;

    [ObservableProperty] private Bitmap? _qrImage;

    [ObservableProperty] private bool _isAutoPlaying;

    partial void OnIsAutoPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(AutoPlayText));
    }

    public string QrFrameText => QrFrameCount == 0
        ? "还没有生成二维码"
        : $"第 {FrameIndex + 1} / {QrFrameCount} 帧";

    public double QrProgressPercent => QrFrameCount == 0
        ? 0
        : (FrameIndex + 1) * 100d / QrFrameCount;

    public bool HasQrFrames => QrFrameCount > 0;

    public bool CanGoPrevious => IsQrMode && FrameIndex > 0;

    public bool CanGoNext => IsQrMode && FrameIndex + 1 < QrFrameCount;

    public string AutoPlayText => IsAutoPlaying ? "暂停" : "自动播放";

    public string QrHint => "手机端用 SecRandom 的扫码导入接收";

    partial void OnFrameIndexChanged(int value)
    {
        UpdateFramePresentation();
    }

    partial void OnQrFrameCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasQrFrames));
        OnPropertyChanged(nameof(QrFrameText));
        OnPropertyChanged(nameof(QrProgressPercent));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    // ---- 云传输（在线二维码 / 会话码） ----

    [ObservableProperty] private bool _isCloudBusy;

    [ObservableProperty] private string _cloudStatusText = string.Empty;

    [ObservableProperty] private string _cloudExpiresText = string.Empty;

    [ObservableProperty] private int _cloudRecordCount;

    [ObservableProperty] private Bitmap? _pairingQrImage;

    [ObservableProperty] private string _pairingUrl = string.Empty;

    [ObservableProperty] private string _sessionCodeText = string.Empty;

    /// <summary>云传输已经生成好了（有配对链接或会话码）。</summary>
    public bool HasCloudTransfer => _cloudInfo is not null;

    public bool CanRefreshCloudTransfer => IsCloudMode && !IsCloudBusy;

    partial void OnIsCloudBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefreshCloudTransfer));
    }

    public string QuickQrHint => "用手机 SecRandom 扫码接收；二维码 30 分钟内有效，关掉抽屉就撤销";

    public string SessionCodeHint => "把 12 位会话码告诉接收方，在导入里选「会话码导入」；30 分钟内有效";

    [RelayCommand]
    private void RefreshCloudTransfer()
    {
        if (!IsCloudMode || IsCloudBusy)
            return;

        CancelCloud(revoke: true);
        ResetCloudState();
        StatusText = IsQuickQrMode ? "正在生成配对二维码…" : "正在生成会话码…";
        _ = StartCloudTransferAsync(IsQuickQrMode ? DutyCloudTransferMode.QuickQr : DutyCloudTransferMode.SessionCode);
    }

    [RelayCommand]
    private void CopySessionCode()
    {
        if (!string.IsNullOrEmpty(SessionCodeText))
            CopyTextRequested?.Invoke(SessionCodeText);
    }

    [RelayCommand]
    private void CopyPairingUrl()
    {
        if (!string.IsNullOrEmpty(PairingUrl))
            CopyTextRequested?.Invoke(PairingUrl);
    }

    private async Task StartCloudTransferAsync(DutyCloudTransferMode mode)
    {
        var cts = new CancellationTokenSource();
        _cloudCts = cts;
        IsCloudBusy = true;
        ErrorText = string.Empty;

        try
        {
            var members = _service.Document.Members;
            if (members.Count == 0)
            {
                CloudStatusText = "名单里还没有成员，传不了";
                StatusText = CloudStatusText;
                return;
            }

            var groups = _service.Document.Groups.ToDictionary(group => group.Id);
            var document = DutyRosterExporter.BuildTransferDocument(members, groups, RosterName);
            var info = await _cloud.CreateAsync(document, mode, cancellationToken: cts.Token);

            if (cts.IsCancellationRequested)
            {
                await RevokeQuietlyAsync(info);
                return;
            }

            _cloudInfo = info;
            CloudRecordCount = info.RecordCount;
            CloudExpiresText = $"有效期到 {info.ExpiresAt.LocalDateTime:yyyy-MM-dd HH:mm}";

            if (mode == DutyCloudTransferMode.QuickQr)
            {
                PairingUrl = info.PairingUrl ?? string.Empty;
                PairingQrImage = info.PairingUrl is null
                    ? null
                    : ToBitmap(DutyRosterExporter.CreateQrPng(info.PairingUrl));
                CloudStatusText = "配对二维码已生成，等手机扫码接收";
            }
            else
            {
                SessionCodeText = info.SessionCode ?? string.Empty;
                CloudStatusText = "会话码已生成，等接收方输入";
            }

            StatusText = CloudStatusText;
            OnPropertyChanged(nameof(HasCloudTransfer));
            OnPropertyChanged(nameof(CanRefreshCloudTransfer));
        }
        catch (OperationCanceledException)
        {
            // 切走模式或关抽屉时取消：不报错。
        }
        catch (DutyTransferException exception)
        {
            CloudStatusText = "生成失败，可以点「重新生成」再试";
            StatusText = CloudStatusText;
            ErrorText = DescribeTransferError(exception);
        }
        catch (Exception exception)
        {
            CloudStatusText = "生成失败，可以点「重新生成」再试";
            StatusText = CloudStatusText;
            ErrorText = $"云传输失败：{exception.Message}";
        }
        finally
        {
            // 只有当前这轮才是「忙」的持有者：切模式时旧任务收尾不能把新任务的状态抹掉。
            if (ReferenceEquals(_cloudCts, cts))
            {
                _cloudCts = null;
                IsCloudBusy = false;
                cts.Dispose();
            }
        }
    }

    private void CancelCloud(bool revoke)
    {
        _cloudCts?.Cancel();
        var info = _cloudInfo;
        _cloudInfo = null;
        OnPropertyChanged(nameof(HasCloudTransfer));
        OnPropertyChanged(nameof(CanRefreshCloudTransfer));

        if (revoke && info is not null)
            _ = RevokeQuietlyAsync(info);
    }

    private void ResetCloudState()
    {
        PairingQrImage?.Dispose();
        PairingQrImage = null;
        PairingUrl = string.Empty;
        SessionCodeText = string.Empty;
        CloudStatusText = string.Empty;
        CloudExpiresText = string.Empty;
        CloudRecordCount = 0;
        OnPropertyChanged(nameof(HasCloudTransfer));
    }

    private async Task RevokeQuietlyAsync(DutyCloudTransferInfo info)
    {
        try
        {
            await _cloud.RevokeAsync(info, CancellationToken.None);
        }
        catch
        {
            // 撤销失败没关系：服务端到期会自动清掉。
        }
    }

    private static Bitmap ToBitmap(byte[] png) => new(new MemoryStream(png));

    private static string DescribeTransferError(DutyTransferException exception) => exception.Error switch
    {
        DutyTransferError.Network => "连不上云服务，检查网络后再试",
        DutyTransferError.InvalidSessionCode => "会话码无效或已过期",
        DutyTransferError.InvalidPairingCode => "配对链接无效或已过期",
        DutyTransferError.InvalidPayload => "云传输内容无效，可能两边版本不一致",
        _ => "云服务暂时不可用，稍后再试"
    };

    // ---- 文件导出 ----

    [RelayCommand]
    private void ExportFile()
    {
        if (!IsFileMode)
            return;

        ErrorText = string.Empty;
        FileSaveRequested?.Invoke();
    }

    /// <summary>按路径写文件（扩展名决定格式；没有扩展名就补当前格式的后缀）。</summary>
    public void SaveFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            var target = EnsureExtension(path);
            using var stream = File.Create(target);
            WriteExport(stream, target);
            MarkFileExported();
        }
        catch (Exception ex)
        {
            SetExportFailure(ex);
        }
    }

    /// <summary>写到一个调用方给的流（浏览器 / 移动端的 StorageFile 走这条路）。</summary>
    public void SaveFile(Stream output, string fileName)
    {
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            WriteExport(output, fileName);
            MarkFileExported();
        }
        catch (Exception ex)
        {
            SetExportFailure(ex);
        }
    }

    private void WriteExport(Stream output, string fileName)
    {
        var members = _service.Document.Members;
        var groups = _service.Document.Groups.ToDictionary(group => group.Id);

        var bytes = ResolveFormat(fileName) == DutyExportFormat.Xlsx
            ? DutyRosterExporter.BuildXlsx(members, groups, RosterName, DateTime.Now)
            : DutyRosterExporter.BuildCsvBytes(members, groups);

        output.Write(bytes, 0, bytes.Length);
        output.Flush();
    }

    private void MarkFileExported()
    {
        ErrorText = string.Empty;
        StatusText = "已导出文件";
        Completed?.Invoke();
    }

    private void SetExportFailure(Exception ex)
    {
        StatusText = "导出失败";
        ErrorText = $"导出失败：{ex.Message}";
    }

    private DutyExportFormat ResolveFormat(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".xlsx" => DutyExportFormat.Xlsx,
            ".csv" => DutyExportFormat.Csv,
            _ => SelectedFileFormat?.Format ?? DutyExportFormat.Csv
        };

    private string EnsureExtension(string path)
    {
        var extension = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(extension))
            return path;

        return path + (SelectedFileFormat?.Extension ?? ".csv");
    }

    // ---- 二维码帧 ----

    private void StartQrSession()
    {
        var members = _service.Document.Members;
        var groups = _service.Document.Groups.ToDictionary(group => group.Id);

        if (members.Count == 0)
        {
            ResetQrState();
            StatusText = "名单里还没有成员，生成不了二维码";
            return;
        }

        QrSessionId = OfflineQrProtocol.CreateSessionId();
        _qrFrames = DutyRosterExporter.BuildQrFrames(members, groups, RosterName, QrSessionId);

        QrImage = null;
        ReleaseFrameImages();
        QrFrameCount = _qrFrames.Count;
        FrameIndex = 0;
        IsAutoPlaying = false;
        UpdateFramePresentation();

        StatusText = QrFrameCount == 0
            ? "名单里还没有成员，生成不了二维码"
            : $"已生成 {QrFrameCount} 帧，用手机逐帧扫码接收";
    }

    private void ResetQrState()
    {
        IsAutoPlaying = false;
        _qrFrames = [];
        QrFrameCount = 0;
        FrameIndex = 0;
        QrSessionId = string.Empty;
        QrImage = null;
        ReleaseFrameImages();
        UpdateFramePresentation();
    }

    [RelayCommand]
    private void PreviousFrame()
    {
        if (!IsQrMode)
            return;

        SetFrame(FrameIndex - 1);
        IsAutoPlaying = false;
    }

    [RelayCommand]
    private void NextFrame()
    {
        if (!IsQrMode)
            return;

        SetFrame(FrameIndex + 1);
    }

    [RelayCommand]
    private void ToggleAutoPlay()
    {
        if (!IsQrMode || QrFrameCount == 0)
            return;

        if (IsAutoPlaying)
        {
            IsAutoPlaying = false;
            return;
        }

        // 停在最后一帧时再点「自动播放」：从第一帧重新播。
        if (!CanGoNext)
            SetFrame(0);

        IsAutoPlaying = true;
    }

    /// <summary>
    ///     自动播放定时器每跳一次调一次；走到最后一帧就停。
    ///     返回 <c>false</c> 表示已经播完（界面层可以据此停表）。
    /// </summary>
    public bool AdvanceAutoFrame()
    {
        if (!IsAutoPlaying)
            return false;

        if (!CanGoNext)
        {
            IsAutoPlaying = false;
            return false;
        }

        SetFrame(FrameIndex + 1);

        if (!CanGoNext)
        {
            IsAutoPlaying = false;
            return false;
        }

        return true;
    }

    /// <summary>离开抽屉时释放缓存的二维码位图，并撤销还挂着的云传输。</summary>
    public void ReleaseResources()
    {
        IsAutoPlaying = false;
        QrImage = null;
        ReleaseFrameImages();
        CancelCloud(revoke: true);
        ResetCloudState();
    }

    private void SetFrame(int index)
    {
        if (_qrFrames.Count == 0)
            return;

        FrameIndex = Math.Clamp(index, 0, _qrFrames.Count - 1);
    }

    private void UpdateFramePresentation()
    {
        QrImage = _qrFrames.Count == 0 ? null : GetFrameImage(FrameIndex);
        NotifyFrameProperties();
    }

    private void NotifyFrameProperties()
    {
        OnPropertyChanged(nameof(QrFrameText));
        OnPropertyChanged(nameof(QrProgressPercent));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    private Bitmap GetFrameImage(int index)
    {
        if (_qrFrameImages.TryGetValue(index, out var cached))
            return cached;

        var png = DutyRosterExporter.CreateQrPng(_qrFrames[index]);
        var bitmap = new Bitmap(new MemoryStream(png));
        _qrFrameImages[index] = bitmap;
        return bitmap;
    }

    private void ReleaseFrameImages()
    {
        foreach (var bitmap in _qrFrameImages.Values)
            bitmap.Dispose();

        _qrFrameImages.Clear();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var cleaned = new string(chars).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "值日名单" : cleaned;
    }
}