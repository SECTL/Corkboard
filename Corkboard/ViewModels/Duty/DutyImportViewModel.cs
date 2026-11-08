using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.DutyRoster;
using Corkboard.Core.DutyRoster.Import;
using Corkboard.Core.DutyRoster.Transfer;

namespace Corkboard.ViewModels.Duty;

/// <summary>可选的列（第一项是「无」）。</summary>
public sealed class DutyColumnOption
{
    /// <summary>表格里的列名；null 表示不导入。</summary>
    public string? Column { get; init; }

    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>导入来源。</summary>
public enum DutyImportMode
{
    File = 0,
    QuickQr = 1,
    OfflineQr = 2,
    SessionCode = 3
}

/// <summary>来源下拉的一行。</summary>
public sealed class DutyImportModeOption
{
    public DutyImportMode Mode { get; init; }

    public string Label { get; init; } = string.Empty;

    public override string ToString() => Label;
}

/// <summary>预览表的一行。</summary>
public sealed class DutyPreviewRow
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Gender { get; init; } = string.Empty;
}

/// <summary>
///     名单导入抽屉：来源（文件 / 粘贴 / 扫码 / 会话码）→ 导入区域 → 列映射 → 预览 → 整表覆盖导入。
///     <para>
///         照 SecRandom-C 的名单导入抽屉对齐；扫码走相机（平台层抓帧、Core 解码），
///         会话码走 SecRandom Sync 云传输；文件里的小组 / 标签列不参与导入（映射只留序号、名称、性别三列）。
///     </para>
///     <para>
///         这里只管状态与解析：选文件对话框、覆盖确认框在抽屉 code-behind 里弹；
///         区域选择器是独立控件 <c>DutyImportRegionSelector</c>，由 code-behind 把网格与选区在两边搬。
///     </para>
/// </summary>
public sealed partial class DutyImportViewModel : ObservableObject
{
    /// <summary>「不导入」在列下拉里的显示名。</summary>
    public const string NoColumn = "无";

    private readonly DutyRosterService _service;
    private readonly DutySyncTransferClient _cloud;
    private RosterImportGrid? _grid;
    private RosterImportTable _table = RosterImportTable.Empty;
    private IReadOnlyList<DutyMember> _pendingMembers = [];
    private OfflineQrFrameAccumulator? _qr;
    private string? _lastFilePath;
    private IReadOnlyList<string> _sheetNames = [];
    private string? _selectedSheetName;
    private bool _isUpdatingColumns;
    private bool _isNormalizingSessionCode;
    private bool _fetchingSession;
    private string? _fetchedSessionCode;
    private CancellationTokenSource? _sessionCts;
    private string? _tableImportMethod;
    private string? _tableSessionId;

    public DutyImportViewModel(DutyRosterService service, DutySyncTransferClient cloud)
    {
        _service = service;
        _cloud = cloud;

        ImportModes.Add(new DutyImportModeOption { Mode = DutyImportMode.File, Label = "Excel / CSV 文件" });
        ImportModes.Add(new DutyImportModeOption { Mode = DutyImportMode.QuickQr, Label = "扫码快速导入" });
        ImportModes.Add(new DutyImportModeOption { Mode = DutyImportMode.OfflineQr, Label = "无网扫码导入" });
        ImportModes.Add(new DutyImportModeOption { Mode = DutyImportMode.SessionCode, Label = "会话码导入" });
        _selectedImportMode = ImportModes[0];

        ColumnOptions.Add(new DutyColumnOption { Column = null, Name = NoColumn });
    }

    // ---- 对外事件（抽屉 code-behind 用） ----

    /// <summary>需要弹系统文件选择器。</summary>
    public event EventHandler? FilePickRequested;

    /// <summary>网格换了（选文件 / 换工作表 / 粘贴 / 扫完码），抽屉要把新网格塞进区域选择器。</summary>
    public event EventHandler? RegionSourceChanged;

    /// <summary>名单已经写回 <see cref="DutyRosterService" />，抽屉应关闭。</summary>
    public event EventHandler? Completed;

    // ---- 标题区 ----

    public string Title => "导入成员名单";

    public string TargetListDescription => $"目标名单：{ResolveRosterName()}";

    public string Description => "导入是整表覆盖：当前名单的内容会被这次导入的结果替换，名称与序号一致的成员沿用原来的记录";

    public int ExistingMemberCount => _service.Document.Members.Count;

    // ---- 来源 ----

    public ObservableCollection<DutyImportModeOption> ImportModes { get; } = [];

    [ObservableProperty] private DutyImportModeOption _selectedImportMode;

    partial void OnSelectedImportModeChanged(DutyImportModeOption value)
    {
        ErrorText = string.Empty;
        if (value?.Mode != DutyImportMode.SessionCode)
            CancelSessionFetch();

        OnPropertyChanged(nameof(IsFileMode));
        OnPropertyChanged(nameof(IsQuickQrMode));
        OnPropertyChanged(nameof(IsOfflineQrMode));
        OnPropertyChanged(nameof(IsQrMode));
        OnPropertyChanged(nameof(IsSessionCodeMode));
        OnPropertyChanged(nameof(IsRegionVisible));
        OnPropertyChanged(nameof(QrModeDescription));
        OnPropertyChanged(nameof(SessionCanFetch));
        OnPropertyChanged(nameof(CanImport));
    }

    public bool IsFileMode => SelectedImportMode?.Mode == DutyImportMode.File;

    public bool IsQuickQrMode => SelectedImportMode?.Mode == DutyImportMode.QuickQr;

    public bool IsOfflineQrMode => SelectedImportMode?.Mode == DutyImportMode.OfflineQr;

    public bool IsQrMode => IsQuickQrMode || IsOfflineQrMode;

    public bool IsSessionCodeMode => SelectedImportMode?.Mode == DutyImportMode.SessionCode;

    /// <summary>区域选择器只在文件模式、且已经读进一张表时出现。</summary>
    public bool IsRegionVisible => IsFileMode && Grid is not null;

    /// <summary>区域选择器拿到网格（没有表时为 null）。</summary>
    public RosterImportGrid? Grid => _grid;

    /// <summary>是否已经有一张可映射的表（文件选区完成或二维码拼装完成）。</summary>
    public bool HasTable => !_table.IsEmpty;

    public IReadOnlyList<string> SheetNames => _sheetNames;

    public string? SelectedSheetName => _selectedSheetName;

    public bool HasMultipleSheets => _sheetNames.Count > 1;

    [RelayCommand]
    private void PickFile()
    {
        FilePickRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---- 文件 ----

    [ObservableProperty] private string? _selectedFileName;

    public string FileNameText => string.IsNullOrWhiteSpace(SelectedFileName) ? "尚未选择文件" : SelectedFileName;

    partial void OnSelectedFileNameChanged(string? value)
    {
        OnPropertyChanged(nameof(FileNameText));
    }

    public string FileHint => "支持 .xlsx、.xls、.csv；导入区域可自行选择";

    public string LegacyXlsHint => "选择 .xls 后请先在 Excel 里另存为 .xlsx 或 .csv";

    // ---- 粘贴 ----

    [ObservableProperty] private string _pasteText = string.Empty;

    public string PasteHint => "也可以直接把表格里的内容粘贴到这里，支持 Tab、分号或逗号分隔";

    // ---- 二维码 ----

    [ObservableProperty] private string _qrFrameText = string.Empty;

    [ObservableProperty] private string _qrProgressText = "已收 0 / 共 0 帧";

    [ObservableProperty] private string _qrSessionId = "—";

    [ObservableProperty] private bool _qrCanComplete;

    [ObservableProperty] private string _qrStatusText = string.Empty;

    /// <summary>本平台能不能用相机扫码（由抽屉按平台能力设置；不支持时整块相机入口隐藏）。</summary>
    [ObservableProperty] private bool _isCameraSupported;

    /// <summary>相机状态（已打开、打不开、识别不到谁的二维码……）。</summary>
    [ObservableProperty] private string _cameraStatusText = string.Empty;

    public string CameraHint => "把二维码放进取景框里，识别到就自动收下";

    public string QrHint => "把 SecRandom 分享的整份名单二维码逐帧粘贴（或扫码后把文本输入）进来，收齐后点「拼成名单」";

    public string QrModeDescription => IsOfflineQrMode
        ? "无网二维码：SecRandom 在无网模式下生成的帧，用相机逐帧扫，或者把文本贴进来"
        : "扫码快速导入：对着导出方屏幕上的配对二维码扫一下即可取回名单";

    // ---- 会话码（云传输） ----

    [ObservableProperty] private string _sessionCode = string.Empty;

    [ObservableProperty] private bool _isSessionBusy;

    [ObservableProperty] private string _sessionStatusText = "输入导出方显示的 12 位会话码，满了会自动取回";

    public bool SessionCanFetch => DutySessionCode.IsValid(SessionCode) && !IsSessionBusy;

    partial void OnIsSessionBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(SessionCanFetch));
    }

    partial void OnSessionCodeChanged(string value)
    {
        if (_isNormalizingSessionCode)
            return;

        var normalized = DutySessionCode.Normalize(value);
        if (!string.Equals(normalized, value, StringComparison.Ordinal))
        {
            _isNormalizingSessionCode = true;
            SessionCode = normalized;
            _isNormalizingSessionCode = false;
            return;
        }

        OnPropertyChanged(nameof(SessionCanFetch));

        if (DutySessionCode.IsValid(normalized) && !string.Equals(normalized, _fetchedSessionCode, StringComparison.Ordinal))
            _ = FetchSessionAsync(normalized);
    }

    [RelayCommand]
    private void FetchSessionCode()
    {
        var normalized = DutySessionCode.Normalize(SessionCode);
        if (!DutySessionCode.IsValid(normalized) || IsSessionBusy)
            return;

        _fetchedSessionCode = null;
        _ = FetchSessionAsync(normalized);
    }

    private async Task FetchSessionAsync(string code)
    {
        if (_fetchingSession)
            return;

        _fetchingSession = true;
        _fetchedSessionCode = code;
        IsSessionBusy = true;
        ErrorText = string.Empty;
        SessionStatusText = "正在从云服务取回名单…";

        var cts = new CancellationTokenSource();
        _sessionCts = cts;

        try
        {
            var document = await _cloud.ImportSessionAsync(code, cts.Token);
            if (document.Kind != RosterTransferKind.DutyRoster)
            {
                ErrorText = "取回的内容不是值日名单，换一个会话码试试";
                SessionStatusText = "取回失败";
                return;
            }

            _tableImportMethod = "SessionCode";
            _tableSessionId = null;
            ApplyTransferDocument(document, "已通过会话码取回名单，核对预览后点「导入并替换」");
        }
        catch (OperationCanceledException)
        {
            // 抽屉关了或换了来源：不提示。
        }
        catch (DutyTransferException exception)
        {
            ErrorText = DescribeTransferError(exception);
            SessionStatusText = "取回失败，核对会话码后再试";
        }
        catch (Exception exception)
        {
            ErrorText = $"取回名单失败：{exception.Message}";
            SessionStatusText = "取回失败，稍后再试";
        }
        finally
        {
            IsSessionBusy = false;
            if (ReferenceEquals(_sessionCts, cts))
            {
                _sessionCts = null;
                cts.Dispose();
            }

            _fetchingSession = false;
        }
    }

    private void CancelSessionFetch()
    {
        _sessionCts?.Cancel();
        _fetchedSessionCode = null;
    }

    /// <summary>离开抽屉时取消还没回来的云请求。</summary>
    public void ReleaseResources()
    {
        CancelSessionFetch();
    }

    private static string DescribeTransferError(DutyTransferException exception) => exception.Error switch
    {
        DutyTransferError.Network => "连不上云服务，检查网络后再试",
        DutyTransferError.InvalidSessionCode => "会话码无效或已过期",
        DutyTransferError.InvalidPairingCode => "配对链接无效或已过期",
        DutyTransferError.InvalidPayload => "取回的内容无效，可能两边版本不一致",
        _ => "云服务暂时不可用，稍后再试"
    };

    // ---- 列映射 ----

    public ObservableCollection<DutyColumnOption> ColumnOptions { get; } = [];

    [ObservableProperty] private DutyColumnOption? _idColumn;

    [ObservableProperty] private DutyColumnOption? _nameColumn;

    [ObservableProperty] private DutyColumnOption? _genderColumn;

    public string MappingDescription => "序号/编号和名称至少选择一项；性别可留空；两项均为空的行不会导入";

    partial void OnIdColumnChanged(DutyColumnOption? value) => RefreshPreviewIfReady();

    partial void OnNameColumnChanged(DutyColumnOption? value) => RefreshPreviewIfReady();

    partial void OnGenderColumnChanged(DutyColumnOption? value) => RefreshPreviewIfReady();

    // ---- 重名 ----

    private IReadOnlyList<string> _duplicateNames = [];

    /// <summary>预览里发现的重复名称个数；导入点下去时按这个决定要不要弹重名对话框。</summary>
    public int DuplicateNameCount => _duplicateNames.Count;

    /// <summary>重名对话框的正文（照 SecRandom-C 的文案；最多列 10 个名字）。</summary>
    public string DuplicateContent => string.Format(
        "以下 {0} 个名称重复：\n{1}\n\n可以保留重名并继续导入，系统会用内部标识区分记录",
        _duplicateNames.Count,
        string.Join('\n', _duplicateNames.Take(10)));

    // ---- 预览 ----

    public ObservableCollection<DutyPreviewRow> PreviewRows { get; } = [];

    public bool HasPreview => PreviewRows.Count > 0;

    [ObservableProperty] private int _sourceRowCount;

    [ObservableProperty] private int _importMemberCount;

    public string PreviewSummary => $"共 {SourceRowCount} 行，将导入 {ImportMemberCount} 名成员";

    public string PreviewHint => "只预览前 3 行；序号/名称/性别都来自上面选中的列";

    partial void OnSourceRowCountChanged(int value) => OnPropertyChanged(nameof(PreviewSummary));

    partial void OnImportMemberCountChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(CanImport));
    }

    // ---- 底栏 ----

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private string _errorText = string.Empty;

    public bool CanImport => ImportMemberCount > 0;

    // ---- 文件 / 粘贴读取 ----

    /// <summary>读一个本地文件；xlsx 会先列出工作表名，再按第一张表读。</summary>
    public void LoadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var sheetNames = RosterFileReader.ReadSheetNames(path);
            var sheetName = sheetNames.FirstOrDefault();
            var result = RosterFileReader.Read(path, sheetName);
            if (!result.Success)
            {
                ErrorText = result.Message ?? "读取文件失败。";
                ResetTable();
                return;
            }

            _lastFilePath = path;
            SelectedFileName = Path.GetFileName(path);
            LoadGrid(result.Grid, sheetNames, sheetName, $"已读取 {result.Grid.RowCount} 行数据");
        }
        catch (Exception ex)
        {
            ErrorText = $"读取文件失败：{ex.Message}";
            ResetTable();
        }
    }

    /// <summary>读粘贴框里的文本（Tab / 分号 / 逗号都认）。</summary>
    public void LoadPastedText()
    {
        if (string.IsNullOrWhiteSpace(PasteText))
        {
            ErrorText = "请先把表格内容粘贴到上面的输入框里。";
            return;
        }

        try
        {
            var grid = RosterFileReader.ParseDelimitedText(PasteText);
            if (grid.RowCount == 0)
            {
                ErrorText = "粘贴的内容里没有任何可用的行。";
                ResetTable();
                return;
            }

            _lastFilePath = null;
            SelectedFileName = null;
            PasteText = string.Empty;
            LoadGrid(grid, [], null, $"已读取 {grid.RowCount} 行数据（粘贴）");
        }
        catch (Exception ex)
        {
            ErrorText = $"读取粘贴内容失败：{ex.Message}";
            ResetTable();
        }
    }

    /// <summary>切换工作表（区域选择器的下拉触发）；只重读同一份文件。</summary>
    public void SelectSheet(string? sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName)
            || string.Equals(sheetName, _selectedSheetName, StringComparison.Ordinal))
            return;

        if (string.IsNullOrWhiteSpace(_lastFilePath))
        {
            _selectedSheetName = sheetName;
            return;
        }

        try
        {
            var result = RosterFileReader.Read(_lastFilePath, sheetName);
            if (!result.Success)
            {
                ErrorText = result.Message ?? "读取工作表失败。";
                return;
            }

            LoadGrid(result.Grid, _sheetNames, sheetName, $"已读取工作表「{sheetName}」的 {result.Grid.RowCount} 行数据");
        }
        catch (Exception ex)
        {
            ErrorText = $"读取工作表失败：{ex.Message}";
        }
    }

    // ---- 区域 / 表格 ----

    /// <summary>抽屉把区域选择器的当前选区送回来。</summary>
    public void SetRegion(RosterImportRegion? region)
    {
        if (_grid is null || _grid.RowCount == 0 || region is null)
        {
            ResetTable();
            return;
        }

        try
        {
            var table = RosterImportRegionBuilder.Build(_grid, region);
            ApplyTable(table, $"已按所选区域读出 {table.RowCount} 行数据");
        }
        catch (Exception ex)
        {
            ErrorText = $"裁剪表格失败：{ex.Message}";
            ResetTable();
        }
    }

    private void LoadGrid(RosterImportGrid grid, IReadOnlyList<string> sheetNames, string? sheetName, string statusText)
    {
        _grid = grid;
        _sheetNames = sheetNames;
        _selectedSheetName = sheetName;
        ErrorText = string.Empty;
        StatusText = statusText;
        OnPropertyChanged(nameof(Grid));
        OnPropertyChanged(nameof(SheetNames));
        OnPropertyChanged(nameof(SelectedSheetName));
        OnPropertyChanged(nameof(HasMultipleSheets));
        OnPropertyChanged(nameof(IsRegionVisible));

        RegionSourceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyTable(RosterImportTable table, string? statusText)
    {
        if (IsFileMode)
        {
            _tableImportMethod = "File";
            _tableSessionId = null;
        }

        _table = table;
        SourceRowCount = table.RowCount;
        if (!string.IsNullOrWhiteSpace(statusText))
            StatusText = statusText;
        RebuildColumnOptions();
        RefreshPreview();
        OnPropertyChanged(nameof(HasTable));
    }

    private void ResetTable()
    {
        _grid = null;
        _table = RosterImportTable.Empty;
        _pendingMembers = [];
        _duplicateNames = [];
        _lastFilePath = null;
        _sheetNames = [];
        _selectedSheetName = null;
        PreviewRows.Clear();
        ColumnOptions.Clear();
        ColumnOptions.Add(new DutyColumnOption { Column = null, Name = NoColumn });
        _isUpdatingColumns = true;
        try
        {
            IdColumn = ColumnOptions[0];
            NameColumn = ColumnOptions[0];
            GenderColumn = ColumnOptions[0];
        }
        finally
        {
            _isUpdatingColumns = false;
        }

        SourceRowCount = 0;
        ImportMemberCount = 0;
        OnPropertyChanged(nameof(HasTable));
        OnPropertyChanged(nameof(Grid));
        OnPropertyChanged(nameof(SheetNames));
        OnPropertyChanged(nameof(SelectedSheetName));
        OnPropertyChanged(nameof(HasMultipleSheets));
        OnPropertyChanged(nameof(IsRegionVisible));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(DuplicateNameCount));
        OnPropertyChanged(nameof(DuplicateContent));
    }

    // ---- 列映射 ----

    private void RebuildColumnOptions()
    {
        _isUpdatingColumns = true;
        try
        {
            ColumnOptions.Clear();
            ColumnOptions.Add(new DutyColumnOption { Column = null, Name = NoColumn });

            foreach (var column in _table.Columns)
                ColumnOptions.Add(new DutyColumnOption { Column = column, Name = column });

            var exclude = new HashSet<string>(StringComparer.Ordinal);
            var keywords = RosterImportKeywords.Default;
            var idColumn = RosterImportParser.FindBestColumn(_table.Columns, keywords.Id, exclude);
            if (idColumn is not null)
                exclude.Add(idColumn);
            var nameColumn = RosterImportParser.FindBestColumn(_table.Columns, keywords.Name, exclude);
            if (nameColumn is not null)
                exclude.Add(nameColumn);
            var genderColumn = RosterImportParser.FindBestColumn(_table.Columns, keywords.Gender, exclude);

            IdColumn = FindOption(idColumn);
            NameColumn = FindOption(nameColumn);
            GenderColumn = FindOption(genderColumn);
        }
        finally
        {
            _isUpdatingColumns = false;
        }
    }

    private DutyColumnOption FindOption(string? column)
    {
        if (string.IsNullOrWhiteSpace(column))
            return ColumnOptions[0];

        return ColumnOptions.FirstOrDefault(option =>
                   string.Equals(option.Column, column, StringComparison.Ordinal))
               ?? ColumnOptions[0];
    }

    private void RefreshPreviewIfReady()
    {
        if (_isUpdatingColumns)
            return;

        RefreshPreview();
    }

    // ---- 预览 ----

    private void RefreshPreview()
    {
        if (_table.IsEmpty)
        {
            _pendingMembers = [];
            _duplicateNames = [];
            PreviewRows.Clear();
            ImportMemberCount = 0;
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(DuplicateNameCount));
            OnPropertyChanged(nameof(DuplicateContent));
            return;
        }

        var mapping = new RosterImportColumnMapping(
            IdColumn?.Column,
            NameColumn?.Column,
            GenderColumn?.Column);

        // ⚠️ existingMembers 必须传当前名单：不传的话每次都会生成新的 RecordId，
        // 于是小组归属、固定值日与值日调整全部指向了不存在的成员。
        var result = RosterImportParser.Parse(
            _table,
            mapping,
            RosterDuplicateHandling.Rename,
            RosterImportKeywords.Default,
            _service.Document.Members);

        _pendingMembers = result.Members ?? [];
        _duplicateNames = result.DuplicateNames ?? [];
        ImportMemberCount = result.Success ? _pendingMembers.Count : 0;
        OnPropertyChanged(nameof(DuplicateNameCount));
        OnPropertyChanged(nameof(DuplicateContent));

        PreviewRows.Clear();
        foreach (var member in _pendingMembers.Take(3))
        {
            PreviewRows.Add(new DutyPreviewRow
            {
                Id = member.Id ?? string.Empty,
                Name = member.Name ?? string.Empty,
                Gender = member.Gender ?? string.Empty
            });
        }

        ErrorText = result.Error == RosterImportError.None ? string.Empty : result.Message ?? string.Empty;
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(CanImport));
    }

    // ---- 二维码 ----

    [RelayCommand]
    private void AddQrFrame()
    {
        var text = QrFrameText?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            ErrorText = "请先把一帧二维码文本粘贴到上面的输入框里。";
            return;
        }

        if (IsQuickQrMode)
        {
            // 快速导入贴进来的应当是配对链接；扫到的也是它，走同一条分流。
            QrFrameText = string.Empty;
            HandleScannedText(text);
            return;
        }

        AddQrFrameText(text);
        QrFrameText = string.Empty;
        UpdateQrState();
    }

    /// <summary>
    ///     相机扫到一段二维码文本：按当前来源分流。
    ///     快速导入扫到的是配对链接（去云服务取回）；无网导入扫到的是一帧离线数据（喂给累加器）。
    /// </summary>
    public void HandleScannedText(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (IsQuickQrMode)
        {
            if (!DutyQuickPairing.TryParse(value, out _, out _))
            {
                ErrorText = "这不是名单配对二维码，换一个二维码再扫";
                return;
            }

            _ = FetchQuickQrAsync(value);
            return;
        }

        if (IsOfflineQrMode)
            AddQrFrameText(value);
    }

    private void AddQrFrameText(string text)
    {
        _qr ??= new OfflineQrFrameAccumulator();
        var result = _qr.AddFrame(text);
        switch (result)
        {
            case RosterQrFrameImportResult.Accepted:
                StatusText = "已收下这一帧";
                ErrorText = string.Empty;
                break;
            case RosterQrFrameImportResult.Duplicate:
                StatusText = "这一帧已经收过了";
                ErrorText = string.Empty;
                break;
            default:
                ErrorText = "这串文本不是有效的 SecRandom 二维码帧。";
                break;
        }

        UpdateQrState();
    }

    [RelayCommand]
    private void CompleteQr()
    {
        if (_qr is null || _qr.ReceivedFrameCount == 0)
        {
            ErrorText = "还没有收到任何帧，先逐帧粘贴二维码文本。";
            return;
        }

        var result = _qr.Complete();
        if (!result.Success || result.Document is null)
        {
            ErrorText = result.Message ?? "这些帧还拼不出完整的名单，检查一下是否漏了帧。";
            return;
        }

        _tableImportMethod = "OfflineQr";
        _tableSessionId = _qr?.SessionId;
        ApplyTransferDocument(result.Document, "已从二维码拼出名单，核对预览后点「导入并替换」");
        UpdateQrState();
    }

    /// <summary>扫码快速导入：扫到的是配对链接，直接去云服务取回名单。</summary>
    private async Task FetchQuickQrAsync(string pairingUrl)
    {
        if (_fetchingSession)
            return;

        _fetchingSession = true;
        IsSessionBusy = true;
        ErrorText = string.Empty;
        QrStatusText = "正在按配对二维码取回名单…";

        var cts = new CancellationTokenSource();
        _sessionCts = cts;

        try
        {
            var document = await _cloud.ImportQuickAsync(pairingUrl, cts.Token);
            if (document.Kind != RosterTransferKind.DutyRoster)
            {
                ErrorText = "取回的内容不是值日名单，换一个二维码试试";
                QrStatusText = "取回失败";
                return;
            }

            _tableImportMethod = "QuickQr";
            _tableSessionId = null;
            ApplyTransferDocument(document, "已通过配对二维码取回名单，核对预览后点「导入并替换」");
            QrStatusText = "已取回名单";
        }
        catch (OperationCanceledException)
        {
            // 抽屉关了或换了来源：不提示。
        }
        catch (DutyTransferException exception)
        {
            ErrorText = DescribeTransferError(exception);
            QrStatusText = "取回失败，检查二维码后再试";
        }
        catch (Exception exception)
        {
            ErrorText = $"取回名单失败：{exception.Message}";
            QrStatusText = "取回失败，稍后再试";
        }
        finally
        {
            IsSessionBusy = false;
            if (ReferenceEquals(_sessionCts, cts))
            {
                _sessionCts = null;
                cts.Dispose();
            }

            _fetchingSession = false;
        }
    }

    /// <summary>把扫码/云传输拿到的名单文档摊成一张三列表，接回后面的列映射与预览。</summary>
    private void ApplyTransferDocument(RosterTransferDocument document, string statusText)
    {
        var columns = new List<string> { "序号", "姓名", "性别" };
        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (var row in document.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id) && string.IsNullOrWhiteSpace(row.Name))
                continue;

            rows.Add(new Dictionary<string, string>
            {
                ["序号"] = row.Id ?? string.Empty,
                ["姓名"] = row.Name ?? string.Empty,
                ["性别"] = row.DetailOne ?? string.Empty
            });
        }

        _grid = null;
        _sheetNames = [];
        _selectedSheetName = null;
        _lastFilePath = null;
        if (!string.IsNullOrWhiteSpace(document.FileName))
            SelectedFileName = document.FileName;

        ErrorText = string.Empty;
        OnPropertyChanged(nameof(Grid));
        OnPropertyChanged(nameof(SheetNames));
        OnPropertyChanged(nameof(SelectedSheetName));
        OnPropertyChanged(nameof(IsRegionVisible));

        ApplyTable(new RosterImportTable(columns, rows), statusText);
    }

    private void UpdateQrState()
    {
        if (_qr is null)
        {
            QrProgressText = "已收 0 / 共 0 帧";
            QrSessionId = "—";
            QrCanComplete = false;
            return;
        }

        QrProgressText = $"已收 {_qr.ReceivedFrameCount} / 共 {_qr.TotalFrameCount} 帧";
        QrSessionId = string.IsNullOrWhiteSpace(_qr.SessionId) ? "—" : _qr.SessionId;
        QrCanComplete = _qr.ReceivedFrameCount > 0;
        OnPropertyChanged(nameof(QrModeDescription));
    }

    // ---- 导入 ----

    /// <summary>
    ///     把预览里的成员整表写回名单；失败时把原因留在 ErrorText 里。
    ///     <paramref name="handling" /> 是重名对话框选的处理方式（没有重名时传 Rename 即可）。
    /// </summary>
    public bool Apply(RosterDuplicateHandling handling = RosterDuplicateHandling.Rename)
    {
        try
        {
            // 重名处理必须重新解析一遍：预览是按 Rename 解析的，用户可能选「保留重名」。
            var mapping = new RosterImportColumnMapping(
                IdColumn?.Column,
                NameColumn?.Column,
                GenderColumn?.Column);
            var result = RosterImportParser.Parse(
                _table,
                mapping,
                handling,
                RosterImportKeywords.Default,
                _service.Document.Members);
            if (!result.Success)
            {
                ErrorText = result.Message ?? "名单解析失败。";
                return false;
            }

            _pendingMembers = result.Members ?? [];
            if (_pendingMembers.Count == 0)
            {
                ErrorText = "没有可以导入的成员，检查一下区域和列映射。";
                return false;
            }

            var source = new DutyRosterSource
            {
                Provider = "SecRandom",
                RosterName = ResolveRosterName(),
                ImportMethod = _tableImportMethod ?? (IsFileMode ? "File" : "Manual"),
                SessionId = _tableSessionId,
                Note = BuildImportNote()
            };

            _service.ApplyRoster(_pendingMembers, source);
            StatusText = $"已导入 {_pendingMembers.Count} 名成员";
            Completed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            ErrorText = $"导入失败：{ex.Message}";
            return false;
        }
    }

    private string BuildImportNote() => _tableImportMethod switch
    {
        "QuickQr" => "SecRandom 扫码快速导入",
        "OfflineQr" => "SecRandom 二维码导入",
        "SessionCode" => "SecRandom 会话码导入",
        "File" => string.IsNullOrWhiteSpace(SelectedFileName) ? "粘贴表格导入" : SelectedFileName,
        _ => "粘贴表格导入"
    };

    private string ResolveRosterName()
    {
        var rosterName = _service.Document.Source?.RosterName;
        return string.IsNullOrWhiteSpace(rosterName) ? "值日名单" : rosterName;
    }

    // ---- 命令（绑定到按钮） ----

    /// <summary>粘贴框旁边的「读入这段文本」；AddQrFrame / CompleteQr / PickFile 由 [RelayCommand] 生成。</summary>
    [RelayCommand]
    private void LoadPaste()
    {
        LoadPastedText();
    }
}