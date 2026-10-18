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

namespace Corkboard.ViewModels.ExamBoard;

/// <summary>
///     「编辑看板」窗口的 ViewModel：一块看板文件里的全部内容都在这里改——
///     标题、副标题、副标题颜色，以及这张表上的每一场考试。
///     <para>
///         为什么单独开一个窗口、而不是继续挤在设置页里：一份看板要同时摆开「一场考试十来列的表」
///         和「一场考试十来个字段的表单」，设置页那个限宽 960 的滚动列里两样都只能压扁。
///         窗口里用左右两栏（左表右表单）把它们摊平，宽度随窗口走。
///     </para>
///     <para>
///         表单字段全部是草稿，点「保存」才写进 <see cref="IExamService" />：考试是业务数据
///         （<c>data/exam/boards/&lt;Id&gt;.json</c>），一张考试要一次性凑齐科目 / 日期 / 起止时刻，
///         逐字段落盘只会留下半截数据。
///     </para>
///     <para>
///         本类是 transient，<see cref="IExamService" /> 与配置 handler 是单例：订阅了就必须
///         在窗口关闭时断开（<see cref="Attach" /> / <see cref="Detach" /> 由窗口 code-behind 调）。
///     </para>
/// </summary>
public partial class ExamBoardEditorViewModel : ViewModelBase
{
    /// <summary>状态那一列要跟着时间走（「还有 12 分钟」），15 秒刷一次足够。</summary>
    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(15);

    /// <summary>取色器拖动时会连发很多次，攒 400ms 再落盘（与作业板默认颜色同一套做法）。</summary>
    private static readonly TimeSpan ColorCommitDelay = TimeSpan.FromMilliseconds(400);

    private readonly IExamService _examService;
    private readonly DispatcherTimer _statusTimer;

    private Color? _subtitleColorDraft;
    private DispatcherTimer? _subtitleColorCommitTimer;
    private bool _isAttached;

    /// <summary>正在编辑哪一场（新增时为 <see langword="null" />）。</summary>
    private Guid? _editingId;

    public ExamBoardEditorViewModel(MainConfigHandler configHandler, IExamService examService)
        : base(configHandler)
    {
        _examService = examService;
        Items = [];

        _statusTimer = new DispatcherTimer { Interval = StatusRefreshInterval };
        _statusTimer.Tick += (_, _) => RefreshStatuses();

        ReloadDrafts();
        RebuildItems();
    }

    /// <summary>看板显示偏好：这里只用它的「即将开始」阈值，好让表里的状态与看板一致。</summary>
    public ExamBoardSettingsConfig Settings => Config.ExamBoard;

    /// <summary>当前看板的标题（副标题为空时窗口标题也只显示这个）。</summary>
    public string BoardTitle => _examService.ActiveBoard.ResolveTitle();

    /// <summary>窗口标题：「编辑看板 · 期中考试」。</summary>
    public string WindowTitle => string.Format(CR.Settings_Exam_Editor_WindowTitle, BoardTitle);

    /// <summary>
    ///     换了一块看板（用户在设置页点了另一行的「编辑」）：把草稿与表格整个重灌。
    ///     窗口已经开着的时候由 <see cref="App.ShowExamBoardEditorWindow" /> 调。
    /// </summary>
    public void ReloadBoard()
    {
        ReloadDrafts();
        RebuildItems();
    }

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

    /// <summary>副标题颜色是否跟随主题（看板文件里为空就是跟随）。</summary>
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

    /// <summary>把还压在草稿里的文本与颜色立刻写进当前看板（失焦 / 回车 / 关窗前调）。</summary>
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
            OnPropertyChanged(nameof(BoardTitle));
            OnPropertyChanged(nameof(WindowTitle));
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

    /// <summary>表里一场都没有时显示那句「还没有考试安排」。</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>条数文案（「共 3 场」）。</summary>
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

    public bool HasFormError => !string.IsNullOrEmpty(FormError);

    /// <summary>表单标题：新增时「添加考试」，编辑时「编辑考试」。</summary>
    public string FormTitle => IsEditing ? CR.Settings_Exam_Schedule_FormEdit : CR.Settings_Exam_Schedule_Form;

    #endregion

    /// <summary>挂上考试数据与配置变化的订阅（窗口打开时调）。重复调用不会重复挂。</summary>
    public void Attach()
    {
        if (_isAttached)
            return;

        _isAttached = true;
        _examService.Changed += OnServiceChanged;
        Settings.PropertyChanged += OnSettingsChanged;
        _statusTimer.Start();
        ReloadDrafts();
        RebuildItems();
    }

    /// <summary>断开订阅（窗口关闭时调）。</summary>
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

    /// <summary>点表里某一行的「编辑」：把那场考试的内容填进表单。</summary>
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

    /// <summary>点「取消」：表单退回新增态（清空并回到今天）。</summary>
    public void CancelEdit() => ResetForm();

    /// <summary>删除某一场考试（确认框在窗口 code-behind 里弹）。</summary>
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
            // 按 Id 写回：保持它在表里的位置与 Id 不变，只换内容。
            var stored = _examService.Exams.FirstOrDefault(exam => exam.Id == editingId);
            if (stored is null)
            {
                _examService.Add(candidate);
            }
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

    /// <summary>把当前看板的标题 / 副标题 / 颜色灌进草稿，并把考试表单退回新增态。</summary>
    private void ReloadDrafts()
    {
        var board = _examService.ActiveBoard;
        CancelColorCommit();

        TitleDraft = board.Title ?? string.Empty;
        SubtitleDraft = board.Subtitle ?? string.Empty;
        _subtitleColorDraft = board.SubtitleColor;
        OnPropertyChanged(nameof(SubtitleColorDraft));
        OnPropertyChanged(nameof(FollowSubtitleThemeColor));
        OnPropertyChanged(nameof(BoardTitle));
        OnPropertyChanged(nameof(WindowTitle));

        ResetForm();
    }

    /// <summary>重建表格：当前看板的全部考试按开考时刻升序（编辑时要能看到历史，所以不筛掉考完的）。</summary>
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

    /// <summary>只刷新状态那一列（表本身没变，不必重建控件）。</summary>
    private void RefreshStatuses()
    {
        var now = DateTime.Now;
        var threshold = Settings.UpcomingThresholdMinutes;
        foreach (var item in Items)
            item.Refresh(now, threshold);
    }

    /// <summary>
    ///     服务变了：重建表格。但**正在填表单时不重灌标题草稿**——那会把用户刚敲了一半的标题冲掉；
    ///     换看板（服务层换了 ActiveBoard）时窗口是新的，草稿自然从头灌。
    /// </summary>
    private void OnServiceChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(BoardTitle));
        RebuildItems();
    }

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

/// <summary>
///     考试表里的一行：把一场考试摊成「日期 | 科目 | 时间 | 状态」几列，
///     状态与卷面信息由窗口统一按当前时刻刷新。
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
