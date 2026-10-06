using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Abstraction.Services;
using Corkboard.Core.Enums;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Board;
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
///         内容支持 Markdown，另外可以给选中的字（没选中就是光标所在的那一段）单独指定颜色与字号，
///         也能给整篇定一个字号。这些都不落在文本里，而是记成
///         <see cref="BoardTextFormatRange" />（原文偏移 + 颜色/字号）；文本一改，
///         由 <see cref="BoardTextFormatEditing.Shift" /> 把标注挪到新位置。
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

    /// <summary>内容里的颜色/字号标注。草稿，确定时才克隆进作业。</summary>
    private readonly List<BoardTextFormatRange> _formats = [];

    /// <summary>上一次的文本，用来算出这次编辑动了哪一段（见 <see cref="OnDraftContentChanged" />）。</summary>
    private string _lastContent = string.Empty;

    /// <summary>文本框最近一次的光标/选区位置，由页面递进来。</summary>
    private int _selectionStart;
    private int _selectionEnd;

    /// <summary>取色器当前色。草稿式的：拖光谱每帧都在变，但只改视图，不写盘。</summary>
    private Color _selectionColorDraft;

    /// <summary>装填阶段（构造、<see cref="LoadForEdit" />）不把草稿变化当成用户操作。</summary>
    private bool _suppressFormatChanges;

    public BoardAssignmentFormViewModel(
        IBoardService boardService, PageOverlayService overlay, MainConfigHandler configHandler)
    {
        _boardService = boardService;
        _overlay = overlay;
        _settings = configHandler.Data.BoardSettings;

        BaseFontSize = BoardContentStyle.ClampFontSize(_settings.DefaultContentFontSize);
        BaseColor = _settings.DefaultContentColor;
        SizeOptions = BoardFontSizeOption.Build(BaseFontSize, includeFollowDefault: false);
        NoteSizeOptions = BoardFontSizeOption.Build(BaseFontSize, includeFollowDefault: true);
        RefreshPaletteColors();

        _suppressFormatChanges = true;
        SelectionColorDraft = BaseColor ?? Colors.Black;
        _suppressFormatChanges = false;

        TypeOptions = [new BoardTypeOption(null, CR.Board_TypeDirect)];
        foreach (var type in _boardService.Types)
            TypeOptions.Add(new BoardTypeOption(type, type.DisplayName));

        SubjectOptions = [.. _boardService.Subjects];

        // 默认停在「直接写」，并顺带把字段区建起来。
        SelectedOption = TypeOptions[0];

        // 「整篇字号」默认跟随设置里的默认字号。
        SelectedNoteSizeOption = NoteSizeOptions[0];
        RefreshPreview();
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

    /// <summary>选段字号的下拉档位（不含「跟随默认」）。</summary>
    public IReadOnlyList<BoardFontSizeOption> SizeOptions { get; }

    /// <summary>「整篇字号」的下拉档位，第一项是跟随设置里的默认字号。</summary>
    public IReadOnlyList<BoardFontSizeOption> NoteSizeOptions { get; }

    /// <summary>设置里配的默认字号：整篇没定字号时，预览与板子都用它。</summary>
    public double BaseFontSize { get; }

    /// <summary>设置里配的默认颜色；为空表示跟随主题。</summary>
    public Color? BaseColor { get; }

    /// <summary>
    ///     工具浮窗上那排预设色，来自「设置 → 作业板 → 快速颜色」。
    ///     设置页改动之后浮窗下次打开就会跟着变，所以每次打开浮窗都重读一次。
    /// </summary>
    public ObservableCollection<Color> PaletteColors { get; } = [];

    [ObservableProperty] private BoardTypeOption? _selectedOption;
    [ObservableProperty] private string _draftContent = string.Empty;
    [ObservableProperty] private string? _draftSubject;
    [ObservableProperty] private BoardFontSizeOption? _selectedNoteSizeOption;

    /// <summary>整篇字号；为空表示跟随设置里的默认字号。</summary>
    [ObservableProperty] private double? _draftFontSize;

    /// <summary>预览用的渲染输入。每次改动换一个新实例，控件只比较引用。</summary>
    [ObservableProperty] private BoardRichText _preview = BoardRichText.Empty;

    public bool HasDraftFields => DraftFields.Count > 0;

    /// <summary>内容里有没有字。空内容时预览位置显示一句提示，而不是一片空白。</summary>
    public bool HasPreviewContent => DraftContent.Length > 0;

    /// <summary>是否在编辑已有作业。为真时表单显示「保存」，标题也换一个。</summary>
    public bool IsEditing => _editingNoteId is not null;

    public string FormTitle => IsEditing ? CR.Board_EditTitle : CR.Board_AddNote;

    /// <summary>确定按钮的文案：新建是「布置」，编辑是「保存」。</summary>
    public string ConfirmText => IsEditing ? CR.Board_SaveEdit : CR.Board_ConfirmAdd;

    /// <summary>内容或任一字段有内容才让点确定，避免建出空作业。</summary>
    public bool CanConfirm =>
        DraftContent.Trim().Length > 0 || DraftFields.Any(draft => draft.HasValue);

    /// <summary>
    ///     取色器的草稿值。
    ///     <para>
    ///         不直接双向绑配置（这里也压根不是配置）：拖光谱每帧都会变，直接在 setter 里
    ///         把颜色套到选段上正好——预览是随打随变的，不用防抖。装填阶段用
    ///         <see cref="_suppressFormatChanges" /> 挡住，免得一打开表单就给第一段上了色。
    ///     </para>
    /// </summary>
    public Color SelectionColorDraft
    {
        get => _selectionColorDraft;
        set
        {
            if (_selectionColorDraft == value)
                return;

            _selectionColorDraft = value;
            OnPropertyChanged();

            if (!_suppressFormatChanges)
                ApplyColorToSelection(value);
        }
    }

    /// <summary>
    ///     重新从设置里读一次预设色板。设置页改动后浮窗不会自动跟着变（两边是不同的 VM 实例），
    ///     所以每次打开表单 / 浮窗时刷一遍，用户改完颜色回来就能看到。
    /// </summary>
    public void RefreshPaletteColors()
    {
        var colors = _settings.ResolvePaletteColors();

        if (PaletteColors.SequenceEqual(colors))
            return;

        PaletteColors.Clear();
        foreach (var color in colors)
            PaletteColors.Add(color);
    }

    /// <summary>把这条作业的内容填进表单，之后确定就变成「保存」。</summary>
    public void LoadForEdit(BoardNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        // 用户可能刚在设置页改过色板，再开浮窗要用最新的那套。
        RefreshPaletteColors();

        _editingNoteId = note.Id;
        _suppressFormatChanges = true;
        try
        {
            var subject = note.Subject.Trim();
            if (subject.Length > 0 && !SubjectOptions.Contains(subject, StringComparer.Ordinal))
                SubjectOptions.Add(subject);

            // 先定类型：这一步会按类型重建字段区，值必须在重建之后才填得进去。
            SelectedOption = TypeOptions.FirstOrDefault(option => option.Type?.Id == note.TypeId) ?? TypeOptions[0];
            BuildDraftFields();

            DraftSubject = subject.Length > 0 ? subject : null;

            // 文本先落地再装标注：DraftContent 一变就会按「上一次文本」算位移，
            // 这时标注还是空的，不会被动过。
            DraftContent = note.Content;
            _formats.Clear();
            _formats.AddRange(note.Formats.Select(range => range.Clone()));
            _lastContent = note.Content;

            SelectedNoteSizeOption = NoteSizeOptions.FirstOrDefault(option => option.Size == note.ContentFontSize)
                                     ?? NoteSizeOptions[0];

            foreach (var draft in DraftFields)
            {
                if (note.Values.TryGetValue(draft.Field.Id.ToString(), out var raw) && !string.IsNullOrEmpty(raw))
                    draft.LoadRawValue(raw);
            }
        }
        finally
        {
            _suppressFormatChanges = false;
        }

        RefreshPreview();
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(ConfirmText));
        OnPropertyChanged(nameof(CanConfirm));
    }

    /// <summary>页面把文本框的选区位置递进来（<c>TextBox.SelectionChanged</c> 与挂载时各调一次）。</summary>
    public void UpdateSelection(int selectionStart, int selectionEnd)
    {
        _selectionStart = selectionStart;
        _selectionEnd = selectionEnd;
    }

    /// <summary>给选中的字（没选中就是光标所在的那一段）定字号；<paramref name="size" /> 为空表示去掉字号。</summary>
    public void ApplyFontSizeToSelection(double? size)
    {
        var (start, length) = ResolveTargetSpan();
        BoardTextFormatEditing.ApplyFontSize(_formats, start, length, size);
        RefreshPreview();
    }

    /// <summary>给选中的字（没选中就是光标所在的那一段）定颜色；<paramref name="color" /> 为空表示去掉颜色。</summary>
    public void ApplyColorToSelection(Color? color)
    {
        var (start, length) = ResolveTargetSpan();
        BoardTextFormatEditing.ApplyColor(_formats, start, length, color);
        RefreshPreview();
    }

    /// <summary>把选中那一段的颜色与字号都清掉，回到整篇默认。</summary>
    public void ClearSelectionFormat()
    {
        var (start, length) = ResolveTargetSpan();
        BoardTextFormatEditing.Clear(_formats, start, length);
        RefreshPreview();
    }

    /// <summary>
    ///     给当前选段套一个 Markdown 记号（格式浮窗里的加粗 / 斜体 / 标题 / 列表 / 行内代码）。
    ///     <para>
    ///         返回套完之后该重新选中的区间，界面据此把 <c>TextBox</c> 的选区摆回去：
    ///         记号落在选区外面，用户才能接着点下一个格式。文本没变化时返回 <c>null</c>。
    ///     </para>
    /// </summary>
    public (int Start, int Length)? ApplyMarkdown(BoardMarkdownFormat format)
    {
        var result = BoardMarkdownEditing.Toggle(DraftContent, _selectionStart, _selectionEnd, format);
        if (string.Equals(result.Text, DraftContent, StringComparison.Ordinal))
            return null;

        // 先落文本：DraftContent 一变就会把颜色/字号标注按公共前后缀挪到新位置（见 OnDraftContentChanged）。
        DraftContent = result.Text;

        // 再把「当前选区」记成记号之外的那段，后面的操作继续照着它来。
        _selectionStart = result.SelectionStart;
        _selectionEnd = result.SelectionStart + result.SelectionLength;

        return (result.SelectionStart, result.SelectionLength);
    }

    /// <summary>
    ///     当前选段正在生效的字号：界面用它把浮窗上的「字号」显示成选段当前的值，
    ///     而不是让下拉永远空着（用户得先猜现在是多少号）。
    ///     没标注就跟随整篇字号，整篇也没定就是设置里的默认字号。
    /// </summary>
    public double ResolveSelectionFontSize()
    {
        var index = Math.Clamp(Math.Min(_selectionStart, _selectionEnd), 0, DraftContent.Length);
        var (_, size) = BoardTextFormatEditing.ResolveEffectiveStyle(_formats, index);

        return size is { } explicitSize ? BoardContentStyle.ClampFontSize(explicitSize) : DraftFontSize ?? BaseFontSize;
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

        // 落盘的内容是去空白之后的版本，标注得跟着同一次位移挪过去。
        var content = DraftContent.Trim();

        var note = new BoardNote
        {
            TypeId = SelectedOption?.Type?.Id,
            Content = content,
            ContentFontSize = DraftFontSize,
            Subject = DraftSubject?.Trim() ?? string.Empty,
            Formats = BoardTextFormatEditing.Shift(_formats, DraftContent, content)
        };

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

    partial void OnDraftContentChanged(string value)
    {
        // 文本变了：把颜色/字号标注挪到新位置，别让它们落到别的字上。
        var shifted = BoardTextFormatEditing.Shift(_formats, _lastContent, value ?? string.Empty);
        _formats.Clear();
        _formats.AddRange(shifted);
        _lastContent = value ?? string.Empty;

        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(HasPreviewContent));
        RefreshPreview();
    }

    partial void OnSelectedNoteSizeOptionChanged(BoardFontSizeOption? value)
    {
        DraftFontSize = value?.Size;
    }

    partial void OnDraftFontSizeChanged(double? value)
    {
        RefreshPreview();
    }

    private (int Start, int Length) ResolveTargetSpan()
    {
        return BoardTextFormatEditing.ResolveTargetSpan(DraftContent, _selectionStart, _selectionEnd);
    }

    /// <summary>按当前原文、标注、整篇字号与默认色重算预览。</summary>
    private void RefreshPreview()
    {
        var baseFontSize = DraftFontSize is { } size ? BoardContentStyle.ClampFontSize(size) : BaseFontSize;
        Preview = new BoardRichText(DraftContent, [.. _formats], baseFontSize, BaseColor);
    }

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
