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
///     「看板显示」设置页：标题与副标题、副标题颜色、即将开始阈值、时钟位置，
///     以及「即将开始 / 进行中」两种形态要不要收起考试表。
/// </summary>
[PageInfo("settings.exam.display", FluentIcons.ClockFilled, "settings.exam.group")]
public partial class ExamDisplaySettingsPage : UserControl
{
    public ExamDisplaySettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamDisplaySettingsPageViewModel>();

        // ViewModel 是 transient、配置是单例：订阅必须在离开可视树时断开。
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) =>
        {
            ViewModel?.CommitAll();
            ViewModel?.Detach();
        };
    }

    private ExamDisplaySettingsPageViewModel? ViewModel => DataContext as ExamDisplaySettingsPageViewModel;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>文本与数值输入框失焦就提交：配置没有防抖，双向绑等于每敲一个字符写一遍盘。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitAll();

    /// <summary>输入框里按回车立刻提交，不用先点别处。</summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        e.Handled = true;
        ViewModel?.CommitAll();
    }
}
