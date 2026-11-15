using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.ControlCenter;
using Corkboard.Core.Models.SubConfigs.ControlCenter;
using Corkboard.Core.Services.Config;
using Corkboard.Services.ControlCenter;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>「同步最近多少天作业」下拉里的一项。</summary>
public sealed record HomeworkBackupDayOption(int Days, string DisplayName);

/// <summary>「同步最近多少场考试」下拉里的一项。</summary>
public sealed record ExamSyncRecentOption(int Count, string DisplayName);

/// <summary>
///     「作业板 → 自动化与数据」设置页的 ViewModel：自动清理、历史与回放、自动保存、同步到集控、数据维护。
///     <para>
///         每一块的逻辑都不在这一页里——它们各自还是自己的 ViewModel（清理、历史、自动保存、集控服务），
///         这一页只负责把它们摆到一起，所以这里只暴露几个子 VM 与集控那几项的包装属性。
///     </para>
/// </summary>
public partial class BoardAutomationSettingsPageViewModel(
    MainConfigHandler configHandler,
    CleanupSettingsPageViewModel cleanup,
    HistorySettingsPageViewModel history,
    AutoSaveSettingsPageViewModel autoSave,
    ControlCenterHostedService controlService) : ViewModelBase(configHandler)
{
    /// <summary>自动清理与数据维护（清空作业板、立即清理）的子 VM。页面绑定加 <c>Cleanup.</c> 前缀。</summary>
    public CleanupSettingsPageViewModel Cleanup { get; } = cleanup;

    /// <summary>历史与回放（保存粒度、保留天数、容量上限、占用与立即清理）的子 VM。绑定加 <c>History.</c> 前缀。</summary>
    public HistorySettingsPageViewModel History { get; } = history;

    /// <summary>自动保存（开关、保存时机、保留天数、保存位置、立即保存一张）的子 VM。绑定加 <c>AutoSave.</c> 前缀。</summary>
    public AutoSaveSettingsPageViewModel AutoSave { get; } = autoSave;

    /// <summary>集控相关的配置（开关、天数、考试同步）。绑定直接写 <c>Config.ControlCenter.Xxx</c>。</summary>
    public ControlCenterSettingsConfig ControlCenter => Config.ControlCenter;

    /// <summary>「同步最近多少天作业」的可选值：1、3、7、14、30 天。</summary>
    public IReadOnlyList<HomeworkBackupDayOption> HomeworkBackupDayOptions { get; } =
        new[] { 1, 3, 7, 14, 30 }
            .Where(days => days is >= ControlCenterSettingsConfig.MinHomeworkBackupDays
                and <= ControlCenterSettingsConfig.MaxHomeworkBackupDays)
            .Select(days => new HomeworkBackupDayOption(
                days,
                string.Format(CultureInfo.CurrentCulture, CR.Settings_Board_HomeworkBackupDaysOption, days)))
            .ToList();

    /// <summary>「同步最近多少场考试」的可选值：5、10、20、30、50 场。</summary>
    public IReadOnlyList<ExamSyncRecentOption> ExamSyncRecentOptions { get; } =
        new[] { 5, 10, 20, 30, 50 }
            .Where(count => count is >= ControlCenterSettingsConfig.MinExamSyncRecentCount
                and <= ControlCenterSettingsConfig.MaxExamSyncRecentCount)
            .Select(count => new ExamSyncRecentOption(
                count,
                string.Format(CultureInfo.CurrentCulture, CR.Settings_Board_ExamSyncRecentOption, count)))
            .ToList();

    /// <summary>下拉选中的天数；改了就直接落进配置（配置 handler 自己会落盘）。</summary>
    [ObservableProperty]
    private HomeworkBackupDayOption? _selectedHomeworkBackupDayOption;

    /// <summary>下拉选中的场数。</summary>
    [ObservableProperty]
    private ExamSyncRecentOption? _selectedExamSyncRecentOption;

    /// <summary>「最近备份」那一行：成功时是本地时间，没成功过 / 失败时给对应文案。</summary>
    [ObservableProperty]
    private string _homeworkBackupStatusText = string.Empty;

    /// <summary>页面出现时开始听集控服务的备份状态，并把下拉对齐到当前配置。</summary>
    public void Attach()
    {
        controlService.HomeworkBackupChanged -= OnHomeworkBackupChanged;
        controlService.HomeworkBackupChanged += OnHomeworkBackupChanged;
        ApplyHomeworkBackup(controlService.HomeworkBackup);
        SyncSelectionFromConfig();
    }

    /// <summary>页面离开时断开：集控服务是单例，transient 的 VM 挂着它会被一起留住。</summary>
    public void Detach() => controlService.HomeworkBackupChanged -= OnHomeworkBackupChanged;

    partial void OnSelectedHomeworkBackupDayOptionChanged(HomeworkBackupDayOption? value)
    {
        if (value is not null && ControlCenter.HomeworkBackupDays != value.Days)
            ControlCenter.HomeworkBackupDays = value.Days;
    }

    partial void OnSelectedExamSyncRecentOptionChanged(ExamSyncRecentOption? value)
    {
        if (value is not null && ControlCenter.ExamSyncRecentCount != value.Count)
            ControlCenter.ExamSyncRecentCount = value.Count;
    }

    private void SyncSelectionFromConfig()
    {
        SelectedHomeworkBackupDayOption =
            HomeworkBackupDayOptions.FirstOrDefault(option => option.Days == ControlCenter.HomeworkBackupDays)
            ?? HomeworkBackupDayOptions[0];

        SelectedExamSyncRecentOption =
            ExamSyncRecentOptions.FirstOrDefault(option => option.Count == ControlCenter.ExamSyncRecentCount)
            ?? ExamSyncRecentOptions[0];
    }

    private void OnHomeworkBackupChanged(object? sender, ControlHomeworkBackupState state)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ApplyHomeworkBackup(state);
        else
            Dispatcher.UIThread.Post(() => ApplyHomeworkBackup(state));
    }

    /// <summary>状态行：最近一次成功的本地时间 / 上次失败 / 还没备份过。</summary>
    private void ApplyHomeworkBackup(ControlHomeworkBackupState state)
    {
        HomeworkBackupStatusText = state.LastSuccessAt is { } successAt
            ? string.Format(
                CultureInfo.CurrentCulture,
                CR.Settings_Control_HomeworkBackup_StatusFormat,
                successAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture))
            : state.LastErrorKey is not null
                ? CR.Settings_Control_HomeworkBackup_Failed
                : CR.Settings_Control_HomeworkBackup_Never;
    }
}