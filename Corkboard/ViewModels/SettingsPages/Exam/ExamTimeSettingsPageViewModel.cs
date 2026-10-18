using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Time;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Exam;

/// <summary>
///     「时间同步」设置页：NTP 开关、同步源清单、间隔与超时、立即同步，以及当前校时状态。
///     <para>
///         看板上的时钟要准到「还有几分钟开考」这种粒度，本机时间飘了就会误事；
///         同步失败不影响使用，<see cref="NtpTimeService" /> 会保留上次的偏差，
///         从没成功过就退回本机时间。
///     </para>
/// </summary>
public partial class ExamTimeSettingsPageViewModel : ViewModelBase
{
    private readonly NtpTimeService _timeService;
    private bool _isAttached;

    public ExamTimeSettingsPageViewModel(MainConfigHandler configHandler, NtpTimeService timeService)
        : base(configHandler)
    {
        _timeService = timeService;
        ResetDrafts();
    }

    public NtpSettingsConfig Settings => Config.Ntp;

    /// <summary>校时服务：状态文案与「立即同步」都走它。</summary>
    public NtpTimeService Time => _timeService;

    /// <summary>同步源清单（配置里那个集合，增删都经 <see cref="NtpSettingsConfig.ApplyServers" />）。</summary>
    private ObservableCollection<string> Servers => Settings.Servers;

    /// <summary>给界面用的同步源清单（界面上要绑一个带类型的对象，不能直接绑字符串）。</summary>
    public ObservableCollection<NtpServerItem> ServerItems { get; } = [];

    #region 源清单

    /// <summary>「添加源」输入框的草稿。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddServer))]
    private string _serverDraft = string.Empty;

    /// <summary>输入框里这一条能不能加（非空、像主机名、没超上限、没重复）。</summary>
    public bool CanAddServer =>
        Servers.Count < NtpServerCatalog.MaxServers
        && !string.IsNullOrWhiteSpace(ServerDraft)
        && NtpServerCatalog.IsValidHost(ServerDraft.Trim())
        && !Servers.Contains(ServerDraft.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>清单是不是空的（正常情况下永远不会空：空表会被填回默认源）。</summary>
    public bool IsServerListEmpty => Servers.Count == 0;

    /// <summary>点「添加」：把输入框里的地址接到清单末尾。</summary>
    [RelayCommand]
    private void AddServer()
    {
        var server = (ServerDraft ?? string.Empty).Trim();
        if (!CanAddServer)
            return;

        var list = Servers.ToList();
        list.Add(server);
        Settings.ApplyServers(list);
        ServerDraft = string.Empty;
        RefreshServerListState();
    }

    /// <summary>删掉清单里的某一条。</summary>
    public void RemoveServer(NtpServerItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Address))
            return;

        Settings.ApplyServers(Servers.Where(server =>
            !string.Equals(server, item.Address, StringComparison.OrdinalIgnoreCase)));
        RefreshServerListState();
    }

    /// <summary>「恢复默认源」：换回内置的那几个国内公共 NTP 服务。</summary>
    public void ResetServers()
    {
        Settings.ApplyServers(null);
        RefreshServerListState();
    }

    #endregion

    #region 间隔与超时

    /// <summary>同步间隔草稿（分钟）。</summary>
    [ObservableProperty]
    private decimal? _intervalDraft;

    /// <summary>单源超时草稿（毫秒）。</summary>
    [ObservableProperty]
    private decimal? _timeoutDraft;

    #endregion

    #region 状态

    /// <summary>状态那一行：正在同步 / 已通过某源校时 / 同步失败 / 还没同步过 / 校时关着。</summary>
    public string StatusText
    {
        get
        {
            if (!Settings.Enabled)
                return CR.Settings_Exam_Time_Status_Disabled;

            if (_timeService.IsSyncing)
                return CR.Settings_Exam_Time_Syncing;

            if (_timeService.LastResult is { } result)
            {
                if (!result.Succeeded)
                    return string.Format(CR.Settings_Exam_Time_Status_Failed, result.Error ?? string.Empty);

                var milliseconds = (int)Math.Round(Math.Abs(result.Offset.TotalMilliseconds));
                return string.Format(CR.Settings_Exam_Time_Status_Success, result.Server, milliseconds);
            }

            return CR.Settings_Exam_Time_Status_Idle;
        }
    }

    /// <summary>本机时间比标准时间快还是慢（校时成功过才有意义）。</summary>
    public string OffsetText
    {
        get
        {
            var milliseconds = (int)Math.Round(Math.Abs(_timeService.Offset.TotalMilliseconds));
            if (milliseconds < 50)
                return CR.Settings_Exam_Time_Offset_Same;

            var amount = $"{milliseconds} {CR.Common_Milliseconds}";
            return string.Format(
                _timeService.Offset > TimeSpan.Zero ? CR.Settings_Exam_Time_Offset_Slow : CR.Settings_Exam_Time_Offset_Fast,
                amount);
        }
    }

    /// <summary>校时成功过才显示偏差那一行（从没成功过就只说「还没同步过」）。</summary>
    public bool HasOffset => Settings.Enabled && _timeService.LastSuccessAt is not null;

    /// <summary>「上次同步 12:15」。</summary>
    public string LastSyncText => _timeService.LastSuccessAt is { } at
        ? string.Format(CR.Settings_Exam_Time_Status_LastSync, at.LocalDateTime.ToString("t"))
        : string.Empty;

    public bool HasLastSync => _timeService.LastSuccessAt is not null;

    #endregion

    /// <summary>同步中、或者校时开关关着时，「立即同步」不可点。</summary>
    private bool CanSyncNow() => Settings.Enabled && !_timeService.IsSyncing;

    /// <summary>挂上校时服务与配置的变化订阅（页面进入可视树时调）。</summary>
    public void Attach()
    {
        if (_isAttached)
            return;

        _isAttached = true;
        _timeService.StatusChanged += OnTimeStatusChanged;
        Settings.PropertyChanged += OnSettingsChanged;
        ResetDrafts();
    }

    /// <summary>断开订阅（页面离开可视树时调）。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        _timeService.StatusChanged -= OnTimeStatusChanged;
        Settings.PropertyChanged -= OnSettingsChanged;
    }

    /// <summary>提交间隔与超时草稿（失焦 / 离开页面时调）。</summary>
    public void CommitAll()
    {
        var interval = IntervalDraft is { } intervalDraft
            ? NtpSettingsConfig.ClampIntervalMinutes((int)intervalDraft)
            : NtpSettingsConfig.ClampIntervalMinutes(Settings.SyncIntervalMinutes);

        if (Settings.SyncIntervalMinutes != interval)
            Settings.SyncIntervalMinutes = interval;

        var timeout = TimeoutDraft is { } timeoutDraft
            ? NtpSettingsConfig.ClampTimeoutMilliseconds((int)timeoutDraft)
            : NtpSettingsConfig.ClampTimeoutMilliseconds(Settings.TimeoutMilliseconds);

        if (Settings.TimeoutMilliseconds != timeout)
            Settings.TimeoutMilliseconds = timeout;

        // 夹取过的值同步回界面，免得显示的和落盘的不是一个数。
        IntervalDraft = interval;
        TimeoutDraft = timeout;
    }

    /// <summary>「立即同步」：不等间隔到点，现在就试一遍所有源。</summary>
    [RelayCommand(CanExecute = nameof(CanSyncNow))]
    private async Task SyncNowAsync()
    {
        CommitAll();

        // 开关是关着的时候同步没有意义（结果也不会被采用），按钮本身已经禁用了。
        if (!Settings.Enabled)
            return;

        await _timeService.SyncNowAsync();
        RaiseStatusChanged();
    }

    private void ResetDrafts()
    {
        IntervalDraft = NtpSettingsConfig.ClampIntervalMinutes(Settings.SyncIntervalMinutes);
        TimeoutDraft = NtpSettingsConfig.ClampTimeoutMilliseconds(Settings.TimeoutMilliseconds);
        RefreshServerListState();
        RaiseStatusChanged();
    }

    private void RefreshServerListState()
    {
        ServerItems.Clear();
        foreach (var server in Servers)
            ServerItems.Add(new NtpServerItem(server));

        OnPropertyChanged(nameof(IsServerListEmpty));
        OnPropertyChanged(nameof(CanAddServer));
        AddServerCommand.NotifyCanExecuteChanged();
    }

    /// <summary>状态相关的一批属性一起通知（它们都由校时服务的当前状态推出来）。</summary>
    private void RaiseStatusChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(OffsetText));
        OnPropertyChanged(nameof(HasOffset));
        OnPropertyChanged(nameof(LastSyncText));
        OnPropertyChanged(nameof(HasLastSync));
        SyncNowCommand.NotifyCanExecuteChanged();
    }

    private void OnTimeStatusChanged(object? sender, EventArgs e) => RaiseStatusChanged();

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null or nameof(NtpSettingsConfig.Enabled):
                RaiseStatusChanged();
                break;
            case nameof(NtpSettingsConfig.Servers):
                RefreshServerListState();
                break;
            case nameof(NtpSettingsConfig.SyncIntervalMinutes):
                IntervalDraft = NtpSettingsConfig.ClampIntervalMinutes(Settings.SyncIntervalMinutes);
                break;
            case nameof(NtpSettingsConfig.TimeoutMilliseconds):
                TimeoutDraft = NtpSettingsConfig.ClampTimeoutMilliseconds(Settings.TimeoutMilliseconds);
                break;
        }
    }
}

/// <summary>同步源清单里的一行（界面上要绑一个带类型的对象，不能直接绑字符串）。</summary>
public sealed record NtpServerItem(string Address)
{
    /// <summary>列表里显示的文字就是主机名本身。</summary>
    public string DisplayName => Address;
}
