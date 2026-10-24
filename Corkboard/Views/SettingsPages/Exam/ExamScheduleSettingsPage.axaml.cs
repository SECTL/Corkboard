using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.ExamBoard;
using Corkboard.ViewModels.SettingsPages.Exam;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.SettingsPages.Exam;

/// <summary>
///     「考试安排」设置页：看板文件列表（一份看板 = 一个文件），每行可编辑 / 设为当前 / 删除。
///     <para>
///         真正的编辑在 <see cref="Corkboard.Views.ExamBoard.ExamBoardEditorWindow" /> 里做：
///         设置页这一列限宽 960，放不下「十来列的表 + 十来个字段的表单」，硬塞只会互相压扁。
///     </para>
/// </summary>
[PageInfo("settings.exam.schedule", FluentIcons.CalendarFilled, "settings.exam.group")]
public partial class ExamScheduleSettingsPage : UserControl
{
    public ExamScheduleSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamScheduleSettingsPageViewModel>();

        // ViewModel 是 transient、考试服务是单例：订阅必须在离开可视树时断开，
        // 否则每开一次这个页面就往服务上多挂一个处理器。
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) => ViewModel?.Detach();
    }

    private ExamScheduleSettingsPageViewModel? ViewModel => DataContext as ExamScheduleSettingsPageViewModel;

    /// <summary>「全屏看板」：开一块整屏的时钟 + 考试表。</summary>
    private void OnOpenBoardClick(object? sender, RoutedEventArgs e) => App.ShowExamBoardWindow();

    /// <summary>「新建看板」：多一份空白看板文件，并直接把编辑窗口开到它上面。</summary>
    private void OnCreateBoardClick(object? sender, RoutedEventArgs e) =>
        ViewModel?.CreateBoardCommand.Execute(null);

    /// <summary>
    ///     「快速布置」：开向导，按科目 + 作息一次排完一整场考试，完成时新建一块看板。
    ///     <para>
    ///         与「新建看板」并列摆着：从零开始的人多半是要排一场考试，不是要一块空板。
    ///     </para>
    /// </summary>
    private void OnQuickPlanClick(object? sender, RoutedEventArgs e) =>
        App.ShowExamPlanWizardWindow(ExamPlanWizardMode.CreateBoard);

    /// <summary>某一行点「编辑」：把这块看板设成当前，再开编辑窗口。</summary>
    private void OnEditBoardClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExamBoardItem item)
            return;

        ViewModel?.SelectBoard(item);
        App.ShowExamBoardEditorWindow(item.Id);
    }

    /// <summary>某一行点「设为当前」：全屏看板换成这一块。</summary>
    private void OnActivateBoardClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ExamBoardItem item)
            ViewModel?.SelectBoard(item);
    }

    /// <summary>某一行点「删除」：连里面的考试一起删，先确认一次。</summary>
    private async void OnDeleteBoardClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExamBoardItem item
            || ViewModel is not { } viewModel
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Exam_Schedule_BoardDeleteTitle,
            Content = string.Format(CR.Settings_Exam_Schedule_BoardDeleteBody, item.Title),
            PrimaryButtonText = CR.Settings_Exam_Schedule_BoardDelete,
            CloseButtonText = CR.Common_Cancel
        };

        if (await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary)
            viewModel.DeleteBoard(item);
    }

    /// <summary>某一份模板点「删除」：模板删掉，已经排进看板的考试不受影响，先确认一次。</summary>
    private async void OnDeleteTemplateClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExamTemplateItem item
            || ViewModel is not { } viewModel
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Exam_Plan_TemplateDeleteTitle,
            Content = string.Format(CR.Settings_Exam_Plan_TemplateDeleteBody, item.Name),
            PrimaryButtonText = CR.Settings_Exam_Plan_TemplateDelete,
            CloseButtonText = CR.Common_Cancel
        };

        if (await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary)
            viewModel.DeleteTemplate(item);
    }
}
