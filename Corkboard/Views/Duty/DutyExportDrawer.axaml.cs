using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Corkboard.Core.Abstraction;
using Corkboard.ViewModels.Duty;

namespace Corkboard.Views.Duty;

/// <summary>
///     名单导出抽屉。挂在设置窗口右侧的 DrawerHost 里，由名单页
///     <c>SettingsView.Current?.OpenDrawer(...)</c> 打开。
///     <para>
///         选保存路径的对话框与二维码自动播放的定时器都在这里（界面层）：
///         VM 只发 <see cref="DutyExportViewModel.FileSaveRequested" />、
///         回答「下一帧是哪个」，写文件与生成 PNG 字节都由 Core 完成。
///     </para>
/// </summary>
public partial class DutyExportDrawer : UserControl
{
    private readonly DutyExportViewModel _viewModel;
    private DispatcherTimer? _autoPlayTimer;

    public DutyExportDrawer()
    {
        InitializeComponent();

        _viewModel = IAppHost.GetService<DutyExportViewModel>();
        _viewModel.FileSaveRequested += OnFileSaveRequested;
        _viewModel.Completed += OnExportCompleted;
        _viewModel.CopyTextRequested += OnCopyTextRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = _viewModel;
    }

    private void OnExportCompleted()
    {
        SettingsView.Current?.CloseDrawer();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DutyExportViewModel.IsAutoPlaying))
            return;

        if (_viewModel.IsAutoPlaying)
            StartAutoPlay();
        else
            StopAutoPlay();
    }

    private void StartAutoPlay()
    {
        _autoPlayTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _autoPlayTimer.Tick -= OnAutoPlayTick;
        _autoPlayTimer.Tick += OnAutoPlayTick;
        _autoPlayTimer.Start();
    }

    private void StopAutoPlay()
    {
        if (_autoPlayTimer is null)
            return;

        _autoPlayTimer.Stop();
        _autoPlayTimer.Tick -= OnAutoPlayTick;
    }

    private void OnAutoPlayTick(object? sender, EventArgs e) => _viewModel.AdvanceAutoFrame();

    private async void OnFileSaveRequested()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出成员名单",
            SuggestedFileName = _viewModel.SuggestedFileName,
            DefaultExtension = Path.GetExtension(_viewModel.SuggestedFileName).TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("CSV 表格")
                {
                    Patterns = ["*.csv"],
                    MimeTypes = ["text/csv"]
                },
                new FilePickerFileType("XLSX 工作簿")
                {
                    Patterns = ["*.xlsx"],
                    MimeTypes = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"]
                }
            ]
        });

        if (file is null)
            return;

        var path = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _viewModel.SaveFile(path);
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        _viewModel.SaveFile(stream, file.Name);
    }

    private async void OnCopyTextRequested(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null || string.IsNullOrEmpty(text))
            return;

        try
        {
            // Avalonia 12 的 IClipboard 只剩 SetDataAsync / SetValueAsync：写文本就是往 DataFormat.Text 放值。
            await clipboard.SetValueAsync(DataFormat.Text, text);
            await clipboard.FlushAsync();
            _viewModel.StatusText = "已复制到剪贴板";
        }
        catch
        {
            _viewModel.StatusText = "复制失败，请手动选中文本复制";
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.CloseDrawer();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        StopAutoPlay();
        _viewModel.ReleaseResources();
        _viewModel.FileSaveRequested -= OnFileSaveRequested;
        _viewModel.Completed -= OnExportCompleted;
        _viewModel.CopyTextRequested -= OnCopyTextRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnUnloaded(e);
    }
}