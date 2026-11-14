using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.ControlCenter;
using Corkboard.Core.Services.Config;
using Corkboard.Services.ControlCenter;
using Corkboard.Services.Desktop;
using Corkboard.Services.Ui;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Control;

/// <summary>班下拉里的一项：<see cref="DisplayName" /> 是「年级名 · 班级名」。</summary>
public sealed record ControlClassOption(string Id, string DisplayName);

/// <summary>
///     「设置 → 集控」页的 ViewModel：开关、连接状态、设备信息、接入与班级占用。
///     <para>
///         连接本身由 <see cref="ControlCenterHostedService" />（单例后台服务）负责：它监听配置、
///         自己重连、自己把接入结果弹成 toast。这一页只做三件事——把草稿写回配置、
///         调服务的方法、把服务的最新快照画出来。所以这里**不调** <c>RestartAsync</c>，
///         也**不重复**弹接入成功 / 失败的提示。
///     </para>
///     <para>
///         状态行与详情行的文案走 <see cref="ControlNodeLinkState.StatusKey" /> /
///         <see cref="ControlNodeLinkState.DetailKey" />（Core 只给键不给中文），
///         找不到对应资源时退回 <c>Settings_Control_Status_Unknown</c>。
///     </para>
///     <para>
///         页面是 transient、服务是单例，所以订阅 <see cref="ControlCenterHostedService.SnapshotChanged" />
///         必须在 <see cref="Detach" /> 里退订，否则每开一次页面就多挂一个处理器。
///     </para>
/// </summary>
public partial class ControlCenterSettingsPageViewModel(
    MainConfigHandler configHandler,
    ControlCenterHostedService controlService,
    IToastService toastService,
    IExternalLauncher launcher) : ViewModelBase(configHandler)
{
    /// <summary>网页控制台路径（拼在「集控站点」后面：私有部署时打开的是自己那台）。</summary>
    private const string ConsolePath = "/dashboard/control";

    /// <summary>设备 ID / 地点没接入时的占位符。</summary>
    private const string EmptyValue = "—";

    /// <summary>快照里当前占着的班（<c>null</c> = 没选班）。</summary>
    private string? _currentClassId;

    /// <summary>上一次快照是否连着；从断线恢复时补拉一次班级清单。</summary>
    private bool _wasConnected;

    // ---------------------------------------------------------------------------------------
    // 草稿：文本框只绑这些，失焦 / 离开页面时才写回 Config.ControlCenter（见 CommitAll）。
    // ---------------------------------------------------------------------------------------

    [ObservableProperty] private string _displayNameDraft = string.Empty;

    [ObservableProperty] private string _serverUrlDraft = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnroll))]
    [NotifyCanExecuteChangedFor(nameof(EnrollCommand))]
    private string _siteIdDraft = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnroll))]
    [NotifyPropertyChangedFor(nameof(EnrollButtonText))]
    [NotifyCanExecuteChangedFor(nameof(EnrollCommand))]
    private string _pairCodeDraft = string.Empty;

    // ---------------------------------------------------------------------------------------
    // 展示：状态由服务推送的快照驱动，页面不手动刷新。
    // ---------------------------------------------------------------------------------------

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private string _detailText = string.Empty;

    [ObservableProperty] private string _deviceIdText = EmptyValue;

    [ObservableProperty] private string _siteNameText = EmptyValue;

    [ObservableProperty] private string _currentClassText = string.Empty;

    /// <summary>「最近备份」那一行：成功时是本地时间，没成功过 / 失败时给对应文案。</summary>
    [ObservableProperty] private string _homeworkBackupStatusText = string.Empty;

    [ObservableProperty] private ObservableCollection<ControlClassOption> _classOptions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectClass))]
    [NotifyCanExecuteChangedFor(nameof(SelectClassCommand))]
    private ControlClassOption? _selectedClassOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnroll))]
    [NotifyPropertyChangedFor(nameof(CanForget))]
    [NotifyPropertyChangedFor(nameof(CanSelectClass))]
    [NotifyPropertyChangedFor(nameof(CanReleaseClass))]
    [NotifyPropertyChangedFor(nameof(EnrollButtonText))]
    [NotifyCanExecuteChangedFor(nameof(EnrollCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForgetCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectClassCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReleaseClassCommand))]
    private bool _isEnrolling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanForget))]
    [NotifyPropertyChangedFor(nameof(CanSelectClass))]
    [NotifyPropertyChangedFor(nameof(CanReleaseClass))]
    [NotifyCanExecuteChangedFor(nameof(ForgetCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectClassCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReleaseClassCommand))]
    private bool _isConfigured;

    /// <summary>「接入」按钮是否可用：两个输入都非空且没有正在接入。</summary>
    public bool CanEnroll => CanEnrollCore();

    /// <summary>「取消接入」按钮是否可用：已接入且没有正在接入。</summary>
    public bool CanForget => !IsEnrolling && IsConfigured;

    /// <summary>「占用」按钮是否可用：选中了班、已接入、没有正在接入。</summary>
    public bool CanSelectClass => !IsEnrolling && IsConfigured && SelectedClassOption is not null;

    /// <summary>「释放」按钮是否可用：当前占着班、已接入、没有正在接入。</summary>
    public bool CanReleaseClass => !IsEnrolling && IsConfigured && HasCurrentClass;

    /// <summary>本机当前是否占着班（「释放」按钮的判据）。</summary>
    public bool HasCurrentClass => _currentClassId is not null;

    /// <summary>接入按钮的文字：执行中换成「正在接入」。</summary>
    public string EnrollButtonText =>
        IsEnrolling ? CR.Settings_Control_Enrolling : CR.Settings_Control_Enroll;

    // ---------------------------------------------------------------------------------------
    // 生命周期
    // ---------------------------------------------------------------------------------------

    /// <summary>
    ///     页面进入可视树：用配置初始化草稿、订阅服务快照、把当前状态画出来，并在需要时拉一次班级清单。
    /// </summary>
    public void Attach()
    {
        var settings = Config.ControlCenter;
        DisplayNameDraft = settings.DisplayName;
        ServerUrlDraft = settings.ServerUrl;
        SiteIdDraft = settings.SiteId;
        PairCodeDraft = settings.PairCode;

        // 先减后加，防止同一个 VM 被重复 Attach 时挂两遍。
        controlService.SnapshotChanged -= OnSnapshotChanged;
        controlService.SnapshotChanged += OnSnapshotChanged;
        controlService.HomeworkBackupChanged -= OnHomeworkBackupChanged;
        controlService.HomeworkBackupChanged += OnHomeworkBackupChanged;

        _wasConnected = controlService.Snapshot.IsConnected;
        ApplySnapshot(controlService.Snapshot);
        ApplyHomeworkBackup(controlService.HomeworkBackup);

        // 班级清单只在「已接入且还没有缓存」时补拉一次；失败静默，不影响页面其余部分。
        if (controlService.IsConfigured && ClassOptions.Count == 0)
            _ = RefreshClassesAsync(silent: true);
    }

    /// <summary>页面离开可视树：断掉对单例服务的订阅（必须，见类注释）。</summary>
    public void Detach()
    {
        controlService.SnapshotChanged -= OnSnapshotChanged;
        controlService.HomeworkBackupChanged -= OnHomeworkBackupChanged;
    }

    // ---------------------------------------------------------------------------------------
    // 提交
    // ---------------------------------------------------------------------------------------

    /// <summary>
    ///     把四个草稿写回 <see cref="Config.ControlCenter" />。
    ///     <para>
    ///         只在值真的变了时写：<c>ConfigHandlerBase</c> 每次属性变化都会落一次盘，
    ///         每次失焦都无脑赋值等于把 <c>settings.json</c> 重写一遍。
    ///         <c>ServerUrl</c> 留空就原样写空串，由 <c>ControlCenterSettingsConfig.Normalize</c>
    ///         在下次读盘时归一成默认地址。
    ///     </para>
    /// </summary>
    public void CommitAll()
    {
        var settings = Config.ControlCenter;

        var displayName = (DisplayNameDraft ?? string.Empty).Trim();
        if (settings.DisplayName != displayName)
            settings.DisplayName = displayName;

        var serverUrl = (ServerUrlDraft ?? string.Empty).Trim();
        if (settings.ServerUrl != serverUrl)
            settings.ServerUrl = serverUrl;

        var siteId = (SiteIdDraft ?? string.Empty).Trim();
        if (settings.SiteId != siteId)
            settings.SiteId = siteId;

        var pairCode = (PairCodeDraft ?? string.Empty).Trim();
        if (settings.PairCode != pairCode)
            settings.PairCode = pairCode;
    }

    // ---------------------------------------------------------------------------------------
    // 命令
    // ---------------------------------------------------------------------------------------

    /// <summary>用系统浏览器打开集控网页控制台（地址跟着当前的「集控站点」走）。</summary>
    [RelayCommand]
    private void OpenConsole()
    {
        var baseUrl = (ServerUrlDraft ?? string.Empty).Trim().TrimEnd('/');
        if (baseUrl.Length == 0)
            baseUrl = (Config.ControlCenter.ServerUrl ?? string.Empty).Trim().TrimEnd('/');
        if (baseUrl.Length == 0)
            baseUrl = "https://sectl.cn";

        if (!launcher.TryOpenUri(baseUrl + ConsolePath))
            toastService.Show(CR.Settings_Control_Error_Unknown, ToastSeverity.Error);
    }

    /// <summary>
    ///     接入集控：地点 ID 可以整句粘贴（Core 会从整句里解析），所以原样传草稿即可。
    ///     结果提示由服务自己弹，这里只负责清掉一次性配对码并刷新界面。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEnrollCore))]
    private async Task EnrollAsync()
    {
        CommitAll();
        IsEnrolling = true;

        try
        {
            await controlService.EnrollAsync(SiteIdDraft, PairCodeDraft);

            // 配对码是一次性的，接完立刻从草稿里清掉（服务的成功路径也会把配置里的写空）。
            PairCodeDraft = string.Empty;

            // 接入成功后服务手里是归一化过的地点 ID（整句粘贴时配置里可能还是一整句），优先用它。
            var siteId = FirstNotEmpty(controlService.SiteId, Config.ControlCenter.SiteId);
            if (siteId is not null)
                SiteIdDraft = siteId;

            ApplySnapshot(controlService.Snapshot);
            await RefreshClassesAsync();
        }
        catch (Exception)
        {
            // 正常的接入成功 / 失败由服务弹提示；这里只兜服务没接住的异常，避免任务把界面带崩。
            toastService.Show(CR.Settings_Control_Error_Unknown, ToastSeverity.Error);
        }
        finally
        {
            IsEnrolling = false;
        }
    }

    /// <summary>取消本机接入（确认框在页面 code-behind 里，确认后才执行这个命令）。</summary>
    [RelayCommand(CanExecute = nameof(CanForgetCore))]
    private async Task ForgetAsync()
    {
        try
        {
            await controlService.ForgetAsync();

            PairCodeDraft = string.Empty;
            SelectedClassOption = null;
            SiteIdDraft = Config.ControlCenter.SiteId;

            ApplySnapshot(controlService.Snapshot);
        }
        catch (Exception)
        {
            toastService.Show(CR.Settings_Control_Error_Unknown, ToastSeverity.Error);
        }
    }

    /// <summary>占用选中的班。成功后的配置写回由服务负责，这里只刷新展示。</summary>
    [RelayCommand(CanExecute = nameof(CanSelectClassCore))]
    private async Task SelectClassAsync()
    {
        if (SelectedClassOption is not { } option)
            return;

        try
        {
            await controlService.SelectClassAsync(option.Id);
            ApplySnapshot(controlService.Snapshot);
        }
        catch (Exception)
        {
            toastService.Show(CR.Settings_Control_Error_Unknown, ToastSeverity.Error);
        }
    }

    /// <summary>释放当前占着的班。</summary>
    [RelayCommand(CanExecute = nameof(CanReleaseClassCore))]
    private async Task ReleaseClassAsync()
    {
        try
        {
            await controlService.ReleaseClassAsync();
            SelectedClassOption = null;
            ApplySnapshot(controlService.Snapshot);
        }
        catch (Exception)
        {
            toastService.Show(CR.Settings_Control_Error_Unknown, ToastSeverity.Error);
        }
    }

    /// <summary>「刷新班级」按钮：重新拉一遍班级清单，失败弹提示。</summary>
    [RelayCommand]
    private Task RefreshClasses() => RefreshClassesAsync();

    // ---------------------------------------------------------------------------------------
    // 快照 → 界面
    // ---------------------------------------------------------------------------------------

    /// <summary>快照可能在后台线程推送，先切回 UI 线程再改可观察属性。</summary>
    private void OnSnapshotChanged(object? sender, ControlNodeLinkState state)
    {
        if (Dispatcher.UIThread.CheckAccess())
            OnSnapshot(state);
        else
            Dispatcher.UIThread.Post(() => OnSnapshot(state));
    }

    private void OnSnapshot(ControlNodeLinkState state)
    {
        var wasConnected = _wasConnected;
        ApplySnapshot(state);
        _wasConnected = state.IsConnected;

        // 断线重连回来（或第一次连上）时班级清单可能变了，补拉一次。
        if (controlService.IsConfigured && (!wasConnected || ClassOptions.Count == 0))
            _ = RefreshClassesAsync(silent: true);
    }

    /// <summary>把服务给的不可变快照画到页面上。</summary>
    private void ApplySnapshot(ControlNodeLinkState state)
    {
        StatusText = LookupKey(state.StatusKey) ?? CR.Settings_Control_Status_Unknown;
        DetailText = LookupKey(state.DetailKey) ?? DetailFallback(state);

        IsConfigured = controlService.IsConfigured;
        DeviceIdText = string.IsNullOrWhiteSpace(controlService.DeviceId)
            ? EmptyValue
            : controlService.DeviceId;
        SiteNameText = FirstNotEmpty(controlService.SiteName, controlService.SiteId) ?? EmptyValue;

        var selectedClassId = FirstNotEmpty(state.SelectedClassId, Config.ControlCenter.SelectedClassId);
        _currentClassId = selectedClassId;
        RefreshCurrentClassText();

        NotifyCommandStates();
    }

    /// <summary>备份状态可能在后台线程推送，先切回 UI 线程再改可观察属性。</summary>
    private void OnHomeworkBackupChanged(object? sender, ControlHomeworkBackupState state)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ApplyHomeworkBackup(state);
        else
            Dispatcher.UIThread.Post(() => ApplyHomeworkBackup(state));
    }

    /// <summary>「自动备份作业」的状态行：最近一次成功的本地时间 / 上次失败 / 还没备份过。</summary>
    private void ApplyHomeworkBackup(ControlHomeworkBackupState state)
    {
        string value;
        if (state.LastSuccessAt is { } successAt)
        {
            value = successAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        }
        else if (state.LastErrorKey is not null)
        {
            value = CR.Settings_Control_HomeworkBackup_Failed;
        }
        else
        {
            value = CR.Settings_Control_HomeworkBackup_Never;
        }

        HomeworkBackupStatusText = string.Format(
            CultureInfo.CurrentCulture,
            CR.Settings_Control_HomeworkBackup_StatusFormat,
            value);
    }

    /// <summary>详情行没有 <c>DetailKey</c> 时按状态给一句通用说明。</summary>
    private static string DetailFallback(ControlNodeLinkState state) => state.Status switch
    {
        ControlNodeLinkStatus.Disabled => CR.Settings_Control_Detail_Disabled,
        ControlNodeLinkStatus.Unpaired => CR.Settings_Control_Detail_Unpaired,
        ControlNodeLinkStatus.Connecting => state.RetryDelay is not null
            ? CR.Settings_Control_Detail_Retrying
            : CR.Settings_Control_Detail_Connecting,
        ControlNodeLinkStatus.Online => CR.Settings_Control_Detail_Online,
        ControlNodeLinkStatus.Superseded => CR.Settings_Control_Detail_Superseded,
        ControlNodeLinkStatus.Revoked => CR.Settings_Control_Detail_Revoked,
        ControlNodeLinkStatus.Stopped => CR.Settings_Control_Detail_Stopped,
        _ => CR.Settings_Control_Status_Unknown
    };

    /// <summary>当前班级显示名：清单里能找到就用「年级名 · 班级名」，找不到退回 ID，没班就写「未占用」。</summary>
    private void RefreshCurrentClassText()
    {
        if (_currentClassId is null)
        {
            CurrentClassText = CR.Settings_Control_ClassNone;
            return;
        }

        CurrentClassText = ClassOptions.FirstOrDefault(option => option.Id == _currentClassId)?.DisplayName
                           ?? _currentClassId;
    }

    /// <summary>
    ///     拉班级清单：把每个年级下的班摊平成「年级名 · 班级名」。
    ///     <paramref name="silent" /> 为真时不弹失败提示（页面刚打开 / 自动补拉用）。
    /// </summary>
    public async Task RefreshClassesAsync(bool silent = false)
    {
        if (!controlService.IsConfigured)
        {
            _currentClassId = null;
            ClassOptions.Clear();
            SelectedClassOption = null;
            RefreshCurrentClassText();
            NotifyCommandStates();
            return;
        }

        try
        {
            var grades = await controlService.GetTreeAsync();

            var options = new List<ControlClassOption>();
            foreach (var grade in grades)
            foreach (var classNode in grade.Classes)
                options.Add(new ControlClassOption(classNode.Id, $"{grade.Name} · {classNode.Name}"));

            // 换清单时保留当前选中项（按 Id 对回来）。
            var selectedId = SelectedClassOption?.Id ?? _currentClassId;

            ClassOptions.Clear();
            foreach (var option in options)
                ClassOptions.Add(option);

            SelectedClassOption = selectedId is null
                ? null
                : ClassOptions.FirstOrDefault(option => option.Id == selectedId);

            RefreshCurrentClassText();
            NotifyCommandStates();
        }
        catch (Exception)
        {
            if (!silent)
                toastService.Show(CR.Settings_Control_Error_Network, ToastSeverity.Error);
        }
    }

    // ---------------------------------------------------------------------------------------
    // 小工具
    // ---------------------------------------------------------------------------------------

    /// <summary>按资源键取当前语言下的文案；键为空或资源不存在时返回 <c>null</c>。</summary>
    private static string? LookupKey(string? key) =>
        string.IsNullOrEmpty(key) ? null : CR.ResourceManager.GetString(key, CR.Culture);

    /// <summary>取第一个非空白字符串。</summary>
    private static string? FirstNotEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : string.IsNullOrWhiteSpace(second) ? null : second;

    /// <summary>刷新所有依赖当前状态的可观察属性与命令可用性。</summary>
    private void NotifyCommandStates()
    {
        OnPropertyChanged(nameof(HasCurrentClass));
        OnPropertyChanged(nameof(CanEnroll));
        OnPropertyChanged(nameof(CanForget));
        OnPropertyChanged(nameof(CanSelectClass));
        OnPropertyChanged(nameof(CanReleaseClass));

        EnrollCommand.NotifyCanExecuteChanged();
        ForgetCommand.NotifyCanExecuteChanged();
        SelectClassCommand.NotifyCanExecuteChanged();
        ReleaseClassCommand.NotifyCanExecuteChanged();
    }

    private bool CanEnrollCore() =>
        !IsEnrolling && !string.IsNullOrWhiteSpace(SiteIdDraft) && !string.IsNullOrWhiteSpace(PairCodeDraft);

    private bool CanForgetCore() => !IsEnrolling && IsConfigured;

    private bool CanSelectClassCore() => !IsEnrolling && IsConfigured && SelectedClassOption is not null;

    private bool CanReleaseClassCore() => !IsEnrolling && IsConfigured && HasCurrentClass;
}