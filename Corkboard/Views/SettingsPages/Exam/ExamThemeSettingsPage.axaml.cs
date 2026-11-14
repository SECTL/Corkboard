using Avalonia.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Exam;

namespace Corkboard.Views.SettingsPages.Exam;

/// <summary>
///     「看板主题」设置页：考试看板的 13 个语义颜色从哪来、自定义时逐项改色，
///     以及「恢复默认主题」「恢复所有颜色」。
/// </summary>
[PageInfo("settings.exam.theme", FluentIcons.ColorFilled, "settings.exam.group")]
public partial class ExamThemeSettingsPage : UserControl
{
    public ExamThemeSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamThemeSettingsPageViewModel>();

        // ViewModel 是 transient、主题服务与配置是单例：订阅必须在离开可视树时断开。
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) =>
        {
            ViewModel?.CommitAll();
            ViewModel?.Detach();
        };
    }

    private ExamThemeSettingsPageViewModel? ViewModel => DataContext as ExamThemeSettingsPageViewModel;
}