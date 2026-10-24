using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Exam;
using Corkboard.Services.Desktop;
using Corkboard.Services.Ui;
using Corkboard.ViewModels.ExamBoard;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.ExamBoard;

/// <summary>
///     「编辑看板」窗口：一块看板文件里的标题、副标题、颜色与全部考试都在这里改。
///     <para>
///         关窗不弹确认：标题、副标题与每一场考试的改动**当场就写进看板文件**了（表单点「保存」时写），
///         关掉不会丢东西，没必要拦一道。
///     </para>
///     <para>
///         ViewModel 是 transient、考试服务是单例：订阅与计时器必须在窗口关闭时一起收掉，
///         否则每开一次窗口就多挂一份处理器、多跑一个 15 秒的计时器。
///     </para>
/// </summary>
public partial class ExamBoardEditorWindow : FAAppWindow
{
    public ExamBoardEditorWindow()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExamBoardEditorViewModel>();

        // 和主窗口一样喂多尺寸 ICO：不设的话 Windows 会退回 Avalonia 自带的默认图标，
        // 而这个窗口在任务栏里有按钮，图标必须是 Corkboard 自己的。见 AppIcon。
        ((Window)this).Icon = AppIcon.LoadWindowIcon();

        Opened += (_, _) => ViewModel?.Attach();
        Closed += (_, _) =>
        {
            // 关窗前把还压在草稿里的标题 / 副标题 / 颜色落一次盘，别让最后敲的那几个字丢掉。
            ViewModel?.CommitAll();
            ViewModel?.Detach();
        };
    }

    private ExamBoardEditorViewModel? ViewModel => DataContext as ExamBoardEditorViewModel;

    /// <summary>换了一块看板（窗口已经开着时由 <see cref="App.ShowExamBoardEditorWindow" /> 调）。</summary>
    public void ReloadBoard() => ViewModel?.ReloadBoard();

    /// <summary>某一行点「编辑」：把那场考试填进表单。</summary>
    private void OnEditExamClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ExamScheduleItem item)
            ViewModel?.BeginEdit(item);
    }

    /// <summary>某一行点「删除」：先确认，再删。</summary>
    private async void OnDeleteExamClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExamScheduleItem item
            || ViewModel is not { } viewModel
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Exam_Schedule_DeleteTitle,
            Content = string.Format(CR.Settings_Exam_Schedule_DeleteBody, item.Subject),
            PrimaryButtonText = CR.Settings_Exam_Schedule_Delete,
            CloseButtonText = CR.Common_Cancel
        };

        if (await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary)
            viewModel.Remove(item);
    }

    /// <summary>「取消」：表单退回新增态。</summary>
    private void OnCancelEditClick(object? sender, RoutedEventArgs e) => ViewModel?.CancelEdit();

    /// <summary>标题 / 副标题输入框失焦就提交：看板文件整份重写，没必要每敲一个字符写一遍。</summary>
    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CommitAll();

    /// <summary>输入框里按回车立刻提交，不用先点别处。</summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        e.Handled = true;
        ViewModel?.CommitAll();
    }

    /// <summary>科目 / 备注输入框里按回车即保存，连着录几场更顺手。</summary>
    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        e.Handled = true;
        if (ViewModel?.SubmitCommand.CanExecute(null) == true)
            ViewModel.SubmitCommand.Execute(null);
    }

    /// <summary>「全屏看板」：先把手里的改动落盘，再开那块整屏的板子看效果。</summary>
    private void OnPreviewClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CommitAll();
        App.ShowExamBoardWindow();
    }

    /// <summary>
    ///     「快速布置」：开向导，往**当前这块**看板批量追加考试。
    ///     <para>
    ///         点之前先把标题 / 副标题的草稿落盘：向导排完会重建这张表，用户敲了一半的字不该丢。
    ///     </para>
    /// </summary>
    private void OnQuickPlanClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CommitAll();
        App.ShowExamPlanWizardWindow(ExamPlanWizardMode.AppendToBoard);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    ///     「另存为模板」：弹一个只有输入框的确认框问名字，存完在窗口标题栏下方出一行提示。
    ///     <para>
    ///         没有考试时不弹框，直接说一句「先录几条」——空模板存下来也没用。
    ///     </para>
    /// </summary>
    private async void OnSaveTemplateClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        if (viewModel.Items.Count == 0)
        {
            await ShowNoticeAsync(topLevel, CR.Settings_Exam_Plan_SaveTemplateEmpty);
            return;
        }

        // 名字默认给「看板标题」，用户直接回车就能存。
        var nameBox = new TextBox
        {
            Text = viewModel.BoardTitle,
            PlaceholderText = CR.Settings_Exam_Plan_SaveTemplateNamePlaceholder,
            MinWidth = 320
        };

        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Exam_Plan_SaveTemplateTitle,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = CR.Settings_Exam_Plan_SaveTemplateBody,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    nameBox
                }
            },
            PrimaryButtonText = CR.Settings_Exam_Plan_SaveTemplate,
            CloseButtonText = CR.Common_Cancel
        };

        using var scope = ModalOpacityScope.Begin(this);

        var result = await dialog.ShowAsync(topLevel);
        if (result != FAContentDialogResult.Primary)
            return;

        if (viewModel.SaveAsTemplate(nameBox.Text) is { } template)
            await ShowNoticeAsync(topLevel, string.Format(CR.Settings_Exam_Plan_SaveTemplateDone, template.ResolveName()));
    }

    /// <summary>一句话提示（只有「知道了」一个按钮）。</summary>
    private async Task ShowNoticeAsync(TopLevel topLevel, string message)
    {
        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Exam_Plan_SaveTemplateTitle,
            Content = message,
            CloseButtonText = CR.Common_Close
        };

        using var scope = ModalOpacityScope.Begin(this);
        await dialog.ShowAsync(topLevel);
    }
}
