using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Board;
using Corkboard.Core.Handwriting;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Notes;
using Corkboard.Core.RichText;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Services.Stats;
using Corkboard.Services.Stats;
using Corkboard.Services.Ui;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.MainPages.Homework;

/// <summary>
///     「布置作业」表单的 ViewModel，显示在壳的页面级弹层里（<see cref="PageOverlayService" />）。
///     <para>
///         同一份表单兼管**编辑已有作业**：<see cref="LoadForEdit" /> 之后按钮文案变成「保存」，
///         确定时走 <see cref="IBoardService.Update" /> 而不是 <see cref="IBoardService.Add" />。
///     </para>
///     <para>
///         内容有**两种形态**，由 <see cref="IsTextMode" /> / <see cref="IsHandwritingMode" /> 切换：
///         <list type="bullet">
///             <item>打字：富文本 HTML（<see cref="DraftHtml" />），编辑与渲染都由表单里的
///                 <c>RichTextBlock</c> 负责，落盘走 <c>Note.SetHtmlContent</c>；</item>
///             <item>手写：笔迹 JSON（<see cref="HandwritingData" />），画布在表单里，落盘走
///                 <c>Note.SetHandwriting</c>。</item>
///         </list>
///         两种草稿各留一份、互不覆盖：切回去的时候另一边还在。
///     </para>
///     <para>
///         颜色与字号是编辑器自己的行为（对选中文字生效，默认值见 <see cref="BaseFontSize" /> /
///         <see cref="BaseColor" />），这里不需要再记标注，也不需要跟着文本位移挪标注。
///     </para>
///     <para>
///         表单活得很短：每次打开都新建一个，科目清单只在构造时读一次，
///         所以不订阅 <see cref="IBoardService.Changed" />，也就没有 transient 订阅单例的回收问题。
///     </para>
/// </summary>
public partial class HomeworkAssignmentFormViewModel : ObservableObject
{
    private readonly IBoardService _boardService;
    private readonly PageOverlayService _overlay;
    private readonly BoardSettingsConfig _settings;
    private readonly PlatformUsageReportService _usageReport;

    /// <summary>正在编辑的作业 Id；为 <c>null</c> 表示这是「布置作业」。</summary>
    private Guid? _editingNoteId;

    /// <summary>
    ///     编辑中的作业原来带的「已清理」标记。表单里改不了它，但保存时要原样带回去：
    ///     <see cref="IBoardService.Update" /> 是逐字段复制的，漏了它等于把清掉的作业又放回板子。
    /// </summary>
    private DateTimeOffset? _editingCleanedAt;

    /// <summary>当前内容形态。只读地暴露成两个 bool（见 <see cref="IsTextMode" />）。</summary>
    private HomeworkComposeMode _mode = HomeworkComposeMode.Text;

    public HomeworkAssignmentFormViewModel(
        IBoardService boardService,
        PageOverlayService overlay,
        MainConfigHandler configHandler,
        PlatformUsageReportService usageReport)
    {
        _boardService = boardService;
        _overlay = overlay;
        _settings = configHandler.Data.BoardSettings;
        _usageReport = usageReport;

        BaseFontSize = RichTextStyle.ClampFontSize(_settings.DefaultContentFontSize);
        BaseColor = _settings.DefaultContentColor;

        SubjectOptions = [.. _boardService.Subjects];

        // 手写工具栏的初始状态取自设置：笔色（没设就跟随主题前景色）、笔粗、橡皮粗细。
        // 先排好常用色圆点再定笔色，圆点的高亮状态才会跟着刷（见 OnPenColorChanged）。
        foreach (var color in _settings.ResolveHandwritingColors())
            HandwritingColorOptions.Add(new HandwritingColorOption(color, this));

        PenColor = _settings.HandwritingPenColor ?? ResolveThemeForegroundColor();
        PenWidth = HandwritingStyle.ClampPenWidth(_settings.HandwritingPenWidth);
        EraserWidth = HandwritingStyle.ClampEraserWidth(_settings.HandwritingEraserWidth);

        LoadQuickTags();

        // 分类下拉跟着科目走：新建时还没选科目，先只摆一项「不分类」。
        RefreshSectionOptions(DraftSubject);

        // 「布置作业」默认明天截止：今天布置、明天要交是常态，省得每次都去点日历。
        // 编辑已有作业时 LoadForEdit 会把它换成那条作业自己的截止日期（原来没设的就是空）。
        DueDateDraft = DateTime.Today.AddDays(1);
    }

    /// <summary>
    ///     科目下拉的选项。默认就是科目清单；编辑一条科目已被设置页删掉的旧作业时，
    ///     会把那一条补进来，免得下拉框空着、一保存就把科目洗没了。
    /// </summary>
    public ObservableCollection<string> SubjectOptions { get; } = [];

    /// <summary>
    ///     二级分类下拉的选项：第一项恒为「不分类」（<see cref="HomeworkSectionOption.Value" /> 为 <c>null</c>），
    ///     后面跟当前科目的分类（<see cref="IBoardService.GetSections" />）。
    /// </summary>
    public ObservableCollection<HomeworkSectionOption> SectionOptions { get; } = [];

    /// <summary>手写工具栏上的常用色圆点（设置 → 作业板 → 手写 → 常用颜色）。</summary>
    public ObservableCollection<HandwritingColorOption> HandwritingColorOptions { get; } = [];

    /// <summary>快捷标签 chips（设置 → 作业板 → 快捷标签，最多 8 个）。选中的会存进 <c>Note.Tags</c>。</summary>
    public ObservableCollection<HomeworkQuickTagOption> QuickTagOptions { get; } = [];

    /// <summary>当前勾中的标签文字（保存时写进 <c>Note.Tags</c>，见 <see cref="Confirm" />）。</summary>
    public IReadOnlyList<string> SelectedTags =>
        QuickTagOptions.Where(option => option.IsSelected).Select(option => option.Text).ToList();

    /// <summary>设置里配的默认字号：内容没单独设过字号时用的就是它。</summary>
    public double BaseFontSize { get; }

    /// <summary>设置里配的默认颜色；为空表示跟随主题。</summary>
    public Color? BaseColor { get; }

    /// <summary>笔粗滑杆的下限。夹取规则与设置页 / 画布共用 <see cref="HandwritingStyle" />，别在 XAML 里另写一套。</summary>
    public double PenWidthMinimum => HandwritingStyle.MinPenWidth;

    /// <summary>笔粗滑杆的上限（1–24）。</summary>
    public double PenWidthMaximum => HandwritingStyle.MaxPenWidth;

    /// <summary>橡皮粗细滑杆的下限（8–64）。</summary>
    public double EraserWidthMinimum => HandwritingStyle.MinEraserWidth;

    /// <summary>橡皮粗细滑杆的上限。</summary>
    public double EraserWidthMaximum => HandwritingStyle.MaxEraserWidth;

    /// <summary>内容的 HTML 片段，编辑器的双向绑定源（回写时已经是干净版本）。</summary>
    [ObservableProperty] private string _draftHtml = string.Empty;

    [ObservableProperty] private string? _draftSubject;

    /// <summary>分类下拉里选中的那一项；<see cref="DraftSection" /> 是它落到 <c>Note.Section</c> 的值。</summary>
    [ObservableProperty] private HomeworkSectionOption? _selectedSectionOption;

    /// <summary>「新建分类」的输入行是不是展开着。</summary>
    [ObservableProperty] private bool _isAddingSection;

    /// <summary>「新建分类」输入行里的草稿名。</summary>
    [ObservableProperty] private string _newSectionName = string.Empty;

    /// <summary>
    ///     新建分类的输入有没有问题（空名 / 没选科目 / 写盘失败）。
    ///     只是把输入框标红，不弹新东西——这是轻量操作，不值得开对话框。
    /// </summary>
    [ObservableProperty] private bool _hasSectionInputError;

    /// <summary>当前笔色。设置里没指定时是主题前景色（见构造函数）。</summary>
    [ObservableProperty] private Color _penColor;

    /// <summary>当前笔粗。</summary>
    [ObservableProperty] private double _penWidth;

    /// <summary>当前橡皮粗细。</summary>
    [ObservableProperty] private double _eraserWidth;

    /// <summary>手写模式现在用的是橡皮还是笔：橡皮时工具栏多显示一条橡皮粗细。</summary>
    [ObservableProperty] private bool _isEraserMode;

    /// <summary>手写画布的笔迹 JSON；<c>null</c> 表示还没写过。</summary>
    [ObservableProperty] private string? _handwritingData;

    /// <summary>手写画布上有没有笔迹（由画布的 <c>Changed</c> 事件同步过来，见 code-behind）。</summary>
    [ObservableProperty] private bool _hasInk;

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

    /// <summary>要写进 <c>Note.Section</c> 的分类名；<c>null</c> = 不分类。</summary>
    public string? DraftSection => SelectedSectionOption?.Value;

    /// <summary>现在是不是「打字」形态。两个 bool 供 XAML 做可见性切换。</summary>
    public bool IsTextMode => _mode == HomeworkComposeMode.Text;

    /// <summary>现在是不是「手写」形态。</summary>
    public bool IsHandwritingMode => _mode == HomeworkComposeMode.Handwriting;

    /// <summary>
    ///     「笔 / 橡皮」两颗 ToggleButton 的选中态。没用 <c>RadioButton</c>，因为两颗 ToggleButton 得互相排斥：
    /// 点亮的那个被再点一下会把值改回 <c>false</c>，所以切换走命令、<c>IsChecked</c> 只做单向绑定，
    /// 命令里无条件通知一遍把它们扶正（见 <see cref="SwitchToPen" /> / <see cref="SwitchToEraser" />）。
    /// </summary>
    public bool IsPenMode => !IsEraserMode;

    /// <summary>
    ///     快捷标签整行只在设置里配过标签时出现。手写作业一样能贴标签：
    ///     标签存在 <c>Note.Tags</c> 里，跟内容形态无关，卡片上都画成胶囊。
    /// </summary>
    public bool ShowQuickTags => QuickTagOptions.Count > 0;

    /// <summary>手写模式下画布还空着时，在画布中央盖一句「先写点什么再布置」。</summary>
    public bool ShowHandwritingHint => IsHandwritingMode && !HasInk;

    /// <summary>没选科目就没法建分类（分类是按科目存的）。</summary>
    public bool CanAddSection => !string.IsNullOrWhiteSpace(DraftSubject);

    /// <summary>是否在编辑已有作业。为真时表单显示「保存」，标题也换一个。</summary>
    public bool IsEditing => _editingNoteId is not null;

    public string FormTitle => IsEditing ? CR.Board_EditTitle : CR.Board_AddNote;

    /// <summary>确定按钮的文案：新建是「布置」，编辑是「保存」。</summary>
    public string ConfirmText => IsEditing ? CR.Board_SaveEdit : CR.Board_ConfirmAdd;

    /// <summary>
    ///     能不能布置：手写模式要至少有过一笔，打字模式要真有内容。
    ///     <para>
    ///         打字这边不能只看 <c>Trim().Length &gt; 0</c>：编辑器回写的是 HTML，一个空段落也有标签，
    ///         必须让 <see cref="RichTextHtmlStyling.HasMeaningfulContent" /> 去数「除了标签、空白与种子之外」的内容。
    ///     </para>
    /// </summary>
    public bool CanConfirm => IsHandwritingMode
        ? HasInk
        : RichTextHtmlStyling.HasMeaningfulContent(DraftHtml);

    /// <summary>把这条作业的内容填进表单，之后确定就变成「保存」。</summary>
    public void LoadForEdit(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        _editingNoteId = note.Id;
        _editingCleanedAt = note.CleanedAt;

        var subject = note.Subject.Trim();
        if (subject.Length > 0 && !SubjectOptions.Contains(subject, StringComparer.Ordinal))
            SubjectOptions.Add(subject);

        DraftSubject = subject.Length > 0 ? subject : null;

        // 科目定下来之后重拉一遍分类，再把这条作业自己的分类选回去：
        // 分类定义可能已经被设置页删掉，那条也要能显示出来（见 RefreshSectionOptions）。
        RefreshSectionOptions(DraftSubject, note.Section);

        // 按内容格式自动切到对应形态，并把那一份草稿装进表单。
        // 另一份草稿（比如手写作业的 DraftHtml）保持为空：用户要切成另一种形态就是从白纸开始写。
        // 形态判定不能只看 ContentKind：历史数据里有「笔迹在、kind 却被洗成 Html」的作业
        // （旧版本编辑时按打字态保存过），那种作业一点「编辑」会停在打字页签，
        // 用户再保存一次就把整段笔迹抹了。所以只要笔迹非空，就按手写打开。
        var hasHandwriting = !string.IsNullOrWhiteSpace(note.Handwriting);
        HandwritingData = note.Handwriting;

        if (note.ContentKind == RichTextContentKind.Handwriting
            || (hasHandwriting && string.IsNullOrWhiteSpace(note.Content)))
        {
            HasInk = hasHandwriting;
            SetMode(HomeworkComposeMode.Handwriting);
        }
        else
        {
            DraftHtml = note.Content ?? string.Empty;
            SetMode(HomeworkComposeMode.Text);
        }

        // 载入完再通知一遍：表单是先把 DataContext 挂上、再调 LoadForEdit 的，
        // 这一步保证两颗页签按最终形态扶正（不依赖绑定求值的先后）。
        NotifyModeProperties();

        // 截止日期：没设过的作业留空，选择器就是空的。
        DueDateDraft = note.DueDate is { } due ? due.ToDateTime(TimeOnly.MinValue) : null;

        // 快捷标签：把这条作业记着的几颗点亮。设置里后来删掉的标签也补一颗选中项进来，
        // 否则一打开表单、一保存，作业上那颗标签就被悄悄洗掉了。
        if (note.Tags is { Count: > 0 } tags)
        {
            foreach (var tag in tags)
            {
                var option = QuickTagOptions.FirstOrDefault(
                    existing => string.Equals(existing.Text, tag, StringComparison.OrdinalIgnoreCase));

                if (option is null)
                {
                    option = new HomeworkQuickTagOption(tag, this);
                    QuickTagOptions.Add(option);
                }

                option.IsSelected = true;
            }

            OnPropertyChanged(nameof(ShowQuickTags));
        }

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(ConfirmText));
        OnPropertyChanged(nameof(CanConfirm));
    }

    /// <summary>切到「打字」。</summary>
    [RelayCommand]
    private void SwitchToText() => SetMode(HomeworkComposeMode.Text);

    /// <summary>切到「手写」。</summary>
    [RelayCommand]
    private void SwitchToHandwriting() => SetMode(HomeworkComposeMode.Handwriting);

    /// <summary>手写工具栏切回画笔。点到已经点亮的那颗也会重新通知，让 ToggleButton 自己保持选中。</summary>
    [RelayCommand]
    private void SwitchToPen() => SetEraserMode(false);

    /// <summary>手写工具栏切到橡皮（这时才显示橡皮粗细）。</summary>
    [RelayCommand]
    private void SwitchToEraser() => SetEraserMode(true);

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

        var note = new Note
        {
            Subject = DraftSubject?.Trim() ?? string.Empty,
            Section = DraftSection,
            DueDate = DraftDueDate,
            // 快捷标签是结构化字段，不是正文里的字：卡片上按它画胶囊。
            Tags = Note.NormalizeTags(SelectedTags)
        };

        if (IsHandwritingMode)
        {
            // 手写：内容在 Handwriting（归一化坐标的笔迹 JSON）里，Content 留空、ContentKind 标成 Handwriting。
            note.SetHandwriting(HandwritingData);
        }
        else if (string.IsNullOrWhiteSpace(DraftHtml) && !string.IsNullOrWhiteSpace(HandwritingData))
        {
            // 兜底：文本页签里正文是空的、但这条作业带着笔迹——按手写保存，别把笔迹抹掉。
            note.SetHandwriting(HandwritingData);
        }
        else
        {
            // 内容是富文本片段：一次写好 Content 与 ContentKind，顺手清掉旧的标注字段。
            note.SetHtmlContent(DraftHtml.Trim());
        }

        if (_editingNoteId is { } id)
        {
            // 编辑：拿 Id 找回原作业覆盖，创建时间与归档位置都不动，只刷新修改时间。
            // 清理标记不在表单里编辑，但 Update 会按这份副本整条覆盖，所以必须原样带回去。
            note.Id = id;
            note.CleanedAt = _editingCleanedAt;
            if (_boardService.Update(note))
            {
                _overlay.Close();
            }

            return;
        }

        _boardService.Add(note);
        // 使用量统计：只有「新增」算 note_add，编辑已有作业不算。
        _usageReport.Record(UsageMetricNames.NoteAdd);
        _overlay.Close();
    }

    /// <summary>切换内容形态：只改模式，两份草稿都留着（切回去还在）。</summary>
    private void SetMode(HomeworkComposeMode mode)
    {
        // 点已经选中的那颗 ToggleButton 时它会把值改回 false，这里无条件通知一遍把它扶正。
        _mode = mode;
        NotifyModeProperties();
    }

    /// <summary>切换画笔 / 橡皮。同样无条件通知，理由见 <see cref="SetMode" />。</summary>
    private void SetEraserMode(bool eraser)
    {
        IsEraserMode = eraser;

        // IsEraserMode 没变时 setter 自己不发通知，但按钮这回已经被自己点反了：
        // 两颗都要显式再通知一遍，让 IsChecked 的单向绑定把它们扶正。
        OnPropertyChanged(nameof(IsEraserMode));
        OnPropertyChanged(nameof(IsPenMode));
    }

    private void NotifyModeProperties()
    {
        OnPropertyChanged(nameof(IsTextMode));
        OnPropertyChanged(nameof(IsHandwritingMode));
        OnPropertyChanged(nameof(ShowQuickTags));
        OnPropertyChanged(nameof(ShowHandwritingHint));
        OnPropertyChanged(nameof(CanConfirm));
    }

    /// <summary>
    ///     按科目重建二级分类下拉。
    ///     <para>
    ///         第一项恒为「不分类」；<paramref name="keep" /> 是希望选中的分类名（编辑回填时用），
    ///         为空则选「不分类」。作业记着、但科目定义里已经没有的分类也会补一项：
    ///         否则一编辑、一下拉、一保存，那条作业的分类就被洗成「不分类」了。
    ///     </para>
    /// </summary>
    private void RefreshSectionOptions(string? subject, string? keep = null)
    {
        var previous = keep ?? DraftSection;

        SectionOptions.Clear();
        SectionOptions.Add(new HomeworkSectionOption(null));

        var normalizedSubject = subject?.Trim();
        if (!string.IsNullOrEmpty(normalizedSubject))
        {
            foreach (var section in _boardService.GetSections(normalizedSubject))
            {
                var name = section.Name.Trim();
                if (name.Length == 0 || ContainsSection(name))
                    continue;

                SectionOptions.Add(new HomeworkSectionOption(name));
            }
        }

        if (!string.IsNullOrEmpty(previous) && !ContainsSection(previous))
            SectionOptions.Add(new HomeworkSectionOption(previous));

        SelectedSectionOption = SectionOptions
            .FirstOrDefault(option => string.Equals(option.Value, previous, StringComparison.Ordinal))
            ?? SectionOptions[0];
    }

    private bool ContainsSection(string name) =>
        SectionOptions.Any(option => string.Equals(option.Value, name, StringComparison.Ordinal));

    /// <summary>展开「新建分类」输入行。</summary>
    [RelayCommand]
    private void BeginAddSection()
    {
        NewSectionName = string.Empty;
        HasSectionInputError = false;
        IsAddingSection = true;
    }

    /// <summary>收起「新建分类」输入行，什么也不做。</summary>
    [RelayCommand]
    private void CancelAddSection()
    {
        IsAddingSection = false;
        HasSectionInputError = false;
        NewSectionName = string.Empty;
    }

    /// <summary>
    ///     新建分类：<see cref="IBoardService.AddSection" /> 写盘，成功后选中它并收起输入行。
    ///     同名分类会返回已有那一项（不是失败），所以这里不用先去重。
    /// </summary>
    [RelayCommand]
    private void ConfirmAddSection()
    {
        var subject = DraftSubject?.Trim();
        var name = NewSectionName.Trim();

        if (string.IsNullOrEmpty(subject)
            || name.Length == 0
            || _boardService.AddSection(subject, name) is null)
        {
            HasSectionInputError = true;
            return;
        }

        HasSectionInputError = false;
        IsAddingSection = false;
        NewSectionName = string.Empty;
        RefreshSectionOptions(subject, name);
    }

    /// <summary>点一颗常用色圆点：换笔色（同时把圆点的高亮刷过去，见 <see cref="OnPenColorChanged" />）。</summary>
    [RelayCommand]
    private void SelectPenColor(HandwritingColorOption? option)
    {
        if (option is not null)
            PenColor = option.Color;
    }

    /// <summary>
    ///     把设置里的快捷标签摊成 chips；空则整行在 XAML 里隐藏（见 <see cref="ShowQuickTags" />）。
    ///     <para>chips 的选中态是 <c>TwoWay</c> 绑在 <see cref="HomeworkQuickTagOption.IsSelected" /> 上的，
    ///     点一下只切换自己，不再往正文里写文字。</para>
    /// </summary>
    private void LoadQuickTags()
    {
        QuickTagOptions.Clear();

        foreach (var tag in BoardSettingsConfig.NormalizeQuickTagList(_settings.QuickTags))
            QuickTagOptions.Add(new HomeworkQuickTagOption(tag, this));

        OnPropertyChanged(nameof(ShowQuickTags));
    }

    partial void OnDraftHtmlChanged(string value)
    {
        OnPropertyChanged(nameof(CanConfirm));
    }

    partial void OnDraftSubjectChanged(string? value)
    {
        // 换科目 → 分类清单跟着换；原来选的分类不在新科目里就退回「不分类」（RefreshSectionOptions 负责）。
        RefreshSectionOptions(value);

        // 没科目就没法建分类：把已经展开的输入行一并收掉。
        if (!CanAddSection && IsAddingSection)
        {
            IsAddingSection = false;
            HasSectionInputError = false;
            NewSectionName = string.Empty;
        }

        OnPropertyChanged(nameof(CanAddSection));
    }

    partial void OnSelectedSectionOptionChanged(HomeworkSectionOption? value)
    {
        OnPropertyChanged(nameof(DraftSection));
    }

    partial void OnNewSectionNameChanged(string value)
    {
        // 一改就不再标红：错误提示是对上一次输入的回应，别一直挂着。
        if (HasSectionInputError)
            HasSectionInputError = false;
    }

    partial void OnPenColorChanged(Color value)
    {
        // 笔色一变就重刷圆点高亮：点圆点、以及将来程序里换笔色都走这一条。
        foreach (var option in HandwritingColorOptions)
            option.IsSelected = option.Color == value;
    }

    partial void OnIsEraserModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPenMode));
    }

    partial void OnHasInkChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(ShowHandwritingHint));
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

    /// <summary>
    ///     设置里没指定笔色时跟随主题的文字色（浅色主题近黑、深色主题白）。
    ///     <para>
    ///         取主题资源必须显式带上主题变体：不带变体的查找走「默认」那套字典，
    ///         暗色主题下会拿到亮色字典里的近黑，写出来看不见。
    ///     </para>
    /// </summary>
    private static Color ResolveThemeForegroundColor()
    {
        if (Application.Current is { } app)
        {
            var variant = app.ActualThemeVariant;
            variant = variant == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

            if (app.TryFindResource("TextFillColorPrimaryBrush", variant, out var resource)
                && resource is ISolidColorBrush brush)
            {
                return brush.Color;
            }
        }

        return Colors.Black;
    }
}
