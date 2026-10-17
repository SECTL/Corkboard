using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Board;
using Corkboard.Core.Homework;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using Corkboard.ViewModels.SettingsPages.Homework;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>备份清单里的一行：文本已经拼好（时间 + 目录名）。</summary>
public sealed record BoardBackupListItem(string DisplayName);

/// <summary>
///     「清理与备份」设置页：到期自动清理的开关与时刻、立即清理、清空作业板（清前自动备份）与备份清单。
///     <para>
///         清理这块是从旧作业板设置页原样搬过来的：配置直接落 <c>settings.json</c> 的
///         <c>board_settings</c>，只有「上次清理」是进程内状态（见
///         <see cref="HomeworkCleanupService.LastResult" />，不落盘，重开应用就回到「还没清理过」）。
///     </para>
///     <para>
///         清空作业板是「先备份、备份成功才清」：备份失败时一条作业都不动，提示用户再试。
///     </para>
/// </summary>
public partial class CleanupSettingsPageViewModel : ViewModelBase
{
    /// <summary>清空作业板之前那份备份的原因标记（写进备份目录的 manifest）。</summary>
    private const string ClearBoardBackupReason = "clear-board";

    private readonly IBoardService _boardService;
    private readonly HomeworkCleanupService _cleanupService;
    private readonly BoardBackupService _backupService;

    public CleanupSettingsPageViewModel(
        MainConfigHandler configHandler,
        IBoardService boardService,
        HomeworkCleanupService cleanupService,
        BoardBackupService backupService)
        : base(configHandler)
    {
        _boardService = boardService;
        _cleanupService = cleanupService;
        _backupService = backupService;

        // 清理时刻先塞进草稿并夹一次：手改过 settings.json 的越界值（比如 25 点）不该继续留着。
        _cleanupTimeDraft = TimeDraft(Settings.CleanupHour, Settings.CleanupMinute);
        CommitCleanup();

        ReloadBackups();
    }

    public IBoardService Board => _boardService;

    public HomeworkCleanupService Cleanup => _cleanupService;

    public BoardBackupService Backups => _backupService;

    public BoardSettingsConfig Settings => Config.BoardSettings;

    #region 自动清理

    /// <summary>清理周期下拉项。按周清理时下面才会多出一个「星期几」。</summary>
    public IReadOnlyList<HomeworkCleanupFrequencyOption> CleanupFrequencyOptions { get; } =
    [
        new(HomeworkCleanupFrequency.Daily, CR.Settings_Board_CleanupFrequency_Daily),
        new(HomeworkCleanupFrequency.Weekly, CR.Settings_Board_CleanupFrequency_Weekly)
    ];

    /// <summary>「星期几」下拉项，序号与 <see cref="DayOfWeek" /> 同序。</summary>
    public IReadOnlyList<HomeworkCleanupWeekdayOption> CleanupWeekdayOptions { get; } =
    [
        new(DayOfWeek.Monday, CR.Board_Weekday_Monday),
        new(DayOfWeek.Tuesday, CR.Board_Weekday_Tuesday),
        new(DayOfWeek.Wednesday, CR.Board_Weekday_Wednesday),
        new(DayOfWeek.Thursday, CR.Board_Weekday_Thursday),
        new(DayOfWeek.Friday, CR.Board_Weekday_Friday),
        new(DayOfWeek.Saturday, CR.Board_Weekday_Saturday),
        new(DayOfWeek.Sunday, CR.Board_Weekday_Sunday)
    ];

    /// <summary>按周清理时才显示「星期几」。</summary>
    public bool IsWeeklyCleanup => Settings.CleanupFrequency == HomeworkCleanupFrequency.Weekly;

    /// <summary>清理时刻的选择器草稿。先存草稿，失焦 / 离开页面时再提交。</summary>
    [ObservableProperty] private TimeSpan? _cleanupTimeDraft;

    /// <summary>
    ///     上一次清理干了什么。<b>只反映这一次进程里的清理</b>：历史结果没有落盘，
    ///     重开应用后这里会回到「还没清理过」。
    /// </summary>
    public string CleanupLastResultText
    {
        get
        {
            if (_cleanupService.LastResult is not { } result)
                return CR.Settings_Board_CleanupNeverRun;

            var text = result.Cleaned == 0
                ? CR.Settings_Board_CleanupResultNothing
                : string.Format(CR.Settings_Board_CleanupResultFormat, result.Cleaned);

            return $"{result.RanAt.LocalDateTime:t} · {text}";
        }
    }

    public HomeworkCleanupFrequencyOption SelectedCleanupFrequencyOption
    {
        get => CleanupFrequencyOptions.FirstOrDefault(option => option.Frequency == Settings.CleanupFrequency)
               ?? CleanupFrequencyOptions[0];
        set
        {
            if (value is null || Settings.CleanupFrequency == value.Frequency)
                return;

            Settings.CleanupFrequency = value.Frequency;
            OnPropertyChanged(nameof(IsWeeklyCleanup));
        }
    }

    public HomeworkCleanupWeekdayOption SelectedCleanupWeekdayOption
    {
        get => CleanupWeekdayOptions.FirstOrDefault(option => option.Weekday == Settings.CleanupWeekday)
               ?? CleanupWeekdayOptions[0];
        set
        {
            if (value is not null)
                Settings.CleanupWeekday = value.Weekday;
        }
    }

    /// <summary>
    ///     提交清理时刻。手输 / 选出来的越界值在这里夹回合法区间，并把夹过的值同步回界面，
    ///     免得显示的和落盘的不是一个数。按 24 小时制存成「时 + 分」两个整数。
    /// </summary>
    public void CommitCleanup()
    {
        var (hour, minute) = ResolveTime(CleanupTimeDraft, Settings.CleanupHour, Settings.CleanupMinute);
        Settings.CleanupHour = HomeworkCleanupDefaults.ClampHour(hour);
        Settings.CleanupMinute = HomeworkCleanupDefaults.ClampMinute(minute);

        CleanupTimeDraft = TimeDraft(Settings.CleanupHour, Settings.CleanupMinute);
    }

    /// <summary>时间选择器的草稿 → 时与分；选择器被清空（<c>null</c>）就退回原值，而不是写 0 点。</summary>
    private static (int Hour, int Minute) ResolveTime(TimeSpan? draft, int fallbackHour, int fallbackMinute)
        => draft is { } value ? (value.Hours, value.Minutes) : (fallbackHour, fallbackMinute);

    private static TimeSpan TimeDraft(int hour, int minute) => new(hour, minute, 0);

    /// <summary>
    ///     用户点了「立即清理」：不看时刻，截止日期过了的作业当场清掉。
    ///     先把界面上的清理时刻提交掉，免得草稿还挂在输入框里没落盘。
    /// </summary>
    [RelayCommand]
    private void RunCleanupNow()
    {
        CommitCleanup();

        _cleanupService.Run(DateTimeOffset.Now);

        // 板子上的列表重建由 IBoardService.Changed 驱动；这里只管自己那行结果文字。
        OnPropertyChanged(nameof(CleanupLastResultText));
    }

    #endregion

    #region 清空作业板与备份清单

    /// <summary>备份清单，新的在前。文本在这里拼好，界面只负责显示。</summary>
    public ObservableCollection<BoardBackupListItem> BackupItems { get; } = [];

    public bool HasBackups => BackupItems.Count > 0;

    /// <summary>重新读一遍备份目录，刷新列表（进页面、清空之后、点刷新按钮都会调）。</summary>
    public void ReloadBackups()
    {
        BackupItems.Clear();

        foreach (var entry in _backupService.ListBackups())
        {
            BackupItems.Add(new BoardBackupListItem(string.Format(
                CR.Settings_Board_BackupItemFormat,
                entry.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Path.GetFileName(entry.Directory))));
        }

        OnPropertyChanged(nameof(HasBackups));
    }

    /// <summary>刷新按钮：备份是用户能从外面（资源管理器）改的，所以给一个手动重读的入口。</summary>
    [RelayCommand]
    private void RefreshBackupList() => ReloadBackups();

    /// <summary>
    ///     清空作业板：先备份，备份成功才真的清。备份失败时原样返回失败结果，
    ///     板子上的作业一条都不动（页面据此只弹一句失败提示）。
    /// </summary>
    public async Task<BoardBackupResult> ClearBoardAsync(CancellationToken cancellationToken = default)
    {
        var backup = await _backupService.CreateBackupAsync(ClearBoardBackupReason, cancellationToken);
        if (!backup.Success)
            return backup;

        _boardService.ClearAll();
        ReloadBackups();
        return backup;
    }

    #endregion
}
