using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Corkboard.Core.Abstraction;
using Corkboard.Core.DutyRoster.Import;
using Corkboard.Platforms.Abstractions;
using Corkboard.ViewModels.Duty;
using FluentAvalonia.UI.Controls;

namespace Corkboard.Views.Duty;

/// <summary>
///     名单导入抽屉。挂在设置窗口右侧的 DrawerHost 里，由名单页
///     <c>SettingsView.Current?.OpenDrawer(new DutyImportDrawer())</c> 打开。
///     <para>
///         这里只做「界面连接」：选文件对话框、覆盖确认框、把网格/选区在 ViewModel 与
///         <see cref="DutyImportRegionSelector" /> 之间搬运；相机扫码的开关、取帧、
///         解码也在这一层（采集线程 decode，UI 线程显示预览与分发识别结果）。
///     </para>
/// </summary>
public partial class DutyImportDrawer : UserControl
{
    private readonly DutyImportViewModel _viewModel;
    private readonly ICameraScanner _camera;
    private readonly List<PlatformCameraDevice> _cameraDevices = [];
    private DutyImportRegionSelector? _regionSelector;
    private bool _regionSelectorAttached;
    private bool _cameraStarted;
    private bool _startingCamera;
    private string? _handledText;
    private bool _closed;

    public DutyImportDrawer()
    {
        InitializeComponent();

        _viewModel = IAppHost.GetService<DutyImportViewModel>();
        _camera = IAppHost.GetService<ICameraScanner>();
        _viewModel.IsCameraSupported = _camera.IsSupported;
        _viewModel.FilePickRequested += OnFilePickRequested;
        _viewModel.RegionSourceChanged += OnRegionSourceChanged;
        _viewModel.Completed += OnImportCompleted;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _camera.FrameReceived += OnCameraFrame;
        _camera.CameraError += OnCameraError;
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _closed = false;
        _regionSelector ??= this.FindControl<DutyImportRegionSelector>("RegionSelector");
        if (_regionSelector is not null && !_regionSelectorAttached)
        {
            _regionSelector.RegionChanged += OnRegionChanged;
            _regionSelector.SheetSelectionChanged += OnSheetSelectionChanged;
            _regionSelectorAttached = true;

            // 抽屉是新建的，正常不会有残留网格；这里只是兜底（重开抽屉时状态已经被换掉）。
            ReloadRegionSelector();
        }

        _ = InitializeCameraAsync();
    }

    // ---- 相机 ----

    private DutyQrCameraPreview? Preview => this.FindControl<DutyQrCameraPreview>("CameraPreview");

    private async Task InitializeCameraAsync()
    {
        if (!_camera.IsSupported)
            return;

        await LoadCameraDevicesAsync();
        if (_closed)
            return;

        if (_viewModel.IsQrMode)
            await StartCameraAsync();
    }

    private async Task LoadCameraDevicesAsync()
    {
        var devices = await _camera.GetDevicesAsync();
        _cameraDevices.Clear();
        _cameraDevices.AddRange(devices);

        var combo = this.FindControl<ComboBox>("CameraDeviceComboBox");
        if (combo is null)
            return;

        combo.ItemsSource = _cameraDevices.Select(device => device.DisplayName).ToList();
        combo.SelectedIndex = _cameraDevices.Count > 0 ? 0 : -1;
        if (_cameraDevices.Count == 0)
            _viewModel.CameraStatusText = "没有找到摄像头，可以改用粘贴二维码文本";
    }

    private PlatformCameraDevice? SelectedDevice()
    {
        var combo = this.FindControl<ComboBox>("CameraDeviceComboBox");
        var index = combo?.SelectedIndex ?? -1;
        return index >= 0 && index < _cameraDevices.Count ? _cameraDevices[index] : null;
    }

    private async Task StartCameraAsync()
    {
        if (_closed || !_camera.IsSupported || _cameraStarted || _startingCamera)
            return;

        _startingCamera = true;
        try
        {
            if (_cameraDevices.Count == 0)
                await LoadCameraDevicesAsync();
            if (_closed)
                return;

            _viewModel.CameraStatusText = "正在打开相机…";
            var started = await _camera.StartAsync(SelectedDevice(), CancellationToken.None);
            if (_closed)
            {
                // 抽屉在打开过程中被关掉了：别把相机留在后台跑。
                if (started)
                    await _camera.StopAsync();
                return;
            }

            _cameraStarted = started;
            _viewModel.CameraStatusText = started
                ? "相机已打开，把二维码放进取景框"
                : "打不开相机，可以改用粘贴二维码文本";
        }
        catch (Exception exception)
        {
            _cameraStarted = false;
            _viewModel.CameraStatusText = $"打不开相机：{exception.Message}";
        }
        finally
        {
            _startingCamera = false;
        }
    }

    private async Task StopCameraAsync()
    {
        if (!_cameraStarted)
            return;

        _cameraStarted = false;
        _handledText = null;
        try
        {
            await _camera.StopAsync();
        }
        catch
        {
            // 停相机失败不影响抽屉关闭。
        }

        Preview?.Clear();
        _viewModel.CameraStatusText = string.Empty;
    }

    /// <summary>采集线程：先解码，再把预览与识别结果丢回 UI 线程。</summary>
    private void OnCameraFrame(object? sender, CameraFrame frame)
    {
        if (!_cameraStarted)
            return;

        string? text = null;
        try
        {
            if (QrFrameDecoder.TryDecode(frame.Pixels, frame.Width, frame.Height, QrPixelFormat.Bgr24, out var decoded))
                text = decoded;
        }
        catch
        {
            // 解码出错当这一帧没识别到，不要把相机采集打断。
        }

        var shouldHandle = text is not null && !string.Equals(text, _handledText, StringComparison.Ordinal);
        if (shouldHandle)
            _handledText = text;

        Dispatcher.UIThread.Post(() =>
        {
            if (!_cameraStarted)
                return;

            Preview?.ShowFrame(frame);
            if (shouldHandle)
                _viewModel.HandleScannedText(text);
        });
    }

    private void OnCameraError(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() => _viewModel.CameraStatusText = message);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DutyImportViewModel.IsQrMode))
        {
            if (_viewModel.IsQrMode)
                _ = StartCameraAsync();
            else
                _ = StopCameraAsync();
        }
    }

    private async void OnCameraDeviceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_cameraStarted || _startingCamera)
            return;

        await StopCameraAsync();
        await StartCameraAsync();
    }

    private void OnImportCompleted(object? sender, EventArgs e)
    {
        SettingsView.Current?.CloseDrawer();
    }

    private async void OnFilePickRequested(object? sender, EventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择名单文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("表格文件") { Patterns = ["*.csv", "*.xlsx", "*.xls"] },
                FilePickerFileTypes.All
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            _viewModel.LoadFile(path);
    }

    /// <summary>网格换了（文件 / 工作表 / 粘贴 / 二维码）：把新网格塞进选择器，再让它把选区还回来。</summary>
    private void OnRegionSourceChanged(object? sender, EventArgs e) => ReloadRegionSelector();

    private void ReloadRegionSelector()
    {
        if (_regionSelector is null)
            return;

        var grid = _viewModel.Grid;
        if (grid is null || grid.RowCount == 0)
        {
            // 没有网格（例如刚拼完二维码）时只清选择器，不要把 ViewModel 里已经算好的表重置掉。
            _regionSelector.Clear();
            return;
        }

        _regionSelector.Load(
            _viewModel.SheetNames,
            _viewModel.SelectedSheetName,
            grid,
            RosterImportKeywords.Default.All);
        _viewModel.SetRegion(_regionSelector.CurrentRegion);
    }

    private void OnRegionChanged(object? sender, EventArgs e)
    {
        if (_regionSelector is not null)
            _viewModel.SetRegion(_regionSelector.CurrentRegion);
    }

    private void OnSheetSelectionChanged(object? sender, string? sheetName)
    {
        _viewModel.SelectSheet(sheetName);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.CloseDrawer();
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanImport)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        // 重名处理照 SecRandom-C：只在发现重名时弹，先问重名怎么处理，再问覆盖。
        var handling = RosterDuplicateHandling.Rename;
        if (_viewModel.DuplicateNameCount > 0)
        {
            var duplicateDialog = new FAContentDialog
            {
                Title = "发现重复名称",
                Content = _viewModel.DuplicateContent,
                PrimaryButtonText = "保留重名导入",
                SecondaryButtonText = "自动重命名",
                CloseButtonText = "返回修改",
                DefaultButton = FAContentDialogButton.Primary
            };

            var duplicateResult = await duplicateDialog.ShowAsync(topLevel);
            if (duplicateResult == FAContentDialogResult.Primary)
                handling = RosterDuplicateHandling.Keep;
            else if (duplicateResult == FAContentDialogResult.Secondary)
                handling = RosterDuplicateHandling.Rename;
            else
                return;
        }

        var dialog = new FAContentDialog
        {
            Title = "覆盖当前名单？",
            Content = string.Format(
                "当前名单已有 {0} 名成员，本次将导入 {1} 名成员；继续后会覆盖当前名单内容",
                _viewModel.ExistingMemberCount,
                _viewModel.ImportMemberCount),
            PrimaryButtonText = "覆盖导入",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Primary
        };

        if (await dialog.ShowAsync(topLevel) != FAContentDialogResult.Primary)
            return;

        _viewModel.Apply(handling);
    }

    private async void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _closed = true;
        _viewModel.FilePickRequested -= OnFilePickRequested;
        _viewModel.RegionSourceChanged -= OnRegionSourceChanged;
        _viewModel.Completed -= OnImportCompleted;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _camera.FrameReceived -= OnCameraFrame;
        _camera.CameraError -= OnCameraError;

        if (_regionSelector is not null && _regionSelectorAttached)
        {
            _regionSelector.RegionChanged -= OnRegionChanged;
            _regionSelector.SheetSelectionChanged -= OnSheetSelectionChanged;
            _regionSelectorAttached = false;
        }

        await StopCameraAsync();
        _viewModel.ReleaseResources();
        Preview?.Dispose();

        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }
}