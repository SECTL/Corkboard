using Avalonia.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Exam;

namespace Corkboard.Views.SettingsPages.Exam;

/// <summary>
///     「看板布局」设置页：四档考试状态分别打开独立的全屏布局编辑窗。
///     编辑窗进来就停在对应状态，用示例考试调摆法，改完一次性写回配置。
/// </summary>
[PageInfo("settings.exam.layout", FluentIcons.FullScreenMaximizeFilled, "settings.exam.group")]
public partial class ExamLayoutSettingsPage : UserControl
{
    public ExamLayoutSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamLayoutSettingsPageViewModel>();

        // ViewModel 是 transient、布局服务是单例：订阅必须在离开可视树时断开。
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) => ViewModel?.Detach();
    }

    private ExamLayoutSettingsPageViewModel? ViewModel => DataContext as ExamLayoutSettingsPageViewModel;
}