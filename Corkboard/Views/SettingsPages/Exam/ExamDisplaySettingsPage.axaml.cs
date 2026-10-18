using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Exam;

namespace Corkboard.Views.SettingsPages.Exam;

/// <summary>
///     「看板显示」设置页：即将开始阈值、时钟位置，以及「即将开始 / 进行中」两种形态要不要收起考试表。
///     <para>
///         标题、副标题与考试安排跟着看板文件走，在「考试安排」页里改。
///     </para>
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

    /// <summary>数值输入框失焦就提交：配置没有防抖，双向绑等于每敲一个字符写一遍盘。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitAll();
}
