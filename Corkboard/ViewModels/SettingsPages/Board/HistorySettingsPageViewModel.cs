using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Board.History;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>
///     「历史与回放」设置页：历史记多密、留多久、最多占多少空间，以及现在占了多少、要不要马上清一次。
///     <para>
///         三个输入都是草稿式的：<c>NumericUpDown</c> 每动一下就会发一次变更，直接双向绑配置等于每动一格
///         写一遍 settings.json，所以先落在草稿上，失焦 / 离开页面再提交（见页面 code-behind）。
///         提交时一律过一遍 <see cref="BoardHistoryPolicy" /> 的夹取，并把夹过的值写回界面，
///         免得显示的和落盘的不是一个数。
///     </para>
///     <para>
///         真正把这三个值同步进存储层的是 <c>BoardHistoryHostedService</c>（它订阅配置变化），
///         这里只负责改配置；「立即清理」与占用统计直接问 <see cref="INoteHistoryStore" />。
///     </para>
/// </summary>
public partial class HistorySettingsPageViewModel : ViewModelBase
{
    private readonly INoteHistoryStore _historyStore;

    public HistorySettingsPageViewModel(MainConfigHandler configHandler, INoteHistoryStore historyStore)
        : base(configHandler)
    {
        _historyStore = historyStore;

        // 手改过 settings.json 的越界值先夹一次：界面显示的就是真正生效的那个数。
        _coalesceMinutesDraft = BoardHistoryPolicy.ClampCoalesceMinutes(Settings.HistoryCoalesceMinutes);
        _retentionDaysDraft = BoardHistoryPolicy.ClampRetentionDays(Settings.HistoryRetentionDays);
        _maxSizeMbDraft = BoardHistoryPolicy.ClampMaxSizeMb(Settings.HistoryMaxSizeMb);
        CommitAll();

        RefreshStatistics();
    }

    public BoardSettingsConfig Settings => Config.BoardSettings;

    #region 输入范围

    // 数值框的上下限与 BoardHistoryPolicy 同源：不要在 XAML 里另写一套数字，改了政策这边跟着变。
    public decimal MinCoalesceMinutes => BoardHistoryPolicy.MinCoalesceMinutes;

    public decimal MaxCoalesceMinutes => BoardHistoryPolicy.MaxCoalesceMinutes;

    public decimal MinRetentionDays => BoardHistoryPolicy.MinRetentionDays;

    public decimal MaxRetentionDays => BoardHistoryPolicy.MaxRetentionDays;

    public decimal MinMaxSizeMb => BoardHistoryPolicy.MinMaxSizeMb;

    public decimal MaxMaxSizeMb => BoardHistoryPolicy.MaxMaxSizeMb;

    #endregion

    #region 三个设置项（草稿）

    /// <summary>合并窗口的草稿（分钟）：两次保存挨得比它近，历史里只留一版。</summary>
    [ObservableProperty] private decimal? _coalesceMinutesDraft;

    /// <summary>保留天数的草稿。</summary>
    [ObservableProperty] private decimal? _retentionDaysDraft;

    /// <summary>容量上限的草稿（MB）。</summary>
    [ObservableProperty] private decimal? _maxSizeMbDraft;

    /// <summary>
    ///     提交三个草稿。夹过的值同步回界面，界面显示的永远是真正生效的那个数。
    ///     <para>数值框失焦、离开页面、点「立即清理历史」时都会调。</para>
    /// </summary>
    public void CommitAll()
    {
        var coalesceMinutes = BoardHistoryPolicy.ClampCoalesceMinutes(
            ToInt(CoalesceMinutesDraft, Settings.HistoryCoalesceMinutes));
        if (Settings.HistoryCoalesceMinutes != coalesceMinutes)
            Settings.HistoryCoalesceMinutes = coalesceMinutes;
        if (CoalesceMinutesDraft != (decimal)coalesceMinutes)
            CoalesceMinutesDraft = coalesceMinutes;

        var retentionDays = BoardHistoryPolicy.ClampRetentionDays(
            ToInt(RetentionDaysDraft, Settings.HistoryRetentionDays));
        if (Settings.HistoryRetentionDays != retentionDays)
            Settings.HistoryRetentionDays = retentionDays;
        if (RetentionDaysDraft != (decimal)retentionDays)
            RetentionDaysDraft = retentionDays;

        var maxSizeMb = BoardHistoryPolicy.ClampMaxSizeMb(
            ToInt(MaxSizeMbDraft, Settings.HistoryMaxSizeMb));
        if (Settings.HistoryMaxSizeMb != maxSizeMb)
            Settings.HistoryMaxSizeMb = maxSizeMb;
        if (MaxSizeMbDraft != (decimal)maxSizeMb)
            MaxSizeMbDraft = maxSizeMb;
    }

    /// <summary>数值框被清空（<c>null</c>）时退回配置里的原值，而不是当成 0 写下去。</summary>
    private static int ToInt(decimal? draft, int fallback)
        => draft is { } value ? (int)Math.Round(value, MidpointRounding.AwayFromZero) : fallback;

    #endregion

    #region 占用统计与立即清理

    /// <summary>历史里有多少条事件。</summary>
    [ObservableProperty] private string _entryCountText = "0";

    /// <summary>历史里存了多少份去重后的内容快照。</summary>
    [ObservableProperty] private string _snapshotCountText = "0";

    /// <summary>历史总共占多少空间，已经换算成人看的单位。</summary>
    [ObservableProperty] private string _sizeText = "0 B";

    /// <summary>上一次「立即清理」删掉了多少天；没点过时为空，那一行不显示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPruneResult))]
    private string? _pruneResultText;

    /// <summary>点过「立即清理」之后才显示结果那一行。</summary>
    public bool HasPruneResult => !string.IsNullOrEmpty(PruneResultText);

    /// <summary>进页面 / 清完之后重读一次占用（存储层会自己扫日志与快照目录）。</summary>
    public void RefreshStatistics()
    {
        var statistics = _historyStore.GetStatistics();
        EntryCountText = statistics.EntryCount.ToString(CultureInfo.CurrentCulture);
        SnapshotCountText = statistics.SnapshotCount.ToString(CultureInfo.CurrentCulture);
        SizeText = FormatSize(statistics.Bytes);
    }

    /// <summary>
    ///     用户点了「立即清理历史」：先把界面上还没提交的输入提交掉（清理按最新设置算），
    ///     再让存储层按保留天数与容量上限删一遍，最后刷新占用。
    /// </summary>
    [RelayCommand]
    private void PruneHistory()
    {
        CommitAll();

        var removed = _historyStore.Prune();
        RefreshStatistics();

        PruneResultText = removed == 0
            ? CR.Settings_Board_History_PruneNothing
            : string.Format(CR.Settings_Board_History_PruneResultFormat, removed);
    }

    /// <summary>字节数换成人看的单位；历史大多在几 MB 量级，超过 MB 仍按 MB 显示。</summary>
    private static string FormatSize(long bytes)
    {
        const double kilo = 1024;
        const double mega = kilo * kilo;

        return bytes switch
        {
            < (long)kilo => $"{bytes} B",
            < (long)mega => $"{bytes / kilo:0.#} KB",
            _ => $"{bytes / mega:0.#} MB"
        };
    }

    #endregion
}
