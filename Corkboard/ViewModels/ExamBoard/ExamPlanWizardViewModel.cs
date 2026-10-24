using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Board;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.ExamBoard;

/// <summary>快速布置这次是「新建一块看板」还是「往当前看板追加」。</summary>
public enum ExamPlanWizardMode
{
    /// <summary>新建一块看板，建完直接进编辑窗口。</summary>
    CreateBoard = 0,

    /// <summary>往当前看板批量追加，已有的考试不动。</summary>
    AppendToBoard = 1
}

/// <summary>
///     「快速布置考试」向导：基本信息 → 考哪些科目 → 作息 → 对照表，四步排完一整场考试。
///     <para>
///         为什么做成向导而不是一堆输入框：用户要的是「非常方便地填写、设置」。真赶时间的时候
///         一路「下一步」就能拿到一张可用的表——标题有默认值、科目勾上就带出时长、作息有预设、
///         第一天默认今天；想细调才停下来改。
///     </para>
///     <para>
///         排布本身**不在这里算**：全部交给 Core 的 <see cref="ExamSchedulePlanner" />（纯函数、有单测）。
///         本类只负责把界面上的草稿收成「科目序列 + 作息 + 第一天」，以及把结果摊成对照表的行。
///         三处（向导、模板、单测）共用同一条排布路径，才不会算出三个不同的结果。
///     </para>
///     <para>
///         对照表里改时刻 / 时长之后**不会重新从头排**（那会把用户刚调好的东西冲掉）：
///         只把改动写回那几场，再让 <see cref="ExamSchedulePlanner.Refresh" /> 重排「第几场」与冲突。
///         回到上一步改了科目或作息，才整张重排。
///     </para>
/// </summary>
public partial class ExamPlanWizardViewModel : ViewModelBase
{
    private const int LastStep = 2;

    private readonly IExamService _examService;
    private readonly IBoardService _boardService;
    private readonly IExamTemplateService _templateService;
    private readonly IExamSlotPresetService _slotPresetService;

    /// <summary>正在把界面改动往数据上同步：期间不再触发重排，否则会自己咬自己。</summary>
    private bool _isSyncing;

    /// <summary>已经挂上作息模板服务的订阅（<see cref="Attach" /> / <see cref="Detach" /> 防重复）。</summary>
    private bool _isAttached;

    /// <summary>
    ///     用户亲手动过作息（改过某一行、或者自己挑过预设）：之后就不再按科目数自动推荐，
    ///     否则「我明明调好了，回去改个科目又被冲掉」。
    /// </summary>
    private bool _slotsTouchedByUser;

    /// <summary>对照表正在显示的那一批场次（与 <see cref="ReviewRows" /> 一一对应）。</summary>
    private List<ExamPlanEntry> _entries = [];

    /// <summary>本次向导建出来 / 追加到的看板 Id（完成之后窗口据此打开编辑窗口）。</summary>
    private Guid? _resultBoardId;

    /// <summary>
    ///     这次是按模板排的：非空时第 2、3 步跳过，直接由模板算出对照表。
    ///     <para>
    ///         模板里存的是「第几天几点考什么、考多久」（见 <see cref="ExamPlanTemplateEntry" />），
    ///         所以套用时只需要一个「这次的第一天」，其它全现算——模板因此可以一直用下去。
    ///     </para>
    /// </summary>
    private ExamPlanTemplate? _template;

    public ExamPlanWizardViewModel(
        MainConfigHandler configHandler,
        IExamService examService,
        IBoardService boardService,
        IExamTemplateService templateService,
        IExamSlotPresetService slotPresetService)
        : base(configHandler)
    {
        _examService = examService;
        _boardService = boardService;
        _templateService = templateService;
        _slotPresetService = slotPresetService;

        BoardSubjects = [];
        PlanSubjects = [];
        ReviewRows = [];
        TemplateOptions = [];
        SlotPresets = [];

        ReloadBoardSubjects();
        ReloadTemplates();
        ReloadSlotPresets();

        // 先摆一份「一天两场、08:00 开考、歇 20 分钟」兜底：用户可能压根不选科目就直接往下点，
        // 总览页也得有三张能看的数。真正的推荐值在进入总览时按科目数再算一次
        // （见 ApplyRecommendedSlots）——科目多的考试不该被排成一天一场。
        var fallback = SlotPresets.FirstOrDefault(option => option.Id == DefaultPresetId)
                       ?? SlotPresets.FirstOrDefault();

        if (fallback is not null)
        {
            ApplyShape(fallback.Create());

            // 这里只是「初始值」，不算用户挑过（否则进总览就不会按科目数推荐了）。
            _isSyncing = true;
            try
            {
                SelectedSlotPreset = fallback;
            }
            finally
            {
                _isSyncing = false;
            }
        }

        // 标题默认给一个能用的名字：真赶时间的人不该被「必填」卡住。
        FirstDayDraft = DateTime.Today;
        ApplyModeDrafts();
    }

    /// <summary>构造期兜底用的那份作息预设（「一天两场」）：最常见，也最不容易排出撞车。</summary>
    private static Guid DefaultPresetId => ExamSlotPreset.BuiltIn[1].Id;

    /// <summary>
    ///     这次是新建看板还是往当前看板追加。
    ///     <para>
    ///         它由入口决定（设置页的「快速布置」是新建、编辑窗口里的是追加），
    ///         所以不放在构造函数上——VM 是 transient、由 DI 造出来，入口只拿得到已经造好的实例。
    ///     </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreateMode))]
    [NotifyPropertyChangedFor(nameof(ModeHint))]
    private ExamPlanWizardMode _mode = ExamPlanWizardMode.CreateBoard;

    /// <summary>给 <see cref="App.ShowExamPlanWizardWindow" /> 用的入口：显示之前把模式定下来。</summary>
    public ExamPlanWizardMode ModeOverride
    {
        get => Mode;
        set => Mode = value;
    }

    partial void OnModeChanged(ExamPlanWizardMode value) => ApplyModeDrafts();

    /// <summary>
    ///     按当前模式把标题 / 副标题草稿摆好。
    ///     <para>
    ///         追加模式下这两个框是只读的（axaml 里 <c>IsEnabled="{Binding IsCreateMode}"</c>），
    ///         作用只是让用户看清「这一批要加到哪块看板上去」——所以**跟随当前看板**，
    ///         而不是给一个跟看板对不上的默认名。
    ///     </para>
    ///     <para>
    ///         注意这里只摆草稿，<see cref="TryFinish" /> 的追加分支**不会**把草稿写回看板。
    ///     </para>
    /// </summary>
    private void ApplyModeDrafts()
    {
        if (IsCreateMode)
        {
            if (string.IsNullOrWhiteSpace(TitleDraft))
                TitleDraft = string.Format(CR.Settings_Exam_Plan_TitleDefault, DateTime.Today.Year);
            return;
        }

        var board = _examService.ActiveBoard;
        var title = board?.Title?.Trim();
        TitleDraft = string.IsNullOrEmpty(title)
            ? string.Format(CR.Settings_Exam_Plan_TitleDefault, DateTime.Today.Year)
            : title;

        var subtitle = board?.Subtitle?.Trim();
        SubtitleDraft = string.IsNullOrEmpty(subtitle) ? CR.Settings_Exam_Plan_SubtitleDefault : subtitle;
    }

    /// <summary>追加模式：标题 / 副标题改的是**已经存在的**看板，不该在这一步再问一遍。</summary>
    public bool IsCreateMode => Mode == ExamPlanWizardMode.CreateBoard;

    /// <summary>窗口标题。</summary>
    public string WindowTitle => CR.Settings_Exam_Plan_WindowTitle;

    /// <summary>这一趟会怎么落地，写清楚免得用户点「完成」时心里没底。</summary>
    public string ModeHint => IsCreateMode ? CR.Settings_Exam_Plan_CreateHint : CR.Settings_Exam_Plan_AppendHint;

    /// <summary>
    ///     看板上已有的考试（追加模式用来判「跟已有考试撞了」）。
    ///     每次要用时现取：窗口开着的时候用户可能正在编辑窗口里改表。
    /// </summary>
    private IReadOnlyList<ExamEntry> ExistingExams =>
        IsCreateMode || ReplaceExisting ? [] : [.. _examService.Exams.Where(exam => exam is not null)];

    /// <summary>
    ///     窗口打开时挂上订阅。
    ///     <para>
    ///         VM 是 transient、<see cref="IExamSlotPresetService" /> 是单例：挂了就必须在
    ///         <see cref="Detach" /> 里退订，否则每开一次向导就往服务上多留一个处理器（AGENTS.md 的硬规矩）。
    ///     </para>
    /// </summary>
    public void Attach()
    {
        if (_isAttached)
            return;

        _isAttached = true;
        _slotPresetService.Changed += OnSlotPresetsChanged;

        // 窗口开着的时候别的地方可能改过看板 / 作息模板，进来先对齐一次。
        ReloadSlotPresets();
        ApplyModeDrafts();
    }

    /// <summary>窗口关闭时断开。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        _slotPresetService.Changed -= OnSlotPresetsChanged;
    }

    private void OnSlotPresetsChanged(object? sender, EventArgs e) => ReloadSlotPresets();

    #region 步骤

    /// <summary>当前第几步（0 = 基本信息，2 = 总览）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBasicStep))]
    [NotifyPropertyChangedFor(nameof(IsSubjectsStep))]
    [NotifyPropertyChangedFor(nameof(IsReviewStep))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(StepText))]
    [NotifyPropertyChangedFor(nameof(NextText))]
    [NotifyPropertyChangedFor(nameof(StepError))]
    [NotifyPropertyChangedFor(nameof(HasStepError))]
    private int _stepIndex;

    public bool IsBasicStep => StepIndex == 0;

    public bool IsSubjectsStep => StepIndex == 1;

    public bool IsReviewStep => StepIndex == 2;

    public bool CanGoPrevious => StepIndex > 0;

    public bool IsLastStep => StepIndex >= LastStep;

    /// <summary>
    ///     这次是按模板排的：科目与作息两步跳过（模板里已经有了）。
    ///     <para>
    ///         界面上那两颗步骤胶囊跟着藏起来，免得用户点不进去还以为坏了。
    ///     </para>
    /// </summary>
    public bool IsTemplateMode => _template is not null;

    /// <summary>没套模板时走三步，套了模板就只有「基本信息 → 总览」两步。</summary>
    public bool IsSubjectsStepVisible => !IsTemplateMode;

    /// <summary>步骤条上的「第 2 步 / 共 3 步 · 考哪些科目」。</summary>
    public string StepText => string.Format(
        CR.Settings_Exam_Plan_StepFormat,
        IsTemplateMode ? (StepIndex == 0 ? 1 : 2) : StepIndex + 1,
        IsTemplateMode ? 2 : LastStep + 1,
        StepName(StepIndex));

    /// <summary>右下角那颗主按钮的文字：最后一步是「完成」，其它是「下一步」。</summary>
    public string NextText => IsLastStep ? CR.Settings_Exam_Plan_Finish : CR.Settings_Exam_Plan_Next;

    /// <summary>这一步没走通的提示（红字，不弹对话框）。</summary>
    public string? StepError => StepIndex switch
    {
        1 when PlanSubjects.Count == 0 => CR.Settings_Exam_Plan_NeedSubject,
        _ => null
    };

    public bool HasStepError => !string.IsNullOrEmpty(StepError);

    private static string StepName(int step) => step switch
    {
        1 => CR.Settings_Exam_Plan_StepSubjects,
        2 => CR.Settings_Exam_Plan_StepReview,
        _ => CR.Settings_Exam_Plan_StepBasic
    };

    /// <summary>「下一步」：校验通过才往下走；到最后一步就完成。</summary>
    [RelayCommand]
    private void Next()
    {
        if (HasStepError)
        {
            OnPropertyChanged(nameof(HasStepError));
            return;
        }

        if (IsLastStep)
            return;

        // 套了模板就没有「选科目」这一步——模板里已经定死了，直接进总览。
        var next = IsTemplateMode && StepIndex == 0 ? LastStep : StepIndex + 1;

        // 第一次进总览：按已选科目数摆一份合适的作息。科目多的考试（6 门、9 门）
        // 排成一天一场会拖成一个多星期，默认值就该跟着科目数走，而不是写死「一天两场」。
        if (next == LastStep && !_slotsTouchedByUser)
            ApplyRecommendedSlots();

        StepIndex = next;

        // 走到总览时才重排一次：前两步改动只记草稿，没必要每勾一个科目就排一遍。
        if (IsReviewStep)
            RebuildPlan();
    }

    /// <summary>「上一步」。</summary>
    [RelayCommand]
    private void Previous()
    {
        if (!CanGoPrevious)
            return;

        StepIndex = IsTemplateMode && StepIndex == LastStep ? 0 : StepIndex - 1;
    }

    #endregion

    #region 第 1 步：基本信息

    /// <summary>看板标题草稿。</summary>
    [ObservableProperty]
    private string _titleDraft = string.Empty;

    /// <summary>副标题草稿。</summary>
    [ObservableProperty]
    private string _subtitleDraft = string.Empty;

    /// <summary>第一天草稿（<see cref="Avalonia.Controls.CalendarDatePicker" /> 的 <c>SelectedDate</c> 是 <see cref="DateTime" />）。</summary>
    [ObservableProperty]
    private DateTime? _firstDayDraft = DateTime.Today;

    private Color? _subtitleColorDraft;

    /// <summary>副标题取色器的草稿。</summary>
    public Color SubtitleColorDraft
    {
        get => _subtitleColorDraft ?? ResolveThemeSecondaryColor();
        set
        {
            if (_subtitleColorDraft == value)
                return;

            _subtitleColorDraft = value;
            OnPropertyChanged();
        }
    }

    /// <summary>副标题颜色是否跟随主题。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleColorDraft))]
    private bool _followSubtitleThemeColor = true;

    partial void OnFirstDayDraftChanged(DateTime? value)
    {
        // 第一天变了，整张表都要往后挪——对照表已经排好时立刻重排。
        if (IsReviewStep)
            RebuildPlan();
    }

    #endregion

    #region 模板

    /// <summary>可选模板（「用模板快速布置」那条路）。</summary>
    public ObservableCollection<TemplateOption> TemplateOptions { get; }

    /// <summary>当前选中的模板；选了就跳过科目与作息两步。</summary>
    [ObservableProperty]
    private TemplateOption? _selectedTemplate;

    /// <summary>一份模板都没有：界面上说清楚先去哪儿存一份。</summary>
    public bool HasNoTemplate => TemplateOptions.Count == 0;

    partial void OnSelectedTemplateChanged(TemplateOption? value)
    {
        _template = value?.Template;

        if (_template is not null)
        {
            // 模板自带标题 / 副标题：新建看板时预填，用户还能改。
            if (IsCreateMode)
            {
                TitleDraft = string.IsNullOrWhiteSpace(_template.Title)
                    ? string.Format(CR.Settings_Exam_Plan_TitleDefault, DateTime.Today.Year)
                    : _template.Title;
                SubtitleDraft = _template.Subtitle;
            }

            FollowSubtitleThemeColor = _template.SubtitleColor is null;
            if (_template.SubtitleColor is { } color)
                SubtitleColorDraft = color;
        }

        OnPropertyChanged(nameof(IsTemplateMode));
        OnPropertyChanged(nameof(IsSubjectsStepVisible));
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(StepError));
        OnPropertyChanged(nameof(HasStepError));

        // 已经在对照表那一屏：换模板当场重排。
        if (IsReviewStep)
            RebuildPlan();
    }

    /// <summary>重新读一遍模板清单（窗口打开时、另存为模板之后）。</summary>
    public void ReloadTemplates()
    {
        var selectedId = SelectedTemplate?.Template.Id;

        TemplateOptions.Clear();
        foreach (var template in _templateService.Templates)
            TemplateOptions.Add(new TemplateOption(template));

        OnPropertyChanged(nameof(HasNoTemplate));

        if (selectedId is { } id)
        {
            SelectedTemplate = TemplateOptions.FirstOrDefault(option => option.Template.Id == id)
                               ?? TemplateOptions.FirstOrDefault();
        }
    }

    #endregion

    #region 第 2 步：考哪些科目

    /// <summary>作业板里的科目（勾上就加进下面的考试顺序）。</summary>
    public ObservableCollection<BoardSubjectOption> BoardSubjects { get; }

    /// <summary>这次要考的科目（顺序就是考试顺序，可上移下移，可合卷）。</summary>
    public ObservableCollection<ExamPlanSubjectItem> PlanSubjects { get; }

    /// <summary>作业板一个科目都没有：给一句「先去加」的提示。</summary>
    public bool HasNoBoardSubject => BoardSubjects.Count == 0;

    /// <summary>科目来源提示（「科目来自作业板」）。</summary>
    public string SubjectsSourceText => CR.Settings_Exam_Plan_SubjectsSource;

    /// <summary>「已选 N 场」。</summary>
    public string SelectedCountText => string.Format(CR.Settings_Exam_Plan_SelectedCount, PlanSubjects.Count);

    /// <summary>新科目的名字草稿（在这里加的科目直接写进作业板的科目表，不另开第二套）。</summary>
    [ObservableProperty]
    private string _addSubjectDraft = string.Empty;

    /// <summary>「添加科目」：写进作业板科目表，同时勾上它。</summary>
    [RelayCommand]
    private void AddSubject()
    {
        var name = (AddSubjectDraft ?? string.Empty).Trim();
        if (name.Length == 0)
            return;

        if (!_boardService.Subjects.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _boardService.Subjects.Add(name);
            _boardService.Save();
            ReloadBoardSubjects();
        }

        var option = BoardSubjects.FirstOrDefault(
            item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

        if (option is not null)
            option.IsSelected = true;

        AddSubjectDraft = string.Empty;
    }

    /// <summary>把勾着的几场合成一场（理综 / 文综那种）。</summary>
    [RelayCommand]
    private void Combine()
    {
        var picked = PlanSubjects.Where(item => item.IsChecked).ToList();
        if (picked.Count < 2)
            return;

        // 顺序按它们在列表里的先后，合完插回最靠前那一行的位置。
        var insertAt = PlanSubjects.IndexOf(picked[0]);
        var names = picked.SelectMany(item => item.Subjects).ToList();
        var duration = picked.Sum(item => item.DurationMinutes);

        foreach (var item in picked)
            PlanSubjects.Remove(item);

        var combined = new ExamPlanSubjectItem(names, duration);
        combined.Changed += OnPlanSubjectChanged;
        PlanSubjects.Insert(Math.Min(insertAt, PlanSubjects.Count), combined);

        RefreshSubjectState();
    }

    /// <summary>上移一行（考试顺序往前挪）。</summary>
    public void MoveSubjectUp(ExamPlanSubjectItem item) => MoveSubject(item, -1);

    /// <summary>下移一行。</summary>
    public void MoveSubjectDown(ExamPlanSubjectItem item) => MoveSubject(item, 1);

    /// <summary>从考试顺序里拿掉一行（作业板科目表本身不动）。</summary>
    public void RemoveSubject(ExamPlanSubjectItem item)
    {
        if (item is null)
            return;

        item.Changed -= OnPlanSubjectChanged;
        PlanSubjects.Remove(item);
        SyncBoardSelection();
        RefreshSubjectState();
    }

    private void MoveSubject(ExamPlanSubjectItem item, int delta)
    {
        if (item is null)
            return;

        var index = PlanSubjects.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= PlanSubjects.Count)
            return;

        PlanSubjects.Move(index, target);
        RefreshSubjectState();
    }

    private void ReloadBoardSubjects()
    {
        var selected = BoardSubjects.Where(item => item.IsSelected)
            .Select(item => item.Name)
            .ToList();

        BoardSubjects.Clear();
        foreach (var name in _boardService.Subjects.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var option = new BoardSubjectOption(name, OnBoardSubjectToggled);
            BoardSubjects.Add(option);
        }

        // 重建之后把勾选状态补回来（加科目会走这里，不能把用户已经选好的清掉）。
        foreach (var option in BoardSubjects)
        {
            if (selected.Contains(option.Name, StringComparer.OrdinalIgnoreCase)
                || PlanSubjects.Any(item => item.Subjects.Contains(option.Name, StringComparer.OrdinalIgnoreCase)))
            {
                option.SetSelectedSilently(true);
            }
        }

        OnPropertyChanged(nameof(HasNoBoardSubject));
    }

    private void OnBoardSubjectToggled(BoardSubjectOption option)
    {
        if (_isSyncing || option is null)
            return;

        if (option.IsSelected)
        {
            if (!PlanSubjects.Any(item => item.Subjects.Contains(option.Name, StringComparer.OrdinalIgnoreCase)))
            {
                var item = new ExamPlanSubjectItem([option.Name], ExamDurationPresets.Resolve(option.Name));
                item.Changed += OnPlanSubjectChanged;
                PlanSubjects.Add(item);
            }
        }
        else
        {
            foreach (var item in PlanSubjects.ToList())
            {
                if (!item.Subjects.Contains(option.Name, StringComparer.OrdinalIgnoreCase))
                    continue;

                if (item.IsCombined)
                {
                    // 合卷里只摘掉这一门，剩下的还在——不能整行删掉。
                    var remaining = item.Subjects
                        .Where(name => !string.Equals(name, option.Name, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var at = PlanSubjects.IndexOf(item);
                    item.Changed -= OnPlanSubjectChanged;
                    PlanSubjects.RemoveAt(at);

                    if (remaining.Count > 0)
                    {
                        var replacement = new ExamPlanSubjectItem(
                            remaining,
                            item.DurationMinutes - ExamDurationPresets.Resolve(option.Name));
                        replacement.Changed += OnPlanSubjectChanged;
                        PlanSubjects.Insert(Math.Min(at, PlanSubjects.Count), replacement);
                    }
                }
                else
                {
                    item.Changed -= OnPlanSubjectChanged;
                    PlanSubjects.Remove(item);
                }

                break;
            }
        }

        RefreshSubjectState();
    }

    private void OnPlanSubjectChanged(ExamPlanSubjectItem item)
    {
        if (_isSyncing)
            return;

        // 科目行里改了什么（时长、勾选）：在总览页就按新时长重排一次。
        if (IsReviewStep && item.DurationMinutes > 0)
            RebuildPlan();

        if (item.IsChecked)
        {
            // 勾选只是为了「合卷」，不参与排布，不用重排。
            OnPropertyChanged(nameof(CanCombine));
            return;
        }

        OnPropertyChanged(nameof(CanCombine));
    }

    /// <summary>勾了两场以上才能合卷。</summary>
    public bool CanCombine => PlanSubjects.Count(item => item.IsChecked) >= 2;

    private void SyncBoardSelection()
    {
        _isSyncing = true;
        try
        {
            foreach (var option in BoardSubjects)
            {
                var selected = PlanSubjects.Any(
                    item => item.Subjects.Contains(option.Name, StringComparer.OrdinalIgnoreCase));
                option.SetSelectedSilently(selected);
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void RefreshSubjectState()
    {
        SyncBoardSelection();
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(CanCombine));
        OnPropertyChanged(nameof(StepError));
        OnPropertyChanged(nameof(HasStepError));
    }

    #endregion

    #region 第 3 步：作息设置（并进了总览页，只有三个数）

    /// <summary>一天考几场。</summary>
    [ObservableProperty]
    private int _slotsPerDay = 2;

    /// <summary><c>NumericUpDown.Value</c> 是 decimal?，这里给它一个草稿（写回 <see cref="SlotsPerDay" />）。</summary>
    public decimal? SlotsPerDayDraft
    {
        get => SlotsPerDay;
        set
        {
            if (value is not { } raw)
                return;

            var count = Math.Clamp((int)raw, MinSlotsPerDay, MaxSlotsPerDay);
            if (SlotsPerDay == count)
                return;

            SlotsPerDay = count;
        }
    }

    /// <summary>第一场几点开考（<see cref="Avalonia.Controls.TimePicker" /> 的 <c>SelectedTime</c> 是 <see cref="TimeSpan" />）。</summary>
    [ObservableProperty]
    private TimeSpan? _firstStartDraft = new(8, 0, 0);

    /// <summary>每场之间歇多久（分钟）。</summary>
    [ObservableProperty]
    private decimal? _breakDraft = ExamPlanSlot.DefaultBreakMinutes;

    partial void OnSlotsPerDayChanged(int value)
    {
        if (_isSyncing)
            return;

        _slotsTouchedByUser = true;
        SyncPresetSelection();
        OnPropertyChanged(nameof(SlotsPerDayDraft));
        OnPlanShapeChanged();
    }

    partial void OnFirstStartDraftChanged(TimeSpan? value)
    {
        if (_isSyncing || value is null)
            return;

        _slotsTouchedByUser = true;
        SyncPresetSelection();
        OnPlanShapeChanged();
    }

    partial void OnBreakDraftChanged(decimal? value)
    {
        if (_isSyncing || value is null)
            return;

        _slotsTouchedByUser = true;
        SyncPresetSelection();
        OnPlanShapeChanged();
    }

    /// <summary>作息的三个数变了：在总览页就重排一次，还停在前两步就先不排。</summary>
    private void OnPlanShapeChanged()
    {
        if (IsReviewStep)
            RebuildPlan();
    }

    /// <summary>按界面上那三个数摊出一天的场次。</summary>
    private List<ExamPlanSlot> BuildSlots()
    {
        var first = FirstStartDraft is { } start
            ? TimeOnly.FromTimeSpan(start)
            : ExamTimeSlotPolicy.DefaultStart(ExamTimeSlot.Morning);

        var breakMinutes = BreakDraft is { } raw
            ? ExamPlanSlot.ClampBreak((int)raw)
            : ExamPlanSlot.DefaultBreakMinutes;

        var count = Math.Clamp(SlotsPerDay, 1, MaxSlotsPerDay);

        return [.. ExamPlanSlotPresets.ForCount(count, first, breakMinutes)];
    }

    /// <summary>一天最多排几场（再多也没法在一天里排开）。</summary>
    public const int MaxSlotsPerDay = 6;

    /// <summary>一天场次的下限。</summary>
    public const int MinSlotsPerDay = 1;

    /// <summary>作息模板下拉（内置那几份在前，用户自己存的在后）。</summary>
    public ObservableCollection<SlotPresetOption> SlotPresets { get; }

    /// <summary>当前选中的作息模板（三个数跟它对不上时置空）。</summary>
    [ObservableProperty]
    private SlotPresetOption? _selectedSlotPreset;

    partial void OnSelectedSlotPresetChanged(SlotPresetOption? value)
    {
        OnPropertyChanged(nameof(CanDeleteSelectedSlotPreset));

        if (_isSyncing || value is null)
            return;

        // 用户自己从下拉里挑了一份：从此不再按科目数自动推荐（否则他挑完改个科目又被冲掉）。
        _slotsTouchedByUser = true;
        ApplySlotPreset(value);
    }

    /// <summary>当前选中的那份作息模板能不能删（内置的不行，没选中也不行）。</summary>
    public bool CanDeleteSelectedSlotPreset => SelectedSlotPreset is { IsBuiltIn: false };

    /// <summary>把作息模板服务里的清单重新读一遍（内置 + 用户存的）。</summary>
    public void ReloadSlotPresets()
    {
        var selectedId = SelectedSlotPreset?.Id;

        SlotPresets.Clear();
        foreach (var preset in _slotPresetService.All)
            SlotPresets.Add(new SlotPresetOption(preset));

        // 按 Id 把选中项找回来；这里只换引用、**不重新套用**，
        // 否则服务一发 Changed 就会把用户正在调的那三个数冲掉。
        _isSyncing = true;
        try
        {
            SelectedSlotPreset = selectedId is { } id
                ? SlotPresets.FirstOrDefault(option => option.Id == id)
                : SelectedSlotPreset;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    /// <summary>
    ///     把当前这套作息存成模板。同名会就地覆盖（由服务保证），存完下拉里直接选中它。
    /// </summary>
    public ExamSlotPreset? SaveCurrentSlots(string? name)
    {
        var slots = BuildSlots();
        if (slots.Count == 0)
            return null;

        var preset = _slotPresetService.Save(ExamSlotPreset.FromSlots(name, slots));

        ReloadSlotPresets();
        _isSyncing = true;
        try
        {
            SelectedSlotPreset = SlotPresets.FirstOrDefault(option => option.Id == preset.Id);
        }
        finally
        {
            _isSyncing = false;
        }

        return preset;
    }

    /// <summary>删掉一份作息模板（内置的删不掉，返回 <see langword="false" />）。</summary>
    public bool DeleteSlotPreset(ExamSlotPreset? preset)
    {
        if (preset is null || preset.IsBuiltIn)
            return false;

        return _slotPresetService.Remove(preset.Id);
    }

    /// <summary>套一份作息模板：把它的「几场 / 第一场 / 休息」抄到那三个数上。</summary>
    private void ApplySlotPreset(SlotPresetOption preset)
    {
        var slots = preset.Create();
        if (slots.Count == 0)
            return;

        ApplyShape(slots);
    }

    /// <summary>
    ///     按已选科目数摆一份推荐作息：科目多就一天多排几场，别让 9 门课按一天一场排成 9 天。
    ///     只在用户还没亲手动过作息时走这里（见 <see cref="Next" />）。
    /// </summary>
    private void ApplyRecommendedSlots()
    {
        var slots = ExamPlanSlotPresets.Recommend(PlanSubjects.Count);
        if (slots.Count == 0)
            return;

        ApplyShape(slots);
    }

    /// <summary>把一份作息折成「几场 / 第一场 / 休息」三个数写回界面。</summary>
    private void ApplyShape(IReadOnlyList<ExamPlanSlot> slots)
    {
        var wasSyncing = _isSyncing;
        _isSyncing = true;
        try
        {
            SlotsPerDay = Math.Clamp(slots.Count, MinSlotsPerDay, MaxSlotsPerDay);
            FirstStartDraft = slots[0].FixedStart.ToTimeSpan();
            BreakDraft = slots.Count > 1
                ? ExamPlanSlot.ClampBreak(slots[1].BreakMinutes)
                : ExamPlanSlot.DefaultBreakMinutes;

            SelectedSlotPreset = FindMatchingPreset(slots);
        }
        finally
        {
            _isSyncing = wasSyncing;
        }

        // 上面是在同步态里赋的值，那几个 partial 回调会提前返回——界面上的数字得手动通知一遍。
        OnPropertyChanged(nameof(SlotsPerDayDraft));
        OnPropertyChanged(nameof(FirstStartDraft));
        OnPropertyChanged(nameof(BreakDraft));
    }

    /// <summary>三个数变了之后，看看还对不对得上某份作息模板。</summary>
    private void SyncPresetSelection()
    {
        var wasSyncing = _isSyncing;
        _isSyncing = true;
        try
        {
            SelectedSlotPreset = FindMatchingPreset(BuildSlots());
        }
        finally
        {
            _isSyncing = wasSyncing;
        }
    }

    /// <summary>找一份与 <paramref name="slots" /> 完全一样的预设（场数、时刻、模式、休息都要一样）。</summary>
    private SlotPresetOption? FindMatchingPreset(IReadOnlyList<ExamPlanSlot> slots)
    {
        foreach (var option in SlotPresets)
        {
            var candidate = option.Create();
            if (candidate.Count != slots.Count)
                continue;

            var same = true;
            for (var i = 0; i < candidate.Count; i++)
            {
                if (candidate[i].FixedStart != slots[i].FixedStart
                    || candidate[i].StartMode != slots[i].StartMode
                    || candidate[i].BreakMinutes != slots[i].BreakMinutes)
                {
                    same = false;
                    break;
                }
            }

            if (same)
                return option;
        }

        return null;
    }

    #endregion


    #region 第 4 步：对照表

    /// <summary>排出来的每一场（对照表一行一场）。</summary>
    public ObservableCollection<ExamPlanRowItem> ReviewRows { get; }

    /// <summary>一场都没排出来。</summary>
    public bool IsReviewEmpty => ReviewRows.Count == 0;

    /// <summary>「共 12 场 · 跨 3 天 · 06-24 至 06-26」。</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>「有 2 场时间撞了」。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    private string _conflictText = string.Empty;

    public bool HasConflicts => !string.IsNullOrEmpty(ConflictText);

    /// <summary>从草稿整张重排（改了科目 / 作息 / 第一天时走这里）。</summary>
    private void RebuildPlan()
    {
        if (_isSyncing)
            return;

        var firstDay = DateOnly.FromDateTime(FirstDayDraft ?? DateTime.Today);

        // 套了模板：科目、时长、作息全在模板里，直接把「第几天几点」摊到这次的日期上。
        // 冲突照旧要重算一遍——模板本身没错，但可能跟看板上已有的考试撞上。
        if (_template is not null)
        {
            _entries =
            [
                .. _template.CreateEntries(firstDay)
                    .Select(entry => new ExamPlanEntry
                    {
                        Date = entry.Date,
                        Start = entry.StartTime,
                        DurationMinutes = Math.Max(1, (int)entry.Duration.TotalMinutes),
                        Subject = entry.Subject,
                        SubjectKeys = [.. entry.ResolveSubjectKeys]
                    })
            ];

            ExamSchedulePlanner.Refresh(_entries, ExistingExams);
            RebuildReviewRows();
            return;
        }

        var subjects = PlanSubjects
            .Select(item => new ExamPlanSubject(item.Subjects, item.DurationMinutes))
            .ToList();

        var slots = BuildSlots();

        // 「覆盖」等于从零排一遍：原有考试反正要清掉，再按它们的位置往后接就没意义了。
        var plan = IsAppendMode && !ReplaceExisting
            ? ExamSchedulePlanner.PlanAfter(subjects, slots, firstDay, ExistingExams)
            : ExamSchedulePlanner.Plan(subjects, slots, firstDay);

        _entries = [.. plan.Entries.Select(entry => entry.Clone())];
        RebuildReviewRows();
    }

    private void RebuildReviewRows()
    {
        ReviewRows.Clear();
        foreach (var entry in _entries)
            ReviewRows.Add(new ExamPlanRowItem(entry, this));

        RefreshReviewTexts();
    }

    /// <summary>对照表里改了一处：只重算「第几场」与冲突，然后按新顺序把行挪一挪。</summary>
    internal void ApplyReviewEdit()
    {
        if (_isSyncing || _entries.Count == 0)
            return;

        _isSyncing = true;
        try
        {
            ExamSchedulePlanner.Refresh(_entries, ExistingExams);

            // Refresh 会就地按日期 + 时刻排序；行也要跟着挪，但**不重建控件**——
            // 用户可能正握着某个 TimePicker，重建会把焦点和输入法状态一起扔掉。
            for (var target = 0; target < _entries.Count; target++)
            {
                var row = ReviewRows.FirstOrDefault(
                    item => ReferenceEquals(item.Entry, _entries[target]));

                if (row is null)
                    continue;

                var current = ReviewRows.IndexOf(row);
                if (current != target && current >= 0)
                    ReviewRows.Move(current, target);
            }
        }
        finally
        {
            _isSyncing = false;
        }

        RefreshReviewTexts();
    }

    private void RefreshReviewTexts()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var row in ReviewRows)
            row.Refresh(_entries, today);

        OnPropertyChanged(nameof(IsReviewEmpty));
        OnPropertyChanged(nameof(StepError));
        OnPropertyChanged(nameof(HasStepError));

        if (_entries.Count == 0)
        {
            SummaryText = string.Empty;
            ConflictText = string.Empty;
            return;
        }

        var first = _entries.Min(entry => entry.Date);
        var last = _entries.Max(entry => entry.Date);
        var days = last.DayNumber - first.DayNumber + 1;

        SummaryText = string.Format(
            CR.Settings_Exam_Plan_Summary,
            _entries.Count,
            days,
            ExamTimeFormatter.FormatDate(first, today),
            ExamTimeFormatter.FormatDate(last, today));

        var conflicts = _entries.Count(entry => entry.HasConflict);
        ConflictText = conflicts == 0 ? string.Empty : string.Format(CR.Settings_Exam_Plan_ConflictCount, conflicts);
    }

    /// <summary>对照表里删掉某一场。</summary>
    public void RemoveRow(ExamPlanRowItem row)
    {
        if (row is null)
            return;

        var index = ExamSchedulePlanner.IndexOf(_entries, row.Entry);
        _entries.Remove(row.Entry);
        ReviewRows.Remove(row);

        // 删掉中间那一场，同一天后面的场次跟着往前顺延（第一场没了就由下一场顶上）。
        if (index >= 0)
            ExamSchedulePlanner.Cascade(_entries, Math.Max(0, index - 1));

        ApplyReviewEdit();
    }

    /// <summary>对照表里上下挪一场。</summary>
    public void MoveRow(ExamPlanRowItem row, int delta)
    {
        if (row is null)
            return;

        var index = ReviewRows.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ReviewRows.Count)
            return;

        // 挪动 = 换开考时刻：把这一场与目标那一场的时刻对调，再让 Refresh 重算冲突。
        var other = ReviewRows[target];
        (row.Entry.Start, other.Entry.Start) = (other.Entry.Start, row.Entry.Start);
        (row.Entry.Date, other.Entry.Date) = (other.Entry.Date, row.Entry.Date);

        ApplyReviewEdit();
    }

    /// <summary>
    ///     总览里改了某一场的**开考时刻**：先把它的「距上一场」记成新间隔，
    ///     再让同一天后面那些场次按各自的休息分钟数顺延。
    /// </summary>
    internal void OnRowStartChanged(ExamPlanRowItem row)
    {
        if (_isSyncing || row is null)
            return;

        var index = ExamSchedulePlanner.IndexOf(_entries, row.Entry);
        if (index >= 0)
        {
            ExamSchedulePlanner.SyncBreak(_entries, index);
            ExamSchedulePlanner.Cascade(_entries, index);
        }

        ApplyReviewEdit();
    }

    /// <summary>总览里改了某一场的**时长**：后面那些场次按各自的休息分钟数顺延。</summary>
    internal void OnRowDurationChanged(ExamPlanRowItem row)
    {
        if (_isSyncing || row is null)
            return;

        var index = ExamSchedulePlanner.IndexOf(_entries, row.Entry);
        if (index >= 0)
            ExamSchedulePlanner.Cascade(_entries, index);

        ApplyReviewEdit();
    }

    #endregion

    #region 完成

    /// <summary>
    ///     写进看板时怎么处理已有考试：<see langword="true" /> = 覆盖（清掉原有的），
    ///     <see langword="false" /> = 追加（原来的留着）。
    ///     <para>只在追加模式（从编辑窗口进来）下有意义，新建看板时天然是空的。</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WriteModeIndex))]
    private bool _replaceExisting;

    /// <summary>
    ///     覆盖时看板上原有的考试会被清掉，所以重排时不能再拿它们判「撞了」——
    ///     追加时相反，撞上已有考试要标出来。
    /// </summary>
    partial void OnReplaceExistingChanged(bool value)
    {
        if (IsReviewStep)
            RebuildPlan();
    }

    /// <summary>这次是不是往已有的看板里写（只有这种时候才需要问「覆盖还是追加」）。</summary>
    public bool IsAppendMode => Mode == ExamPlanWizardMode.AppendToBoard;

    /// <summary>写入方式下拉的选中下标（0 = 追加到已有考试后面，1 = 覆盖看板上的考试）。</summary>
    public int WriteModeIndex
    {
        get => ReplaceExisting ? 1 : 0;
        set
        {
            var replace = value > 0;
            if (ReplaceExisting == replace)
                return;

            ReplaceExisting = replace;
        }
    }

    /// <summary>
    ///     「完成」：把排好的表真正写进考试服务。返回 <see langword="true" /> 表示已经写完了，
    ///     窗口据此关掉自己并打开编辑窗口。
    /// </summary>
    public bool TryFinish()
    {
        if (_entries.Count == 0)
            return false;

        var entries = _entries.Select(entry => entry.ToEntry()).ToList();

        if (Mode == ExamPlanWizardMode.CreateBoard)
        {
            var title = (TitleDraft ?? string.Empty).Trim();
            var board = _examService.CreateBoard(title.Length == 0 ? null : title);

            var subtitle = (SubtitleDraft ?? string.Empty).Trim();
            board.Subtitle = subtitle;
            board.SubtitleColor = FollowSubtitleThemeColor ? null : _subtitleColorDraft;
            _examService.Save();

            _examService.AddMany(entries);
            _resultBoardId = board.Id;
        }
        else
        {
            if (ReplaceExisting)
                _examService.ReplaceAll(entries);
            else
                _examService.AddMany(entries);

            _resultBoardId = _examService.ActiveBoard.Id;
        }

        return true;
    }

    /// <summary>完成之后要打开哪块看板的编辑窗口。</summary>
    public Guid? ResultBoardId => _resultBoardId;

    /// <summary>完成提示（「已排好 12 场考试」）。</summary>
    public string SuccessText => string.Format(CR.Settings_Exam_Plan_Success, _entries.Count);

    #endregion

    /// <summary>主题里的「次要文字色」——副标题默认就用它（与编辑窗口同一套取法）。</summary>
    private static Color ResolveThemeSecondaryColor()
    {
        if (Application.Current is { } application)
        {
            var variant = application.ActualThemeVariant;

            if (application.TryGetResource("TextFillColorSecondaryBrush", variant, out var resource)
                && resource is ISolidColorBrush brush)
            {
                return brush.Color;
            }
        }

        return Colors.Gray;
    }
}

/// <summary>作业板科目列表里的一行（勾上就加进这次要考的科目）。</summary>
public partial class BoardSubjectOption(string name, Action<BoardSubjectOption> onToggled) : ObservableObject
{
    /// <summary>程序内部改勾选状态时置真，免得再绕回去触发一次添加 / 删除。</summary>
    private bool _isSilent;

    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_isSilent)
            onToggled(this);
    }

    /// <summary>程序内部改勾选状态（同步两个列表时不该再触发一次添加）。</summary>
    public void SetSelectedSilently(bool value)
    {
        if (IsSelected == value)
            return;

        _isSilent = true;
        try
        {
            IsSelected = value;
        }
        finally
        {
            _isSilent = false;
        }
    }
}

/// <summary>时长下拉里的一项。</summary>
public sealed class DurationOption(int minutes)
{
    public int Minutes { get; } = ExamDurationPresets.Clamp(minutes);

    public string Text => ExamDurationPresets.Describe(Minutes);

    public override string ToString() => Text;
}

/// <summary>「这次要考的科目」里的一行：一个科目，或者几门合成的合卷。</summary>
public partial class ExamPlanSubjectItem : ObservableObject
{
    public ExamPlanSubjectItem(IEnumerable<string> subjects, int durationMinutes)
    {
        Subjects = [.. subjects];
        DurationOptions = [];
        DurationMinutes = ExamDurationPresets.Clamp(durationMinutes);
        RebuildDurationOptions();
    }

    /// <summary>行里变了（时长、勾选）：向导据此重排 / 重算「能不能合卷」。</summary>
    public event Action<ExamPlanSubjectItem>? Changed;

    /// <summary>绑定的科目（合卷时多个）。</summary>
    public ObservableCollection<string> Subjects { get; }

    /// <summary>显示名：单科就是科目名，合卷是「物理 · 化学」。</summary>
    public string DisplayName => string.Join(ExamPlanSubject.CombineSeparator, Subjects);

    /// <summary>这是合卷。</summary>
    public bool IsCombined => Subjects.Count > 1;

    /// <summary>时长下拉的可选项：五个快捷档 + 当前值（合卷算出来的 225 之类也得能显示）。</summary>
    public ObservableCollection<DurationOption> DurationOptions { get; }

    /// <summary>时长（分钟）。</summary>
    [ObservableProperty]
    private int _durationMinutes;

    /// <summary>勾上表示要参与「合卷」。</summary>
    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private DurationOption? _selectedDuration;

    /// <summary>正在重建下拉项：期间不去改 <see cref="DurationMinutes" />，否则会自己咬自己。</summary>
    private bool _isRebuildingOptions;

    partial void OnSelectedDurationChanged(DurationOption? value)
    {
        if (_isRebuildingOptions || value is null || value.Minutes == DurationMinutes)
            return;

        DurationMinutes = value.Minutes;
        Changed?.Invoke(this);
    }

    partial void OnDurationMinutesChanged(int value)
    {
        if (_isRebuildingOptions || DurationOptions.Any(option => option.Minutes == value))
            return;

        RebuildDurationOptions();
    }

    partial void OnIsCheckedChanged(bool value) => Changed?.Invoke(this);

    private void RebuildDurationOptions()
    {
        var previous = SelectedDuration?.Minutes ?? DurationMinutes;

        var wanted = ExamDurationPresets.QuickMinutes
            .Append(DurationMinutes)
            .Append(previous)
            .Distinct()
            .OrderBy(minutes => minutes)
            .ToList();

        _isRebuildingOptions = true;
        try
        {
            DurationOptions.Clear();
            foreach (var minutes in wanted)
                DurationOptions.Add(new DurationOption(minutes));

            SelectedDuration = DurationOptions.FirstOrDefault(option => option.Minutes == previous)
                               ?? DurationOptions.FirstOrDefault(option => option.Minutes == DurationMinutes)
                               ?? DurationOptions[0];
        }
        finally
        {
            _isRebuildingOptions = false;
        }
    }
}

/// <summary>作息预设下拉里的一项：内置那几份与用户存的作息模板都包成它。</summary>
public sealed class SlotPresetOption
{
    public SlotPresetOption(ExamSlotPreset preset)
    {
        Preset = preset;
    }

    public SlotPresetOption(string name, Func<IReadOnlyList<ExamPlanSlot>> factory)
        : this(new ExamSlotPreset { Name = name, Slots = ExamPlanSlotPresets.Materialize(factory()) })
    {
    }

    /// <summary>背后那份作息模板。</summary>
    public ExamSlotPreset Preset { get; }

    public Guid Id => Preset.Id;

    public string Name => Preset.ResolveName();

    /// <summary>内置的那几份删不掉（界面上不给删）。</summary>
    public bool IsBuiltIn => Preset.IsBuiltIn;

    /// <summary>「一天 3 场」——下拉里那行说明。</summary>
    public string Summary => Preset.Describe();

    /// <summary>拷一份新的作息出来（预设本身不许被界面改）。</summary>
    public IReadOnlyList<ExamPlanSlot> Create() => Preset.CreateSlots();

    public override string ToString() => Name;
}

/// <summary>模板下拉里的一项。</summary>
public sealed class TemplateOption(ExamPlanTemplate template)
{
    /// <summary>背后那份模板（选中它就按它排）。</summary>
    public ExamPlanTemplate Template { get; } = template;

    /// <summary>「期末考试 · 共 12 场 · 跨 3 天」。</summary>
    public string Text => $"{Template.ResolveName()} · {Template.Describe()}";

    public override string ToString() => Text;
}

/// <summary>对照表里的一行：日期 | 时段 | 科目 | 时间 | 时长 | 距上一场。</summary>
public partial class ExamPlanRowItem(ExamPlanEntry entry, ExamPlanWizardViewModel owner) : ObservableObject
{
    /// <summary>这一行背后的那场考试（改动直接写回它）。</summary>
    public ExamPlanEntry Entry { get; } = entry;

    [ObservableProperty]
    private string _indexText = string.Empty;

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private string _slotText = string.Empty;

    [ObservableProperty]
    private string _subjectText = string.Empty;

    [ObservableProperty]
    private string _timeText = string.Empty;

    [ObservableProperty]
    private string _durationText = string.Empty;

    [ObservableProperty]
    private string _breakText = string.Empty;

    [ObservableProperty]
    private bool _hasBreak;

    [ObservableProperty]
    private bool _hasConflict;

    [ObservableProperty]
    private string _conflictText = string.Empty;

    /// <summary>开考时刻草稿（就地改）。</summary>
    [ObservableProperty]
    private TimeSpan? _startDraft;

    /// <summary>时长草稿（就地改）。</summary>
    [ObservableProperty]
    private decimal? _durationDraft;

    private bool _isRefreshing;

    /// <summary>按背后的那场考试重算这一行的文案。</summary>
    public void Refresh(IReadOnlyList<ExamPlanEntry> ordered, DateOnly today)
    {
        _isRefreshing = true;
        try
        {
            IndexText = Entry.IndexInDay.ToString();
            DateText = ExamTimeFormatter.FormatDate(Entry.Date, today);
            SlotText = ExamTimeSlotPolicy.Describe(Entry.TimeSlot);
            SubjectText = Entry.Subject;
            TimeText = $"{ExamTimeFormatter.FormatTime(Entry.Start)} – {ExamTimeFormatter.FormatTime(Entry.End)}";
            DurationText = ExamTimeFormatter.FormatDuration(Entry.Duration);
            StartDraft = Entry.Start.ToTimeSpan();
            DurationDraft = Entry.DurationMinutes;

            var gap = ExamSchedulePlanner.BreakBefore(ordered, Entry);
            HasBreak = gap is not null;
            BreakText = gap is { } value ? ExamTimeFormatter.FormatDuration(value) : string.Empty;

            HasConflict = Entry.HasConflict;
            ConflictText = Entry.ConflictReason ?? string.Empty;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnStartDraftChanged(TimeSpan? value)
    {
        if (_isRefreshing || value is not { } time)
            return;

        var clamped = time < TimeSpan.Zero
            ? TimeOnly.MinValue
            : time >= TimeSpan.FromDays(1) ? new TimeOnly(23, 59) : TimeOnly.FromTimeSpan(time);

        if (Entry.Start == clamped)
            return;

        Entry.Start = clamped;
        owner.OnRowStartChanged(this);
    }

    partial void OnDurationDraftChanged(decimal? value)
    {
        if (_isRefreshing)
            return;

        var minutes = ExamDurationPresets.Clamp(value is { } raw ? (int)raw : ExamDurationPresets.FallbackMinutes);
        if (Entry.DurationMinutes == minutes)
            return;

        Entry.DurationMinutes = minutes;
        owner.OnRowDurationChanged(this);
    }
}
