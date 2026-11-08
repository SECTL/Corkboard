using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.DutyRoster;
using Corkboard.Core.Models.SubConfigs.Duty;
using Corkboard.Core.Services.Config;

namespace Corkboard.ViewModels.SettingsPages.Duty;

/// <summary>固定周几表里的一行：星期 → 小组。</summary>
public sealed partial class DutyFixedDayRow : ObservableObject
{
    public DayOfWeek Day { get; init; }

    public string Label { get; init; } = string.Empty;

    public List<DutyGroupOption> Groups { get; init; } = [];

    [ObservableProperty] private DutyGroupOption? _selectedGroup;
}

/// <summary>下拉项：排班模式。</summary>
public sealed class DutyModeOption
{
    public DutyScheduleMode Mode { get; init; }

    public string Label { get; init; } = string.Empty;

    public override string ToString() => Label;
}

/// <summary>下拉项：轮换方向。</summary>
public sealed class DutyDirectionOption
{
    public int Value { get; init; }

    public string Label { get; init; } = string.Empty;

    public override string ToString() => Label;
}

/// <summary>下拉项：加罚处理方式。</summary>
public sealed class DutyPenaltyOption
{
    public DutyPenaltyMode Mode { get; init; }

    public string Label { get; init; } = string.Empty;

    public override string ToString() => Label;
}

/// <summary>下拉项：周起始日。</summary>
public sealed class DutyWeekStartOption
{
    public int Value { get; init; }

    public string Label { get; init; } = string.Empty;

    public override string ToString() => Label;
}

/// <summary>下拉项：默认视图。</summary>
public sealed class DutyViewOption
{
    public DutyViewMode Mode { get; init; }

    public string Label { get; init; } = string.Empty;

    public override string ToString() => Label;
}

/// <summary>
///     「排班规则」设置页：固定周几 / 小组轮流两套逻辑、轮换参数、节假日与加罚策略、界面偏好。
///     <para>
///         规则是业务数据（<c>data/duty/duty.json</c>），点「保存规则」才写盘；
///         界面偏好（周起始日、默认视图等）留在 settings.json，由配置管道自动保存。
///     </para>
/// </summary>
public sealed partial class DutyScheduleSettingsPageViewModel : ViewModelBase
{
    private readonly DutyRosterService _service;
    private bool _loading;

    public DutyScheduleSettingsPageViewModel(MainConfigHandler configHandler, DutyRosterService service)
        : base(configHandler)
    {
        _service = service;

        ModeOptions.Add(new DutyModeOption { Mode = DutyScheduleMode.FixedWeekday, Label = "固定小组扫周几" });
        ModeOptions.Add(new DutyModeOption { Mode = DutyScheduleMode.Rotation, Label = "小组轮流打扫" });

        DirectionOptions.Add(new DutyDirectionOption { Value = 1, Label = "正序（按小组顺序往下）" });
        DirectionOptions.Add(new DutyDirectionOption { Value = -1, Label = "倒序（从最后一组往上）" });

        PenaltyOptions.Add(new DutyPenaltyOption { Mode = DutyPenaltyMode.Swallow, Label = "吞掉：受罚组占用后面小组的日子" });
        PenaltyOptions.Add(new DutyPenaltyOption { Mode = DutyPenaltyMode.PushBack, Label = "顺延：后面所有小组整体往后推" });

        WeekStartOptions.Add(new DutyWeekStartOption { Value = 1, Label = "周一" });
        WeekStartOptions.Add(new DutyWeekStartOption { Value = 0, Label = "周日" });

        ViewOptions.Add(new DutyViewOption { Mode = DutyViewMode.Week, Label = "周视图" });
        ViewOptions.Add(new DutyViewOption { Mode = DutyViewMode.Month, Label = "月视图" });

        foreach (var (day, label) in WeekdayLabels())
        {
            WeekdayToggles.Add(new DutyWeekdayToggle
            {
                Day = day,
                Label = label,
                IsChecked = DutyWeekdays.Contains(DutyWeekdays.Workdays, day)
            });
        }

        Reload();
    }

    public ObservableCollection<DutyFixedDayRow> FixedDays { get; } = [];

    public ObservableCollection<DutyWeekdayToggle> WeekdayToggles { get; } = [];

    public ObservableCollection<DutyGroupOption> Groups { get; } = [];

    public ObservableCollection<DutyModeOption> ModeOptions { get; } = [];

    public ObservableCollection<DutyDirectionOption> DirectionOptions { get; } = [];

    public ObservableCollection<DutyPenaltyOption> PenaltyOptions { get; } = [];

    public ObservableCollection<DutyWeekStartOption> WeekStartOptions { get; } = [];

    public ObservableCollection<DutyViewOption> ViewOptions { get; } = [];

    [ObservableProperty] private DutyModeOption? _selectedMode;

    [ObservableProperty] private DutyDirectionOption? _selectedDirection;

    [ObservableProperty] private DutyPenaltyOption? _selectedPenalty;

    [ObservableProperty] private DutyGroupOption? _rotationStartGroup;

    [ObservableProperty] private DateTimeOffset? _rotationStartDate;

    [ObservableProperty] private int _daysPerGroup = 5;

    [ObservableProperty] private bool _skipLegalHolidays = true;

    [ObservableProperty] private bool _workOnMakeupDays = true;

    [ObservableProperty] private bool _showFixedBadge = true;

    [ObservableProperty] private DutyWeekStartOption? _selectedWeekStart;

    [ObservableProperty] private DutyViewOption? _selectedDefaultView;

    [ObservableProperty] private bool _highlightToday = true;

    [ObservableProperty] private bool _showLegend = true;

    [ObservableProperty] private bool _dutyEnabled = true;

    [ObservableProperty] private bool _isFixedMode = true;

    [ObservableProperty] private bool _isRotationMode;

    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>界面偏好（存在 settings.json，自动保存）。</summary>
    public DutySettingsConfig Settings => Config.DutySettings;

    public event Action? RosterRequested;

    public event Action? HolidaysRequested;

    public event Action? GroupsRequested;

    partial void OnSelectedModeChanged(DutyModeOption? value)
    {
        IsFixedMode = value?.Mode != DutyScheduleMode.Rotation;
        IsRotationMode = !IsFixedMode;
    }

    /// <summary>从文档重新读一遍规则。</summary>
    public void Reload()
    {
        _loading = true;
        var schedule = _service.Document.Schedule;

        Groups.Clear();
        foreach (var group in _service.Document.OrderedGroups)
            Groups.Add(new DutyGroupOption { Id = group.Id, Name = group.Name });

        FixedDays.Clear();
        foreach (var (day, label) in WeekdayLabels())
        {
            var row = new DutyFixedDayRow
            {
                Day = day,
                Label = label,
                Groups = [.. Groups]
            };

            if (schedule.FixedWeekdayMap.TryGetValue((int)day, out var groupId))
                row.SelectedGroup = row.Groups.FirstOrDefault(group => group.Id == groupId);

            FixedDays.Add(row);
        }

        SelectedMode = ModeOptions.FirstOrDefault(option => option.Mode == schedule.Mode) ?? ModeOptions[0];
        SelectedDirection = DirectionOptions.FirstOrDefault(option => option.Value == schedule.Rotation.Direction)
                            ?? DirectionOptions[0];
        SelectedPenalty = PenaltyOptions.FirstOrDefault(option => option.Mode == schedule.PenaltyMode)
                          ?? PenaltyOptions[0];
        RotationStartGroup = Groups.FirstOrDefault(group => group.Id == schedule.Rotation.StartGroupId);
        RotationStartDate = schedule.Rotation.StartDate is { } start
            ? new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue))
            : null;
        DaysPerGroup = Math.Clamp(schedule.Rotation.DaysPerGroup, 1, 60);

        SkipLegalHolidays = schedule.SkipLegalHolidays;
        WorkOnMakeupDays = schedule.WorkOnMakeupDays;
        ShowFixedBadge = schedule.ShowFixedBadge;

        foreach (var toggle in WeekdayToggles)
            toggle.IsChecked = DutyWeekdays.Contains(schedule.Weekdays, toggle.Day);

        SelectedWeekStart = WeekStartOptions.FirstOrDefault(option => option.Value == Settings.WeekStart)
                            ?? WeekStartOptions[0];
        SelectedDefaultView = ViewOptions.FirstOrDefault(option => option.Mode == Settings.DefaultView)
                              ?? ViewOptions[0];
        HighlightToday = Settings.HighlightToday;
        ShowLegend = Settings.ShowLegend;
        DutyEnabled = Settings.Enabled;

        _loading = false;
    }

    /// <summary>把界面上的草稿写回排班文档（一次保存，不逐项落盘）。</summary>
    [RelayCommand]
    private void SaveSchedule()
    {
        var mask = DutyWeekdays.FromDays(WeekdayToggles.Where(toggle => toggle.IsChecked).Select(toggle => toggle.Day));
        mask = DutyWeekdays.Normalize(mask);

        var fixedMap = new Dictionary<int, Guid>();
        foreach (var row in FixedDays)
        {
            if (row.SelectedGroup is { } group)
                fixedMap[(int)row.Day] = group.Id;
        }

        _service.UpdateSchedule(schedule =>
        {
            schedule.Mode = SelectedMode?.Mode ?? DutyScheduleMode.FixedWeekday;
            schedule.Weekdays = mask;
            schedule.FixedWeekdayMap = fixedMap;
            schedule.Rotation.Direction = SelectedDirection?.Value ?? 1;
            schedule.Rotation.StartGroupId = RotationStartGroup?.Id;
            schedule.Rotation.StartDate = RotationStartDate is { } date
                ? DateOnly.FromDateTime(date.LocalDateTime)
                : null;
            schedule.Rotation.DaysPerGroup = Math.Clamp(DaysPerGroup, 1, 60);
            schedule.SkipLegalHolidays = SkipLegalHolidays;
            schedule.WorkOnMakeupDays = WorkOnMakeupDays;
            schedule.PenaltyMode = SelectedMode?.Mode == DutyScheduleMode.Rotation
                ? SelectedPenalty?.Mode ?? DutyPenaltyMode.Swallow
                : DutyPenaltyMode.Swallow;
            schedule.ShowFixedBadge = ShowFixedBadge;
        });

        StatusText = "排班规则已保存";
    }

    /// <summary>界面偏好：这些存 settings.json，改完立即生效。</summary>
    [RelayCommand]
    private void SavePreferences()
    {
        Settings.WeekStart = SelectedWeekStart?.Value ?? 1;
        Settings.DefaultView = SelectedDefaultView?.Mode ?? DutyViewMode.Week;
        Settings.HighlightToday = HighlightToday;
        Settings.ShowLegend = ShowLegend;
        Settings.Enabled = DutyEnabled;
        Settings.Normalize();
        StatusText = "界面偏好已保存";
    }

    [RelayCommand]
    private void OpenRoster() => RosterRequested?.Invoke();

    [RelayCommand]
    private void OpenHolidays() => HolidaysRequested?.Invoke();

    [RelayCommand]
    private void OpenGroups() => GroupsRequested?.Invoke();

    public void Attach() => _service.Changed += OnDocumentChanged;

    public void Detach() => _service.Changed -= OnDocumentChanged;

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (_loading)
            return;

        Reload();
    }

    private static IEnumerable<(DayOfWeek Day, string Label)> WeekdayLabels()
    {
        yield return (DayOfWeek.Monday, "周一");
        yield return (DayOfWeek.Tuesday, "周二");
        yield return (DayOfWeek.Wednesday, "周三");
        yield return (DayOfWeek.Thursday, "周四");
        yield return (DayOfWeek.Friday, "周五");
        yield return (DayOfWeek.Saturday, "周六");
        yield return (DayOfWeek.Sunday, "周日");
    }
}
