using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.CrashRecovery;
using Corkboard.Services.CrashRecovery;
using CRV = Corkboard.Langs.CrashRecovery.Resources;

namespace Corkboard.ViewModels.CrashRecovery;

/// <summary>
///     崩溃恢复窗口的内容状态：报告正文、倒计时文案与五个按钮的行为。
///     行为照上游 SecRandom-C 的 <c>CrashRecoveryView</c> 一比一搬过来（去掉应用内反馈那一支：
///     Corkboard 没有反馈服务，反馈一律走 GitHub issue）。
/// </summary>
public sealed partial class CrashRecoveryPromptViewModel : ObservableObject
{
    /// <summary>自动重启倒计时秒数。</summary>
    public const int AutoRestartSeconds = 10;

    private readonly bool _autoRestart;
    private readonly Func<bool> _restartApp;
    private DispatcherTimer? _restartTimer;
    private int _remainingSeconds;

    public CrashRecoveryPromptViewModel(CrashRecoveryPromptOptions options, Func<bool> restartApp,
        bool canIgnore = true)
    {
        _restartApp = restartApp;
        _autoRestart = options.AutoRestart;
        CanIgnore = canIgnore;
        CrashReport = CrashRecoveryRuntime.ReadCrashReport(options.CrashReportPath);
        RestartButtonText = CRV.C_Restart;
    }

    /// <summary>崩溃报告正文（只读展示）。</summary>
    public string CrashReport { get; }

    /// <summary>「忽略」按钮是否可见：从命令行带起来的提示窗口不提供忽略（用户必须选重启或关闭）。</summary>
    public bool CanIgnore { get; }

    /// <summary>用户点了「忽略」——窗口关掉之后程序继续以崩溃后的状态跑，不重启。</summary>
    public bool WasIgnored { get; private set; }

    /// <summary>「重新启动」按钮上的字：倒计时期间是「N 秒后重启」。</summary>
    public string RestartButtonText { get; private set; }

    [ObservableProperty] private string? _statusMessage;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>窗口该关掉了（忽略 / 关闭 / 已经交给新进程重启）。</summary>
    public event EventHandler? Dismissed;

    partial void OnStatusMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    /// <summary>开始自动重启倒计时（只在带 <c>--crash-auto-restart</c> 时才有）。</summary>
    public void StartAutoRestartCountdown()
    {
        if (!_autoRestart || _restartTimer is not null)
            return;

        _remainingSeconds = AutoRestartSeconds;
        RefreshRestartButtonText();

        _restartTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _restartTimer.Tick += OnRestartTimerTick;
        _restartTimer.Start();
    }

    /// <summary>停下倒计时（窗口离开可视树、用户点了忽略、或者已经在重启）。</summary>
    public void StopAutoRestartCountdown()
    {
        if (_restartTimer is null)
            return;

        _restartTimer.Stop();
        _restartTimer.Tick -= OnRestartTimerTick;
        _restartTimer = null;
    }

    /// <summary>复制报告用的正文（等于 <see cref="CrashReport" />，单独一个方法只是让调用点读起来清楚）。</summary>
    public string BuildCopyText() => CrashReport;

    /// <summary>
    ///     重新启动。成功返回 <c>true</c>（此时已经启动新进程，窗口应当关掉）；
    ///     失败只把原因写到状态行，**不关窗口**——用户还能复制报告或手动重开。
    /// </summary>
    public async Task<bool> RestartAsync()
    {
        StopAutoRestartCountdown();

        if (_autoRestart && !CrashRecoveryRestartGuard.TryBeginAutomaticRestart(
                CrashRecoveryRuntime.DefaultRestartGuardPath, DateTimeOffset.Now))
        {
            StatusMessage = CRV.C_RestartTooOften;
            return false;
        }

        var restarted = _restartApp();
        await Task.Yield();
        if (!restarted)
        {
            StatusMessage = CRV.C_RestartFailed;
            return false;
        }

        WasIgnored = false;
        Dismissed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>忽略这次崩溃，继续用现在这个（可能已经有问题的）进程。</summary>
    public async Task IgnoreAsync()
    {
        StopAutoRestartCountdown();
        await Task.Delay(100);
        WasIgnored = true;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>关掉窗口但不算「忽略」：调用方据此决定是否退出。</summary>
    public void Close()
    {
        StopAutoRestartCountdown();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>把一句结果写到状态行（复制成功 / 打开链接失败之类）。</summary>
    public void SetStatus(string? message)
    {
        StatusMessage = message;
    }

    private void OnRestartTimerTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        if (_remainingSeconds <= 0)
        {
            _ = RestartAsync();
            return;
        }

        RefreshRestartButtonText();
    }

    private void RefreshRestartButtonText()
    {
        RestartButtonText = _remainingSeconds > 0
            ? string.Format(CRV.C_RestartCountdown, _remainingSeconds)
            : CRV.C_Restart;
        OnPropertyChanged(nameof(RestartButtonText));
    }
}
