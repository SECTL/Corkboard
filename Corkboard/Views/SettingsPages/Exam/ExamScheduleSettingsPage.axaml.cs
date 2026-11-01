using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.Services.Exam;
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

    /// <summary>
    ///     「导入考试」：挑一份 <c>.cbexam</c>，把里面的考试追加进**当前这块**看板。
    ///     <para>
    ///         追加而不是替换：导入的多半是别人发来的一两场，不该把看板里原有的排期冲掉。
    ///         导入完不改当前看板选择，也不开编辑窗口——用户可能只是先攒着。
    ///     </para>
    /// </summary>
    private async void OnImportExamsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        try
        {
            var exams = await IAppHost.GetService<ExamFileService>().ImportExamsAsync(topLevel);
            if (exams is null)
                return;

            if (exams.Count == 0)
            {
                await ShowNoticeAsync(topLevel, CR.Exam_File_ImportEmpty);
                return;
            }

            var added = viewModel.ImportExams(exams);
            await ShowNoticeAsync(topLevel, string.Format(CR.Exam_File_ImportDone, added));
        }
        catch (InvalidOperationException exception)
        {
            await ShowNoticeAsync(topLevel, exception.Message);
        }
    }

    /// <summary>
    ///     「导出考试」：把一块看板的全部考试存成一份 <c>.cbexam</c>。
    ///     <para>
    ///         <b>两个入口共用这一个处理器</b>：看板行上那颗箭头导出的是**那一行**的看板；
    ///         页脚那颗导出的是**当前看板**（`sender` 不是看板行时退回当前看板）。
    ///         行内那颗是用户真正会点的入口——要导出某一块就得先进去翻表太绕了（用户反馈过
    ///         「为什么要点进去才能导出」）。
    ///     </para>
    /// </summary>
    private async void OnExportBoardClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        // 行内按钮的 DataContext 是那一行的 ExamBoardItem；页脚按钮没有行上下文。
        var board = (sender as Control)?.DataContext is ExamBoardItem item ? item.Board : viewModel.ActiveBoard;
        if (board.Exams is null || board.Exams.Count == 0)
        {
            await ShowNoticeAsync(topLevel, CR.Exam_File_ExportEmpty, CR.Settings_Exam_Schedule_ExportAll);
            return;
        }

        try
        {
            if (await IAppHost.GetService<ExamFileService>().ExportBoardAsync(board, topLevel))
                await ShowNoticeAsync(topLevel, string.Format(CR.Exam_File_ExportDone, board.Exams.Count),
                    CR.Settings_Exam_Schedule_ExportAll);
        }
        catch (InvalidOperationException exception)
        {
            await ShowNoticeAsync(topLevel, exception.Message, CR.Settings_Exam_Schedule_ExportAll);
        }
    }

    /// <summary>一句话提示（只有「知道了」一个按钮）。</summary>
    private async Task ShowNoticeAsync(TopLevel topLevel, string message, string? title = null)
    {
        var dialog = new FAContentDialog
        {
            Title = title ?? CR.Settings_Exam_Schedule_Import,
            Content = message,
            CloseButtonText = CR.Common_Close
        };

        await dialog.ShowAsync(topLevel);
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
