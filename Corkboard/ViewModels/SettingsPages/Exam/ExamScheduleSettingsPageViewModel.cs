using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Exam;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Exam;

/// <summary>
///     「考试安排」设置页：先选看板（每块看板是一个文件，自带标题、副标题与考试安排），
///     再编这块看板的标题与副标题，最后是考试清单 + 一张表单（新增 / 编辑共用）。
///     <para>
///         表单里的每个字段都是**草稿**，点「保存」才写进 <see cref="IExamService" />：
///         考试安排是业务数据（<c>data/exam/boards/&lt;Id&gt;.json</c>），不是配置项，
///         而且一张考试要一次性凑齐科目 / 日期 / 起止时刻，逐字段落盘只会产生半截数据。
///     </para>
///     <para>
///         本页由键控 DI 以 transient 创建，而 <see cref="IExamService" /> 是单例：
///         订阅了它就必须在页面离开可视树时断开（<see cref="Attach" /> / <see cref="Detach" />
///         由页面 code-behind 调）。
///     </para>
/// </summary>
public partial class ExamScheduleSettingsPageViewModel : ViewModelBase
{
    /// <summary>状态那一列要跟着时间走（「还有 12 分钟」），但不必每秒刷，15 秒足够。</summary>
    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(15);

    /// <summary>取色器拖动时会连发很多次，攒 400ms 再落盘（与作业板默认颜色同一套做法）。</summary>
    private static readonly TimeSpan ColorCommitDelay = TimeSpan.FromMilliseconds(400);

    private readonly IExamService _examService;
    private readonly DispatcherTimer _statusTimer;

    /// <summary>重建下拉框时置真：那时设 <see cref="SelectedBoardOption" /> 不该真的去切看板。</summary>
    private bool _isSyncingBoardSelection;

    /// <summary>当前草稿属于哪一块看板。换了看板才重新灌草稿，免得编辑考试时把正在输入的标题冲掉。</summary>
    private Guid _draftBoardId;

    private Color? _subtitleColorDraft;
    private DispatcherTimer? _subtitleColorCommitTimer;
    private bool _isAttached;

    public ExamScheduleSettingsPageViewModel(MainConfigHandler configHandler, IExamService examService)
        : base(configHandler)
    {
        _examService = examService;
        Items = [];
        BoardOptions = [];

        _statusTimer = new DispatcherTimer { Interval = StatusRefreshInterval };
        _statusTimer.Tick += (_, _) => RefreshStatuses();

        ResetForm();
        Rebuild();
    }

    /// <summary>看板显示偏好：这里只用它的「即将开始」阈值，好让列表里的状态与看板一致。</summary>
    public ExamBoardSettingsConfig Settings => Config.ExamBoard;

    #region 看板

    /// <summary>全部看板（下拉框）。标签就是看板标题，所以改标题等于改这块看板的名字。</summary>
    public ObservableCollection<ExamBoardOption> BoardOptions { get; }

    /// <summary>当前看板。换一项就等于切看板（整套内容一起换）。</summary>
    [ObservableProperty]
    private ExamBoardOption? _selectedBoardOption;

    /// <summary>只剩一块看板时不给删——界面上永远要有一块可看的看板。</summary>
    public bool CanDeleteBoard => BoardOptions.Count > 1;

    partial void OnSelectedBoardOptionChanged(ExamBoardOption? value)
    {
        if (_isSyncingBoardSelection || value is null)
            return;

        if (value.Id == _examService.ActiveBoard.Id)
            return;

        // 切之前先把还压在草稿里的标题 / 颜色存住，否则切过去就丢。
        CommitTitle();
        CommitSubtitle();
        FlushColorDraft();

        _examService.SelectBoard(value.Id);
    }

    /// <summary>新建一块空看板并切过去。</summary>
    [RelayCommand]
    private void CreateBoard()
    {
        CommitTitle();
        CommitSubtitle();
        FlushColorDraft();

        var name = string.Format(CR.Settings_Exam_Schedule_BoardNewTitle, BoardOptions.Count + 1);
        _examService.CreateBoard(name);
    }

    /// <summary>删掉当前看板连同它的考试（确认框在页面 code-behind 里弹）。返回是否删成功。</summary>
    public bool DeleteActiveBoard() => _examService.DeleteBoard(_examService.ActiveBoard.Id);

    #endregion

    #region 标题与副标题

    /// <summary>主标题草稿。留空 = 用默认名（「考试看板」）。</summary>
    [ObservableProperty]
    private string _titleDraft = string.Empty;

    /// <summary>副标题草稿。留空 = 不显示这一行。</summary>
    [ObservableProperty]
    private string _subtitleDraft = string.Empty;

    /// <summary>副标题取色器的草稿。</summary>
    public Color SubtitleColorDraft
    {
        get => _subtitleColorDraft ?? _examService.ActiveBoard.SubtitleColor ?? ResolveThemeSecondaryColor();
        set
        {
            if (_subtitleColorDraft == value)
                return;

            _subtitleColorDraft = value;
            OnPropertyChanged();
            ScheduleColorCommit(value);
        }
    }

    /// <summary>副标题颜色是否跟随主题（看板文件里为空就是跟随），对应设置卡上的开关。</summary>
    public bool FollowSubtitleThemeColor
    {
        get => _examService.ActiveBoard.SubtitleColor is null;
        set
        {
            if (value == FollowSubtitleThemeColor)
                return;

            CancelColorCommit();

            if (value)
            {
                _examService.ActiveBoard.SubtitleColor = null;
                _examService.Save();
            }
            else
            {
                // 从「跟随主题」切到自定义时，起点取主题当前的次要文字色，
                // 直接给黑色的话深色模式下副标题会糊在背景里。
                _subtitleColorDraft ??= ResolveThemeSecondaryColor();
                _examService.ActiveBoard.SubtitleColor = _subtitleColorDraft;
                _examService.Save();
                OnPropertyChanged(nameof(SubtitleColorDraft));
            }

            // 取色器只在「自定义」时显示，换档位要通知这一条。
            OnPropertyChanged();
        }
    }

    /// <summary>把还压在草稿里的文本、数值与颜色立刻写进当前看板（失焦 / 回车 / 离开页面时调）。</summary>
    public void CommitAll()
    {
        CommitTitle();
        CommitSubtitle();
        FlushColorDraft();
    }

    private void CommitTitle()
    {
        var value = (TitleDraft ?? string.Empty).Trim();
        var board = _examService.ActiveBoard;

        if (!string.Equals(board.Title, value, StringComparison.Ordinal))
        {
            board.Title = value;
            _examService.Save();
        }

        if (!string.Equals(TitleDraft, value, StringComparison.Ordinal))
            TitleDraft = value;
    }

    private void CommitSubtitle()
    {
        var value = (SubtitleDraft ?? string.Empty).Trim();
        var board = _examService.ActiveBoard;

        if (!string.Equals(board.Subtitle, value, StringComparison.Ordinal))
        {
            board.Subtitle = value;
            _examService.Save();
        }

        if (!string.Equals(SubtitleDraft, value, StringComparison.Ordinal))
            SubtitleDraft = value;
    }

    /// <summary>把还压在草稿里的颜色立刻写进当前看板，并停掉待触发的防抖计时。</summary>
    public void FlushColorDraft()
    {
        CancelColorCommit();

        if (_subtitleColorDraft is not { } draft || FollowSubtitleThemeColor)
            return;

        if (_examService.ActiveBoard.SubtitleColor == draft)
            return;

        _examService.ActiveBoard.SubtitleColor = draft;
        _examService.Save();
    }

    private void ScheduleColorCommit(Color value)
    {
        CancelColorCommit();

        _subtitleColorCommitTimer = new DispatcherTimer { Interval = ColorCommitDelay };
        _subtitleColorCommitTimer.Tick += (_, _) =>
        {
            CancelColorCommit();

            // 已经切回「跟随主题」了：别再把手里的颜色写回看板。
            if (FollowSubtitleThemeColor)
                return;

            _examService.ActiveBoard.SubtitleColor = value;
            _examService.Save();
        };
        _subtitleColorCommitTimer.Start();
    }

    private void CancelColorCommit()
    {
        _subtitleColorCommitTimer?.Stop();
        _subtitleColorCommitTimer = null;
    }

    #endregion

    /// <summary>当前看板的全部考试（含已考完的），按开考时刻升序。</summary>
    public ObservableCollection<ExamScheduleItem> Items { get; }

    /// <summary>清单为空时显示那句「还没有考试安排」。</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>清单条数文案（「共 3 场」）。</summary>
    public string CountText => string.Format(CR.Settings_Exam_Schedule_Count, Items.Count);

    #region 表单

    /// <summary>科目草稿。</summary>
    [ObservableProperty]
    private string _subjectDraft = string.Empty;

    /// <summary>考试日期草稿（<see cref="Avalonia.Controls.CalendarDatePicker" /> 的 <c>SelectedDate</c> 是 <see cref="DateTime" />）。</summary>
    [ObservableProperty]
    private DateTime? _dateDraft;

    /// <summary>开考时刻草稿（<see cref="Avalonia.Controls.TimePicker" /> 的 <c>SelectedTime</c> 是 <see cref="TimeSpan" />）。</summary>
    [ObservableProperty]
    private TimeSpan? _startDraft;

    /// <summary>收卷时刻草稿。</summary>
    [ObservableProperty]
    private TimeSpan? _endDraft;

    /// <summary>试卷张数草稿。空着 = 不显示。</summary>
    [ObservableProperty]
    private decimal? _paperCountDraft;

    /// <summary>页数草稿。</summary>
    [ObservableProperty]
    private decimal? _pageCountDraft;

    /// <summary>大题数草稿。</summary>
    [ObservableProperty]
    private decimal? _questionCountDraft;

    /// <summary>备注草稿（考场、座位号之类）。</summary>
    [ObservableProperty]
    private string _noteDraft = string.Empty;

    /// <summary>表单底部的错误提示（科目没填、时间反了、与另一场重叠）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormError))]
    private string? _formError;

    /// <summary>表单现在是「编辑某一场」而不是「新增」。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    private bool _isEditing;

    /// <summary>正在编辑哪一场（新增时为 <see langword="null" />）。</summary>
    private Guid? _editingId;

    public bool HasFormError => !string.IsNullOrEmpty(FormError);

    /// <summary>表单标题：新增时「添加考试」，编辑时「编辑考试」。</summary>
    public string FormTitle => IsEditing ? CR.Settings_Exam_Schedule_FormEdit : CR.Settings_Exam_Schedule_Form;

    #endregion

    /// <summary>挂上考试数据与配置变化的订阅（页面进入可视树时调）。重复调用不会重复挂。</summary>
    public void Attach()
    {
        if (_isAttached)
            return;

        _isAttached = true;
        _examService.Changed += OnServiceChanged;
        Settings.PropertyChanged += OnSettingsChanged;
        _statusTimer.Start();
        Rebuild();
    }

    /// <summary>断开订阅（页面离开可视树时调）。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        _statusTimer.Stop();
        CancelColorCommit();
        _examService.Changed -= OnServiceChanged;
        Settings.PropertyChanged -= OnSettingsChanged;
    }

    /// <summary>点某一行的「编辑」：把那场考试的内容填进表单。</summary>
    public void BeginEdit(ExamScheduleItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var exam = item.Exam;
        _editingId = exam.Id;
        IsEditing = true;
        SubjectDraft = exam.Subject;
        DateDraft = exam.Date.ToDateTime(TimeOnly.MinValue);
        StartDraft = exam.StartTime.ToTimeSpan();
        EndDraft = exam.EndTime.ToTimeSpan();
        PaperCountDraft = exam.PaperCount;
        PageCountDraft = exam.PageCount;
        QuestionCountDraft = exam.QuestionCount;
        NoteDraft = exam.Note ?? string.Empty;
        FormError = null;
    }

    /// <summary>点「取消编辑」：表单退回新增态（清空并回到今天）。</summary>
    public void CancelEdit() => ResetForm();

    /// <summary>删除某一场考试（确认框在页面 code-behind 里弹）。</summary>
    public void Remove(ExamScheduleItem item)
    {
        if (item is not null)
            _examService.Remove(item.Id);
    }

    /// <summary>「保存」：校验通过就写进考试服务，然后把表单退回新增态。</summary>
    [RelayCommand]
    private void Submit()
    {
        var subject = (SubjectDraft ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(subject))
        {
            FormError = CR.Settings_Exam_Schedule_InvalidSubject;
            return;
        }

        var date = DateOnly.FromDateTime(DateDraft ?? DateTime.Today);
        var start = StartDraft ?? new TimeSpan(8, 0, 0);
        var end = EndDraft ?? new TimeSpan(10, 0, 0);
        if (end <= start)
        {
            FormError = CR.Settings_Exam_Schedule_InvalidTime;
            return;
        }

        var candidate = new ExamEntry
        {
            Id = _editingId ?? Guid.NewGuid(),
            Subject = subject,
            Date = date,
            StartTime = TimeOnly.FromTimeSpan(start),
            EndTime = TimeOnly.FromTimeSpan(end),
            PaperCount = ToCount(PaperCountDraft),
            PageCount = ToCount(PageCountDraft),
            QuestionCount = ToCount(QuestionCountDraft),
            Note = string.IsNullOrWhiteSpace(NoteDraft) ? null : NoteDraft.Trim()
        };

        // 同一时段被两场考试占着，看板上就会出现两行「进行中」，先把这一条拦下来。
        if (_examService.Exams.Any(other => other.Id != candidate.Id && ExamStatusPolicy.Overlaps(other, candidate)))
        {
            FormError = CR.Settings_Exam_Schedule_Overlap;
            return;
        }

        if (_editingId is { } editingId)
        {
            // 按 Id 写回：保持它在清单里的位置与 Id 不变，只换内容。
            var stored = _examService.Exams.FirstOrDefault(exam => exam.Id == editingId);
            if (stored is null)
                _examService.Add(candidate);
            else
            {
                var updated = stored.Clone();
                updated.CopyFrom(candidate);
                _examService.Update(updated);
            }
        }
        else
        {
            _examService.Add(candidate);
        }

        ResetForm();
    }

    /// <summary>表单退回新增态：清空、日期回到今天、默认 08:00–10:00。</summary>
    private void ResetForm()
    {
        _editingId = null;
        IsEditing = false;
        SubjectDraft = string.Empty;
        DateDraft = DateTime.Today;
        StartDraft = new TimeSpan(8, 0, 0);
        EndDraft = new TimeSpan(10, 0, 0);
        PaperCountDraft = null;
        PageCountDraft = null;
        QuestionCountDraft = null;
        NoteDraft = string.Empty;
        FormError = null;
    }

    /// <summary>重建下拉框、草稿与清单（看板或考试变了都走这里）。</summary>
    private void Rebuild()
    {
        RefreshBoardOptions();
        RefreshDraftsIfBoardChanged();
        RebuildItems();
    }

    /// <summary>把看板清单灌进下拉框，并选中当前那块（用守卫避免设选中项时又去切看板）。</summary>
    private void RefreshBoardOptions()
    {
        _isSyncingBoardSelection = true;
        try
        {
            BoardOptions.Clear();
            foreach (var board in _examService.Boards)
                BoardOptions.Add(new ExamBoardOption(board.Id, board.ResolveTitle()));

            SelectedBoardOption = BoardOptions.FirstOrDefault(option => option.Id == _examService.ActiveBoard.Id);
            OnPropertyChanged(nameof(CanDeleteBoard));
        }
        finally
        {
            _isSyncingBoardSelection = false;
        }
    }

    /// <summary>换看板了才重新灌标题 / 副标题 / 颜色草稿，并把考试表单退回新增态。</summary>
    private void RefreshDraftsIfBoardChanged()
    {
        var board = _examService.ActiveBoard;
        if (board.Id == _draftBoardId)
            return;

        _draftBoardId = board.Id;
        CancelColorCommit();

        TitleDraft = board.Title ?? string.Empty;
        SubtitleDraft = board.Subtitle ?? string.Empty;
        _subtitleColorDraft = board.SubtitleColor;
        OnPropertyChanged(nameof(SubtitleColorDraft));
        OnPropertyChanged(nameof(FollowSubtitleThemeColor));

        ResetForm();
    }

    /// <summary>重建清单：当前看板的全部考试按开考时刻升序（设置页要能看到历史，所以不筛掉考完的）。</summary>
    private void RebuildItems()
    {
        var now = DateTime.Now;
        var threshold = Settings.UpcomingThresholdMinutes;

        var ordered = _examService.Exams
            .Where(exam => exam is not null)
            .OrderBy(exam => exam.StartAt)
            .ThenBy(exam => exam.Subject, StringComparer.CurrentCulture)
            .ToList();

        Items.Clear();
        foreach (var exam in ordered)
        {
            var item = new ExamScheduleItem(exam);
            item.Refresh(now, threshold);
            Items.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>只刷新状态那一列（清单本身没变，不必重建控件）。</summary>
    private void RefreshStatuses()
    {
        var now = DateTime.Now;
        var threshold = Settings.UpcomingThresholdMinutes;
        foreach (var item in Items)
            item.Refresh(now, threshold);
    }

    private void OnServiceChanged(object? sender, EventArgs e) => Rebuild();

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 阈值变了，状态那一列的分档也跟着变（看板上是同一套规则）。
        if (e.PropertyName is null or nameof(ExamBoardSettingsConfig.UpcomingThresholdMinutes))
            RefreshStatuses();
    }

    private static int? ToCount(decimal? draft) =>
        draft is { } value ? ExamEntry.NormalizeCount((int)value) : null;

    /// <summary>主题里的「次要文字色」——副标题默认就用它（比标题淡一档）。</summary>
    private static Color ResolveThemeSecondaryColor()
    {
        // 条件访问不能带 out 参数，所以先取实例再查资源。
        if (Application.Current is { } application)
        {
            // 必须显式带 ThemeVariant，否则拿到的是亮色字典里的颜色。
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

/// <summary>看板下拉项。标签直接取看板标题，所以「改标题」与「给看板改名」是同一件事。</summary>
public sealed record ExamBoardOption(Guid Id, string DisplayName);

/// <summary>
///     考试清单里的一行：把一场考试摊成「日期 | 科目 | 时间 | 状态」四列，
///     状态与卷面信息由页面统一按当前时刻刷新。
/// </summary>
public partial class ExamScheduleItem(ExamEntry exam) : ObservableObject
{
    public ExamEntry Exam { get; } = exam;

    public Guid Id => Exam.Id;

    /// <summary>日期列（今天 / 明天 / 10月20日 周一）。</summary>
    [ObservableProperty]
    private string _dateText = string.Empty;

    /// <summary>科目列。</summary>
    [ObservableProperty]
    private string _subject = string.Empty;

    /// <summary>时间列（08:00 – 10:00）。</summary>
    [ObservableProperty]
    private string _timeText = string.Empty;

    /// <summary>状态列（即将开始 · 还有 12 分钟）。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>卷面信息（2 张 · 8 页 · 3 大题），没填就是空串。</summary>
    [ObservableProperty]
    private string _paperInfo = string.Empty;

    [ObservableProperty]
    private bool _hasPaperInfo;

    [ObservableProperty]
    private bool _isUpcoming;

    [ObservableProperty]
    private bool _isInProgress;

    [ObservableProperty]
    private bool _isFinished;

    /// <summary>备注（考场之类），没填就不显示。</summary>
    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private bool _hasNote;

    /// <summary>按当前时刻重算这一行的文案与状态色。</summary>
    public void Refresh(DateTime now, int upcomingMinutes)
    {
        var today = DateOnly.FromDateTime(now);
        var status = ExamStatusPolicy.Resolve(Exam, now, upcomingMinutes);

        DateText = ExamTimeFormatter.FormatDate(Exam.Date, today);
        Subject = Exam.Subject;
        TimeText = ExamTimeFormatter.FormatTimeRange(Exam);
        StatusText = ExamTimeFormatter.FormatStatusDetail(status, Exam, now);
        PaperInfo = ExamTimeFormatter.FormatPaperInfo(Exam);
        HasPaperInfo = Exam.HasPaperInfo;
        Note = Exam.Note ?? string.Empty;
        HasNote = !string.IsNullOrEmpty(Note);

        IsUpcoming = status == ExamStatus.Upcoming;
        IsInProgress = status == ExamStatus.InProgress;
        IsFinished = status == ExamStatus.Finished;
    }
}
