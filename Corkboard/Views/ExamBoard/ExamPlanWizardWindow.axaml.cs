using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Corkboard.Core.Exam;
using Corkboard.Services.Desktop;
using Corkboard.Services.Ui;
using Corkboard.ViewModels.ExamBoard;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.ExamBoard;

/// <summary>
///     「快速布置考试」向导窗口。
///     <para>
///         窗口本身几乎不做事：四步的内容全在 <see cref="ExamPlanWizardViewModel" /> 里，
///         这里只负责「完成」之后把新看板的编辑窗口打开、以及窗口关闭时断开订阅。
///     </para>
/// </summary>
public partial class ExamPlanWizardWindow : FAAppWindow
{
    private readonly ExamPlanWizardViewModel _viewModel;

    /// <summary>XAML 设计器用的空构造。</summary>
    public ExamPlanWizardWindow()
    {
        InitializeComponent();
        _viewModel = null!;
    }

    public ExamPlanWizardWindow(ExamPlanWizardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // 与编辑窗口一样喂多尺寸 ICO：这个窗口在任务栏里有按钮，图标必须是 Corkboard 自己的。
        ((Window)this).Icon = AppIcon.LoadWindowIcon();        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e) => _viewModel.Attach();

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Detach();
        Closed -= OnClosed;
    }

    /// <summary>
    ///     「下一步」/「完成」：最后一步才真正写盘，写完立刻开编辑窗口。
    ///     <para>
    ///         按钮上挂的是 <c>Click</c> 而不是 <c>Command</c>，因为最后一步要做「写盘 + 开窗口 + 关自己」
    ///         这一串，光靠命令没法让窗口收尾；前几步仍然交给命令推进。
    ///     </para>
    /// </summary>
    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsLastStep)
        {
            _viewModel.NextCommand.Execute(null);
            return;
        }

        if (!_viewModel.TryFinish())
            return;

        var boardId = _viewModel.ResultBoardId;
        Close();

        if (boardId is { } id)
            App.ShowExamBoardEditorWindow(id);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>科目输入框里回车即添加（与作业板那些「草稿 + 回车提交」的地方一致）。</summary>
    private void OnAddSubjectKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        _viewModel.AddSubjectCommand.Execute(null);
        e.Handled = true;
    }

    private void OnMoveSubjectUpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ExamPlanSubjectItem item })
            _viewModel.MoveSubjectUp(item);
    }

    private void OnMoveSubjectDownClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ExamPlanSubjectItem item })
            _viewModel.MoveSubjectDown(item);
    }

    private void OnRemoveSubjectClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ExamPlanSubjectItem item })
            _viewModel.RemoveSubject(item);
    }

    private void OnRemoveRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ExamPlanRowItem row })
            _viewModel.RemoveRow(row);
    }

    /// <summary>把这一场往前挪（= 换到更早的场次／更早的那一天，见 <c>MoveRow</c>）。</summary>
    private void OnMoveRowUpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ExamPlanRowItem row })
            _viewModel.MoveRow(row, -1);
    }

    /// <summary>把这一场往后挪。</summary>
    private void OnMoveRowDownClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ExamPlanRowItem row })
            _viewModel.MoveRow(row, 1);
    }

    /// <summary>
    ///     「存为作息模板」：弹框问一个名字，把当前这张作息表存进 <c>data/exam/slot-presets.json</c>。
    ///     与看板编辑窗口的「另存为模板」同一套写法（<see cref="FAContentDialog" /> + 输入框 + 一句提示）。
    /// </summary>
    private async void OnSaveSlotPresetClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        // 名字默认给「当前选中的那份」，没选中就给空（存的时候服务会兜一个「未命名作息」）。
        var nameBox = new TextBox
        {
            Text = _viewModel.SelectedSlotPreset?.IsBuiltIn == false ? _viewModel.SelectedSlotPreset.Name : string.Empty,
            PlaceholderText = CR.Settings_Exam_Plan_SaveSlotPresetNamePlaceholder,
            MinWidth = 320
        };

        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Exam_Plan_SaveSlotPresetTitle,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = CR.Settings_Exam_Plan_SaveSlotPresetBody,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    nameBox
                }
            },
            PrimaryButtonText = CR.Settings_Exam_Plan_SaveSlotPreset,
            CloseButtonText = CR.Common_Cancel
        };

        using var scope = ModalOpacityScope.Begin(this);

        var result = await dialog.ShowAsync(topLevel);
        if (result != FAContentDialogResult.Primary)
            return;

        if (_viewModel.SaveCurrentSlots(nameBox.Text) is { } preset)
            await ShowNoticeAsync(
                topLevel,
                string.Format(CR.Settings_Exam_Plan_SaveSlotPresetDone, preset.ResolveName()));
    }

    /// <summary>删掉当前选中的作息模板（内置的不显示这颗按钮，服务那边也会再拦一次）。</summary>
    private async void OnDeleteSlotPresetClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        if (_viewModel.SelectedSlotPreset is not { IsBuiltIn: false } preset)
            return;

        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Exam_Plan_SlotPresetDeleteTitle,
            Content = string.Format(CR.Settings_Exam_Plan_SlotPresetDeleteBody, preset.Name),
            PrimaryButtonText = CR.Settings_Exam_Plan_SlotPresetDelete,
            CloseButtonText = CR.Common_Cancel
        };

        using var scope = ModalOpacityScope.Begin(this);

        if (await dialog.ShowAsync(topLevel) != FAContentDialogResult.Primary)
            return;

        _viewModel.DeleteSlotPreset(preset.Preset);
    }

    /// <summary>一句话提示（只有「知道了」一个按钮）。</summary>
    private async Task ShowNoticeAsync(TopLevel topLevel, string message)
    {
        var dialog = new FAContentDialog
        {
            Title = CR.Settings_Exam_Plan_SlotsTitle,
            Content = message,
            CloseButtonText = CR.Common_Close
        };

        using var scope = ModalOpacityScope.Begin(this);
        await dialog.ShowAsync(topLevel);
    }
}
