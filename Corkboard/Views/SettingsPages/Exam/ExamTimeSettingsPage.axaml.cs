using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Exam;

namespace Corkboard.Views.SettingsPages.Exam;

/// <summary>
///     「时间同步」设置页：NTP 开关、同步源清单、间隔与超时、立即同步与当前校时状态。
/// </summary>
[PageInfo("settings.exam.time", FluentIcons.GlobeFilled, "settings.exam.group")]
public partial class ExamTimeSettingsPage : UserControl
{
    public ExamTimeSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamTimeSettingsPageViewModel>();

        // ViewModel 是 transient、校时服务与配置是单例：订阅必须在离开可视树时断开。
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) =>
        {
            ViewModel?.CommitAll();
            ViewModel?.Detach();
        };
    }

    private ExamTimeSettingsPageViewModel? ViewModel => DataContext as ExamTimeSettingsPageViewModel;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>间隔 / 超时输入框失焦就提交。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitAll();

    /// <summary>源地址输入框里按回车等于点「添加」。</summary>
    private void OnServerDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        e.Handled = true;
        if (ViewModel?.AddServerCommand.CanExecute(null) == true)
            ViewModel.AddServerCommand.Execute(null);
    }

    /// <summary>删掉某一行同步源。</summary>
    private void OnDeleteServerClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is NtpServerItem item)
            ViewModel?.RemoveServer(item);
    }

    /// <summary>「恢复默认源」：换回内置的那几个国内公共 NTP 服务。</summary>
    private void OnResetServersClick(object? sender, RoutedEventArgs e) => ViewModel?.ResetServers();
}
