using Avalonia.Controls;
using Avalonia.Interactivity;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Exam;

namespace Corkboard.Views.SettingsPages.Exam;

/// <summary>
///     「自动放映」设置页：提前量、开考后补弹、考完延迟关闭，以及下一次放映的状态与「立即放映」。
///     <para>
///         每块看板开不开自动放映在「考试安排」页的看板列表里逐块勾（<c>ExamBoardDocument.AutoShowEnabled</c>）。
///     </para>
/// </summary>
[PageInfo("settings.exam.autoplay", FluentIcons.FullScreenMaximizeFilled, "settings.exam.group")]
public partial class ExamAutoShowSettingsPage : UserControl
{
    public ExamAutoShowSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamAutoShowSettingsPageViewModel>();

        // ViewModel 是 transient、考试服务与校时服务是单例：订阅必须在离开可视树时断开。
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) =>
        {
            ViewModel?.CommitAll();
            ViewModel?.Detach();
        };
    }

    private ExamAutoShowSettingsPageViewModel? ViewModel => DataContext as ExamAutoShowSettingsPageViewModel;

    /// <summary>提前量 / 延迟输入框失焦就提交。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitAll();
}
