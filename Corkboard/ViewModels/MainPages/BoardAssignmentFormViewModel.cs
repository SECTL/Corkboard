using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Abstraction.Services;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Ui;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.MainPages;

/// <summary>
///     「布置作业」表单的 ViewModel，显示在壳的页面级弹层里（<see cref="PageOverlayService" />）。
///     <para>
///         同一份表单兼管**编辑已有作业**：<see cref="LoadForEdit" /> 之后按钮文案变成「保存」，
///         确定时走 <see cref="IBoardService.Update" /> 而不是 <see cref="IBoardService.Add" />。
///     </para>
///     <para>
///         内容是一段富文本 HTML（<see cref="DraftHtml" />），编辑与渲染都由表单里的
///         <c>RichTextBlock</c> 负责：它是真正的所见即所得，没有「原文态 / 效果态」的切换。
///         双向绑定回写过来的 HTML 已经是摘掉默认样式之后的干净版本，确定时直接落盘。
///     </para>
///     <para>
///         颜色与字号是编辑器自己的行为（对选中文字生效，默认值见 <see cref="BaseFontSize" /> /
///         <see cref="BaseColor" />），这里不需要再记标注，也不需要跟着文本位移挪标注。
///     </para>
///     <para>
///         表单活得很短：每次打开都新建一个，类型清单只在构造时读一次，
///         所以不订阅 <see cref="IBoardService.Changed" />，也就没有 transient 订阅单例的回收问题。
///     </para>
/// </summary>
public partial class BoardAssignmentFormViewModel : ObservableObject
{
    private readonly IBoardService _boardService;
    private readonly PageOverlayService _overlay;
    private readonly BoardSettingsConfig _settings;

    /// <summary>正在编辑的作业 Id；为 <c>null</c> 表示这是「布置作业」。</summary>
    private Guid? _editingNoteId;

    public BoardAssignmentFormViewModel(
        IBoardService boardService, PageOverlayService overlay, MainConfigHandler configHandler)
    {
        _boardService = boardService;
        _overlay = overlay;
        _settings = configHandler.Data.BoardSettings;

        BaseFontSize = BoardContentStyle.ClampFontSize(_settings.DefaultContentFontSize);
        BaseColor = _settings.DefaultContentColor;

        TypeOptions = [new BoardTypeOption(null, CR.Board_TypeDirect)];
        foreach (var type in _boardService.Types)
            TypeOptions.Add(new BoardTypeOption(type, type.DisplayName));

        SubjectOptions = [.. _boardService.Subjects];

        // 默认停在「直接写」，并顺带把字段区建起来。
        SelectedOption = TypeOptions[0];
    }

    /// <summary>类型下拉的选项，第一项是「直接写」。</summary>
    public ObservableCollection<BoardTypeOption> TypeOptions { get; } = [];

    /// <summary>当前类型要填的字段。选「直接写」时是空的。</summary>
    public ObservableCollection<BoardFieldDraft> DraftFields { get; } = [];

    /// <summary>
    ///     科目下拉的选项。默认就是科目清单；编辑一条科目已被设置页删掉的旧作业时，
    ///     会把那一条补进来，免得下拉框空着、一保存就把科目洗没了。
    /// </summary>
    public ObservableCollection<string> SubjectOptions { get; } = [];

    /// <summary>设置里配的默认字号：内容没单独设过字号时用的就是它。</summary>
    public double BaseFontSize { get; }

    /// <summary>设置里配的默认颜色；为空表示跟随主题。</summary>
    public Color? BaseColor { get; }

    [ObservableProperty] private BoardTypeOption? _selectedOption;

    /// <summary>内容的 HTML 片段，编辑器的双向绑定源（回写时已经是干净版本）。</summary>
    [ObservableProperty] private string _draftHtml = string.Empty;

    [ObservableProperty] private string? _draftSubject;

    /// <summary>
    ///     截止日期选择器里的草稿（<c>CalendarDatePicker.SelectedDate</c> 就是这个类型）。
    ///     为 <c>null</c> 表示这条作业不设截止日期。
    /// </summary>
    [ObservableProperty] private DateTime? _dueDateDraft;

    /// <summary>
    ///     表单里的截止日期：选择器里挑了哪天就是哪天，没挑就是 <c>null</c>（这条作业不设截止日期）。
    ///     <para>只取日历日，时间部分丢掉——过期判定按自然日算。</para>
    /// </summary>
    public DateOnly? DraftDueDate => DueDateDraft is { } due ? DateOnly.FromDateTime(due) : null;

    /// <summary>挑没挑日期。「清除」按钮靠它决定显不显示。</summary>
    public bool HasDueDate => DueDateDraft is not null;

    public bool HasDraftFields => DraftFields.Count > 0;

    /// <summary>是否在编辑已有作业。为真时表单显示「保存」，标题也换一个。</summary>
    public bool IsEditing => _editingNoteId is not null;

    public string FormTitle => IsEditing ? CR.Board_EditTitle : CR.Board_AddNote;

    /// <summary>确定按钮的文案：新建是「布置」，编辑是「保存」。</summary>
    public string ConfirmText => IsEditing ? CR.Board_SaveEdit : CR.Board_ConfirmAdd;

    /// <summary>内容或任一字段有内容才让点确定，避免建出空作业。</summary>
    public bool CanConfirm =>
        DraftHtml.Trim().Length > 0 || DraftFields.Any(draft => draft.HasValue);

    /// <summary>把这条作业的内容填进表单，之后确定就变成「保存」。</summary>
    public void LoadForEdit(BoardNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        _editingNoteId = note.Id;

        var subject = note.Subject.Trim();
        if (subject.Length > 0 && !SubjectOptions.Contains(subject, StringComparer.Ordinal))
            SubjectOptions.Add(subject);

        // 先定类型：这一步会按类型重建字段区，值必须在重建之后才填得进去。
        SelectedOption = TypeOptions.FirstOrDefault(option => option.Type?.Id == note.TypeId) ?? TypeOptions[0];
        BuildDraftFields();

        DraftSubject = subject.Length > 0 ? subject : null;

        // 内容已经是富文本 HTML（旧数据在加载作业时迁过），直接交给编辑器装填。
        DraftHtml = note.Content ?? string.Empty;

        // 截止日期：没设过的作业留空，选择器就是空的。
        DueDateDraft = note.DueDate is { } due ? due.ToDateTime(TimeOnly.MinValue) : null;

        foreach (var draft in DraftFields)
        {
            if (note.Values.TryGetValue(draft.Field.Id.ToString(), out var raw) && !string.IsNullOrEmpty(raw))
                draft.LoadRawValue(raw);
        }

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(ConfirmText));
        OnPropertyChanged(nameof(CanConfirm));
    }

    [RelayCommand]
    private void Cancel()
    {
        _overlay.Close();
    }

    /// <summary>把表单落成一份作业（新建）或写回原作业（编辑），然后关掉弹层。</summary>
    [RelayCommand]
    private void Confirm()
    {
        if (!CanConfirm)
            return;

        var note = new BoardNote
        {
            TypeId = SelectedOption?.Type?.Id,
            Subject = DraftSubject?.Trim() ?? string.Empty,
            DueDate = DraftDueDate
        };

        // 内容是富文本片段：一次写好 Content 与 ContentKind，顺手清掉旧的标注字段。
        note.SetHtmlContent(DraftHtml.Trim());

        foreach (var draft in DraftFields.Where(draft => draft.HasValue))
            note.Values[draft.Field.Id.ToString()] = draft.RawValue;

        if (_editingNoteId is { } id)
        {
            // 编辑：拿 Id 找回原作业覆盖，创建时间与归档位置都不动，只刷新修改时间。
            note.Id = id;
            if (_boardService.Update(note))
            {
                _overlay.Close();
            }

            return;
        }

        _boardService.Add(note);
        _overlay.Close();
    }

    partial void OnSelectedOptionChanged(BoardTypeOption? value)
    {
        BuildDraftFields();
    }

    partial void OnDraftHtmlChanged(string value)
    {
        OnPropertyChanged(nameof(CanConfirm));
    }

    partial void OnDueDateDraftChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(DraftDueDate));
        OnPropertyChanged(nameof(HasDueDate));
    }

    /// <summary>
    ///     把已挑的日期清回「不设」。选择器自己没地方退回空值（点日历只会换一天），
    ///     所以「这条作业不要截止日期」得由外面这个按钮给。
    /// </summary>
    [RelayCommand]
    private void ClearDueDate() => DueDateDraft = null;

    private void BuildDraftFields()
    {
        foreach (var draft in DraftFields)
            draft.PropertyChanged -= OnDraftFieldChanged;

        DraftFields.Clear();
        if (SelectedOption?.Type is { } type)
        {
            foreach (var field in type.Fields)
            {
                var draft = BoardFieldDraftFactory.Create(field);
                draft.PropertyChanged += OnDraftFieldChanged;
                DraftFields.Add(draft);
            }
        }

        OnPropertyChanged(nameof(HasDraftFields));
        OnPropertyChanged(nameof(CanConfirm));
    }

    private void OnDraftFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoardFieldDraft.HasValue) or null)
            OnPropertyChanged(nameof(CanConfirm));
    }
}
