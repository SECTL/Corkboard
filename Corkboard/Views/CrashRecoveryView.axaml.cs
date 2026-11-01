using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Corkboard.Core.CrashRecovery;
using Corkboard.Services.CrashRecovery;
using Corkboard.ViewModels.CrashRecovery;
using CRV = Corkboard.Langs.CrashRecovery.Resources;

namespace Corkboard.Views;

/// <summary>
///     崩溃恢复窗口里的内容（标题 + 说明 + 报告正文 + 五个按钮）。
///     由 <see cref="CrashRecoveryWindow" /> 承载，**也可以直接塞进任意窗口**（调试入口就这么用）。
/// </summary>
public partial class CrashRecoveryView : UserControl
{
    private readonly CrashRecoveryPromptViewModel _viewModel;

    public CrashRecoveryView(CrashRecoveryPromptOptions options, Func<bool> restartApp, bool canIgnore = true)
    {
        InitializeComponent();
        _viewModel = new CrashRecoveryPromptViewModel(options, restartApp, canIgnore);
        _viewModel.Dismissed += (_, _) => Dismissed?.Invoke(this, EventArgs.Empty);
        DataContext = _viewModel;

        AttachedToVisualTree += (_, _) => _viewModel.StartAutoRestartCountdown();
        DetachedFromVisualTree += (_, _) => _viewModel.StopAutoRestartCountdown();
    }

    /// <summary>窗口该关掉了（忽略 / 关闭 / 已经交给新进程重启）。</summary>
    public event EventHandler? Dismissed;

    /// <summary>用户点了「忽略」：关窗之后不重启、也不退出。</summary>
    public bool WasIgnored => _viewModel.WasIgnored;

    private async void CopyReport_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                _viewModel.SetStatus(CRV.C_CopyFailed);
                return;
            }

            // Avalonia 12 的 IClipboard 只剩 SetDataAsync / SetValueAsync，写文本就是往 DataFormat.Text 放值。
            await clipboard.SetValueAsync(DataFormat.Text, _viewModel.BuildCopyText());

            // Windows 上剪贴板默认随进程一起没，先 Flush 一下；别的平台这个方法什么都不做。
            await clipboard.FlushAsync();
            _viewModel.SetStatus(CRV.C_CopyDone);
        }
        catch (Exception)
        {
            _viewModel.SetStatus(CRV.C_CopyFailed);
        }
    }

    private void ReportIssue_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CrashRecoveryRuntime.OpenFeedbackIssue(_viewModel.CrashReport))
            _viewModel.SetStatus(CRV.C_ReportFailed);
    }

    private async void Ignore_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.IgnoreAsync();
    }

    private async void Restart_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RestartAsync();
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.Close();
    }
}
