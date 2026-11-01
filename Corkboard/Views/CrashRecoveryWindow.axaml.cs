using Avalonia.Controls;
using Corkboard.Core.CrashRecovery;
using Corkboard.Services.Desktop;
using Corkboard.Views;

namespace Corkboard.Views;

/// <summary>
///     崩溃恢复窗口（无边框外壳，内容全在 <see cref="CrashRecoveryView" /> 里）。
///     与上游 SecRandom-C 的 <c>CrashRecoveryWindow</c> 一一对应：构造参数是「提示选项 + 重启动作 + 能不能忽略」。
/// </summary>
public partial class CrashRecoveryWindow : Window
{
    private readonly CrashRecoveryView _content;

    public CrashRecoveryWindow(CrashRecoveryPromptOptions options, Func<bool> restartApp, bool canIgnore = true)
    {
        InitializeComponent();
        Icon = AppIcon.LoadWindowIcon();

        _content = new CrashRecoveryView(options, restartApp, canIgnore);
        _content.Dismissed += (_, _) => Close();
        this.FindControl<ContentControl>("ContentHost")!.Content = _content;
    }

    /// <summary>用户点了「忽略」：关窗之后不退出程序。</summary>
    public bool WasIgnored => _content.WasIgnored;
}
