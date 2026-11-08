using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Actions;
using Corkboard.Core.Time;
using Corkboard.Services.Actions;
using LR = Corkboard.Langs.Action.Resources;

namespace Corkboard.ViewModels.Actions;

/// <summary>「目标看板」下拉里的一项。</summary>
public sealed class ActionTargetOption
{
    public string Id { get; init; } = "";

    public string Label { get; init; } = "";

    public bool IsAvailable { get; init; }

    public string? UnavailableReason { get; init; }

    /// <summary>不可用时在下拉里附一句原因（可用时就是名字本身）。</summary>
    public string DisplayText => IsAvailable || string.IsNullOrWhiteSpace(UnavailableReason)
        ? Label
        : $"{Label}（{UnavailableReason}）";
}

/// <summary>「操作」下拉里的一项。</summary>
public sealed class ActionOperationOption
{
    public BoardOperation Value { get; init; }

    public string Label { get; init; } = "";
}

/// <summary>编辑器里的一行步骤。</summary>
public sealed partial class ActionStepRow : ObservableObject
{
    [ObservableProperty] private ActionTargetOption? _selectedTarget;

    [ObservableProperty] private ActionOperationOption? _selectedOperation;
}

/// <summary>
///     规则编辑器的 ViewModel：只负责字段草稿与校验，保存/删除由窗口协调
///     （弹层关闭、删除确认框都画在 ActionWindow 的 overlay 宿主里）。
/// </summary>
public sealed partial class ActionEditorViewModel : ObservableObject
{
    private readonly ActionRuleStore _store;
    private readonly IBoardTargetRegistry _targets;
    private readonly NtpTimeService _time;

    private ActionRule? _original;
    private bool _loading;
    private bool _hasTime;

    public ActionEditorViewModel(ActionRuleStore store, IBoardTargetRegistry targets, NtpTimeService time)
    {
        _store = store;
        _targets = targets;
        _time = time;

        TargetOptions = targets.Targets
            .Select(target => new ActionTargetOption
            {
                Id = target.Id,
                Label = ActionTextFormatter.DescribeTarget(target),
                IsAvailable = target.IsAvailable,
                UnavailableReason = target.UnavailableReason
            })
            .ToList();

        OperationOptions =
        [
            new ActionOperationOption { Value = BoardOperation.Show, Label = ActionTextFormatter.DescribeOperation(BoardOperation.Show) },
            new ActionOperationOption { Value = BoardOperation.Hide, Label = ActionTextFormatter.DescribeOperation(BoardOperation.Hide) }
        ];

        TriggerOptions =
        [
            LR.Action_Trigger_Once, LR.Action_Trigger_Daily, LR.Action_Trigger_Weekly
        ];

        IsEnabled = true;
        TriggerKindIndex = 1;
        TriggerTime = NextWholeHour();
        _hasTime = true;
    }

    /// <summary>保存成功后请求关掉弹层。</summary>
    public event EventHandler? Saved;

    /// <summary>点了「删除」：窗口先弹确认框，确认后直接调 <see cref="Delete" />。</summary>
    public event EventHandler? DeleteRequested;

    /// <summary>请求取消（不落盘）。</summary>
    public event EventHandler? Cancelled;

    public IReadOnlyList<ActionTargetOption> TargetOptions { get; }

    public IReadOnlyList<ActionOperationOption> OperationOptions { get; }

    public IReadOnlyList<string> TriggerOptions { get; }

    public ObservableCollection<ActionStepRow> Steps { get; } = [];

    private bool IsEditing => _original is not null;

    /// <summary>编辑已有规则时才显示「删除」。</summary>
    public bool CanDelete => IsEditing;

    // ── 标题/字段 ─────────────────────────────────────────────────────────────

    public string Title => IsEditing ? LR.Action_Editor_EditTitle : LR.Action_Editor_NewTitle;

    [ObservableProperty] private string _name = "";

    [ObservableProperty] private bool _isEnabled;

    [ObservableProperty] private int _triggerKindIndex;

    partial void OnTriggerKindIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsOnce));
        OnPropertyChanged(nameof(IsDaily));
        OnPropertyChanged(nameof(IsWeekly));
        Validate();
    }

    public bool IsOnce => TriggerKindIndex == 0;

    public bool IsDaily => TriggerKindIndex == 1;

    public bool IsWeekly => TriggerKindIndex == 2;

    /// <summary>单次触发的日期。</summary>
    [ObservableProperty] private DateTime? _triggerDate;

    /// <summary>触发时刻（24 小时制）。</summary>
    [ObservableProperty] private TimeSpan? _triggerTime;

    partial void OnTriggerTimeChanged(TimeSpan? value)
    {
        _hasTime = value is not null;
        Validate();
    }

    [ObservableProperty] private DateTime? _rangeStart;

    [ObservableProperty] private DateTime? _rangeEnd;

    // 星期（每周触发）
    [ObservableProperty] private bool _weekdayMonday = true;

    [ObservableProperty] private bool _weekdayTuesday = true;

    [ObservableProperty] private bool _weekdayWednesday = true;

    [ObservableProperty] private bool _weekdayThursday = true;

    [ObservableProperty] private bool _weekdayFriday = true;

    [ObservableProperty] private bool _weekdaySaturday;

    [ObservableProperty] private bool _weekdaySunday;

    // ── 校验 ──────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _errorText = "";

    [ObservableProperty] private string _warningText = "";

    [ObservableProperty] private bool _hasErrors;

    [ObservableProperty] private bool _hasWarnings;

    /// <summary>保存按钮是否可用（有错误就禁用）。</summary>
    public bool CanSave => !HasErrors;

    public bool CanAddStep => Steps.Count < ActionRuleValidator.MaxSteps;

    // 名字与日期范围也要参与校验
    partial void OnNameChanged(string value) => Validate();

    partial void OnTriggerDateChanged(DateTime? value) => Validate();

    partial void OnRangeStartChanged(DateTime? value) => Validate();

    partial void OnRangeEndChanged(DateTime? value) => Validate();

    // ── 载入 ──────────────────────────────────────────────────────────────────

    /// <summary>载入一条已有规则（编辑）或一条草稿（新建 / 示例）。</summary>
    public void Load(ActionRule? rule)
    {
        _loading = true;
        try
        {
            _original = rule is null ? null : rule;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(CanDelete));

            if (rule is null)
            {
                Name = "";
                IsEnabled = true;
                TriggerKindIndex = 1;
                TriggerDate = null;
                TriggerTime = NextWholeHour();
                _hasTime = true;
                RangeStart = null;
                RangeEnd = null;
                SetWeekdays(ActionDayFlags.Weekdays);
                Steps.Clear();
                AddStepCore(ActionTargetOptionFor(BoardTargetIds.Homework), BoardOperation.Show);
            }
            else
            {
                Name = rule.Name ?? "";
                IsEnabled = rule.Enabled;
                TriggerKindIndex = (int)rule.Trigger.Kind;
                TriggerDate = rule.Trigger.Date?.ToDateTime(TimeOnly.MinValue);
                TriggerTime = rule.Trigger.Time.ToTimeSpan();
                _hasTime = true;
                RangeStart = rule.Condition?.Start?.ToDateTime(TimeOnly.MinValue);
                RangeEnd = rule.Condition?.End?.ToDateTime(TimeOnly.MinValue);
                SetWeekdays(rule.Trigger.Days);

                Steps.Clear();
                foreach (var step in rule.Steps.Take(ActionRuleValidator.MaxSteps))
                {
                    AddStepCore(ActionTargetOptionFor(step.TargetId), step.Operation);
                }

                if (Steps.Count == 0) AddStepCore(ActionTargetOptionFor(BoardTargetIds.Homework), BoardOperation.Show);
            }
        }
        finally
        {
            _loading = false;
        }

        NotifyStepsChanged();
        Validate();
    }

    private void SetWeekdays(ActionDayFlags days)
    {
        if (days == ActionDayFlags.None) days = ActionDayFlags.Weekdays;
        WeekdayMonday = days.HasFlag(ActionDayFlags.Monday);
        WeekdayTuesday = days.HasFlag(ActionDayFlags.Tuesday);
        WeekdayWednesday = days.HasFlag(ActionDayFlags.Wednesday);
        WeekdayThursday = days.HasFlag(ActionDayFlags.Thursday);
        WeekdayFriday = days.HasFlag(ActionDayFlags.Friday);
        WeekdaySaturday = days.HasFlag(ActionDayFlags.Saturday);
        WeekdaySunday = days.HasFlag(ActionDayFlags.Sunday);
    }

    private ActionDayFlags ReadWeekdays()
    {
        var days = ActionDayFlags.None;
        if (WeekdayMonday) days |= ActionDayFlags.Monday;
        if (WeekdayTuesday) days |= ActionDayFlags.Tuesday;
        if (WeekdayWednesday) days |= ActionDayFlags.Wednesday;
        if (WeekdayThursday) days |= ActionDayFlags.Thursday;
        if (WeekdayFriday) days |= ActionDayFlags.Friday;
        if (WeekdaySaturday) days |= ActionDayFlags.Saturday;
        if (WeekdaySunday) days |= ActionDayFlags.Sunday;
        return days;
    }

    private ActionTargetOption? ActionTargetOptionFor(string? targetId) =>
        TargetOptions.FirstOrDefault(option => string.Equals(option.Id, targetId, StringComparison.Ordinal))
        ?? TargetOptions.FirstOrDefault();

    private TimeSpan NextWholeHour()
    {
        var now = _time.NowLocal;
        var next = now.AddHours(1);
        return new TimeSpan(next.Hour, 0, 0);
    }

    // ── 步骤行 ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddStep()
    {
        if (Steps.Count >= ActionRuleValidator.MaxSteps) return;
        AddStepCore(ActionTargetOptionFor(BoardTargetIds.Homework), BoardOperation.Show);
        NotifyStepsChanged();
        Validate();
    }

    [RelayCommand]
    private void RemoveStep(ActionStepRow? row)
    {
        if (row is null) return;
        row.PropertyChanged -= OnStepChanged;
        Steps.Remove(row);
        NotifyStepsChanged();
        Validate();
    }

    private void AddStepCore(ActionTargetOption? target, BoardOperation operation)
    {
        var row = new ActionStepRow
        {
            SelectedTarget = target,
            SelectedOperation = OperationOptions.FirstOrDefault(option => option.Value == operation) ?? OperationOptions[0]
        };
        row.PropertyChanged += OnStepChanged;
        Steps.Add(row);
    }

    private void OnStepChanged(object? sender, PropertyChangedEventArgs e) => Validate();

    private void NotifyStepsChanged()
    {
        OnPropertyChanged(nameof(CanAddStep));
        OnPropertyChanged(nameof(StepCountText));
    }

    public string StepCountText =>
        $"{Steps.Count}/{ActionRuleValidator.MaxSteps}";

    // ── 校验 ──────────────────────────────────────────────────────────────────

    /// <summary>按当前草稿重算一遍错误 / 警告。</summary>
    public void Validate()
    {
        if (_loading) return;

        var now = _time.NowLocal;
        var rule = BuildRule(now);
        var problems = ActionRuleValidator.Validate(rule, now).ToList();

        // 校验器的 Time 不可空（00:00 合法），「没选时刻」由编辑器自己补一条。
        if (!_hasTime)
            problems.Insert(0, new ActionValidationError(ActionValidationMessage.NoTime,
                ActionValidationSeverity.Error));

        var errors = problems.Where(problem => !problem.IsWarning)
            .Select(problem => Localize(problem))
            .ToList();
        var warnings = problems.Where(problem => problem.IsWarning)
            .Select(problem => Localize(problem))
            .ToList();

        ErrorText = string.Join("\n", errors);
        WarningText = string.Join("\n", warnings);
        HasErrors = errors.Count > 0;
        HasWarnings = warnings.Count > 0;
        OnPropertyChanged(nameof(CanSave));
    }

    private static string Localize(ActionValidationError problem)
    {
        var key = problem.Message;
        var text = LR.ResourceManager.GetString(key, LR.Culture);
        if (string.IsNullOrWhiteSpace(text)) text = key;
        if (problem.TargetId is { Length: > 0 } targetId)
            text = $"{text}：{ActionTextFormatter.DescribeTarget(targetId)}";
        return text;
    }

    // ── 保存 / 删除 / 取消 ────────────────────────────────────────────────────

    [RelayCommand]
    private void Save()
    {
        Validate();
        if (HasErrors) return;

        var now = _time.NowLocal;
        var rule = BuildRule(now);

        try
        {
            _store.Upsert(rule);
        }
        catch (ActionStoreReadOnlyException ex)
        {
            HasErrors = true;
            ErrorText = ex.Message;
            OnPropertyChanged(nameof(CanSave));
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HasErrors = true;
            ErrorText = string.Format(CultureInfo.CurrentCulture, LR.Action_Error_SaveFailed, ex.Message);
            OnPropertyChanged(nameof(CanSave));
            return;
        }

        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RequestDelete()
    {
        if (!IsEditing) return;
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>确认框点了「删除」之后由窗口调用。</summary>
    public void Delete()
    {
        if (_original is not { } rule) return;
        try
        {
            _store.Remove(rule.Id);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ActionStoreReadOnlyException ex)
        {
            HasErrors = true;
            ErrorText = ex.Message;
            OnPropertyChanged(nameof(CanSave));
        }
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ClearRange()
    {
        RangeStart = null;
        RangeEnd = null;
        Validate();
    }

    // ── 组装规则 ──────────────────────────────────────────────────────────────

    private ActionRule BuildRule(DateTime now)
    {
        var kind = TriggerKindIndex switch
        {
            0 => ActionTriggerKind.Once,
            2 => ActionTriggerKind.Weekly,
            _ => ActionTriggerKind.Daily
        };

        var time = TriggerTime is { } span ? TimeOnly.FromTimeSpan(span) : new TimeOnly(0, 0);
        var trigger = new ActionTrigger
        {
            Kind = kind,
            Time = time,
            Date = kind == ActionTriggerKind.Once && TriggerDate is { } date
                ? DateOnly.FromDateTime(date)
                : null,
            Days = kind == ActionTriggerKind.Weekly ? ReadWeekdays() : ActionDayFlags.None
        };

        ActionCondition? condition = null;
        var start = RangeStart is { } s ? DateOnly.FromDateTime(s) : (DateOnly?)null;
        var end = RangeEnd is { } e ? DateOnly.FromDateTime(e) : (DateOnly?)null;
        if (start is not null || end is not null)
            condition = new ActionCondition { Kind = ActionConditionKind.DateRange, Start = start, End = end };

        var steps = Steps
            .Where(row => row.SelectedTarget is not null)
            .Select(row => new ActionStep
            {
                TargetId = row.SelectedTarget!.Id,
                Operation = row.SelectedOperation?.Value ?? BoardOperation.Show
            })
            .ToList();

        return new ActionRule
        {
            Id = _original?.Id ?? Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
            Enabled = IsEnabled,
            Trigger = trigger,
            Condition = condition,
            Steps = steps,
            CreatedAt = _original?.CreatedAt ?? now,
            UpdatedAt = now
        };
    }
}
