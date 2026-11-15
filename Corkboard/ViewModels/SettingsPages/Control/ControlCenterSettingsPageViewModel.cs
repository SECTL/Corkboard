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

/// <summary>
///     设备结构树里的一间班。<see cref="Id" /> 就是 <c>class.select</c> 要的班级 ID，
///     下拉框显示「年级 · 班级」（设备端只拿得到 ID，名字得从树里查）。
/// </summary>
public sealed record ControlClassOption(string Id, string GradeName, string ClassName)
{
    public string DisplayName => $"{GradeName} · {ClassName}";
}

/// <summary>
///     「设置 → 集控」页的 ViewModel：开关、连接状态、设备信息、接入与清空。
///     <para>
///         连接本身由 <see cref="ControlCenterHostedService" />（单例后台服务）负责：它监听配置、
///         自己重连、自己把接入结果弹成 toast。这一页只做三件事——把草稿写回配置、
///         调服务的方法、把服务的最新快照画出来。所以这里**不调** <c>RestartAsync</c>，
///         也**不重复**弹接入成功 / 失败的提示。
///     </para>
///     <para>
///         状态文案走 <see cref="ControlNodeLinkState.StatusKey" /> / <see cref="ControlNodeLinkState.DetailKey" />
///         （Core 只给键不给中文），找不到对应资源时退回 <c>Settings_Control_Status_Unknown</c>。
///         在线时不显示详情行：用户明确要求状态只写一个「在线」，不要「等老师下发」那类说明。
///     </para>
///     <para>
///         页面是 transient、服务是单例，所以订阅 <see cref="ControlCenterHostedService.SnapshotChanged" />
///         必须在 <see cref="Detach" /> 里退订，否则每开一次页面就多挂一个处理器。
///     </para>
///     <para>
///         班级只显示「年级 / 班级」和一个换班下拉框：树里的名字靠
///         <see cref="ControlCenterHostedService.GetTreeAsync" /> 查（设备端只有班级 ID），
///         选完立即发 <c>class.select</c>（离线只记住，重连后自动重占）。<b>没有</b>占班 / 下课 / 刷新按钮，
///         用户明确说那些没必要。
///     </para>
/// </summary>
public partial class ControlCenterSettingsPageViewModel(
    MainConfigHandler configHandler,
    ControlCenterHostedService controlService,
    IToastService toastService,
    IExternalLauncher launcher) : ViewModelBase(configHandler)
{
    /// <summary>网页控制台路径（拼在集控站点后面）。</summary>
    private const string ConsolePath = "/dashboard/control";

    /// <summary>设备 ID / 地点没接入时的占位符。</summary>
    private const string EmptyValue = "—";

    // ---------------------------------------------------------------------------------------
    // 草稿：文本框只绑这些，失焦 / 离开页面时才写回 Config.ControlCenter（见 CommitAll）。
    // ---------------------------------------------------------------------------------------

    [ObservableProperty] private string _displayNameDraft = string.Empty;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string _detailText = string.Empty;

    /// <summary>在线时状态行前面点一颗绿点；其它状态不画点。</summary>
    [ObservableProperty] private bool _isOnline;

    [ObservableProperty] private string _deviceIdText = EmptyValue;

    [ObservableProperty] private string _siteNameText = EmptyValue;

    // ---------------------------------------------------------------------------------------
    // 班级：设备端只知道班级 ID，年级 / 班级名得从服务端结构树查。
    // ---------------------------------------------------------------------------------------

    /// <summary>当前占着的班级 ID（空 = 没选班）。快照里的 <c>SelectedClassId</c> 优先。</summary>
    private string? _currentClassId;

    /// <summary>班级 ID → 年级 / 班级名。离线时靠它继续画已有班的名字（树拿不到就保留上一次）。</summary>
    private readonly Dictionary<string, ControlClassOption> _classNames = new(StringComparer.Ordinal);

    /// <summary>正在拉结构树：防止页面打开与状态变化同时触发两次请求。</summary>
    private bool _classesLoading;

    /// <summary>程序正在恢复下拉框选中项：这时不能把赋值当成「用户换班」再发一次 class.select。</summary>
    private bool _suppressClassSelection;

    /// <summary>所在年级（名字查不到时是占位符）。</summary>
    [ObservableProperty] private string _gradeText = EmptyValue;

    /// <summary>所在班级（没选班时是「还没选班级」）。</summary>
    [ObservableProperty] private string _classText = CR.Settings_Control_ClassNone;

    [ObservableProperty] private ObservableCollection<ControlClassOption> _classOptions = [];

    [ObservableProperty] private ControlClassOption? _selectedClassOption;

    /// <summary>结构树拿到了才有下拉框可选；拿不到就只显示当前班级。</summary>
    public bool HasClassOptions => ClassOptions.Count > 0;

    /// <summary>「最近同步」那一行：成功时是本地时间，没成功过 / 失败时给对应文案。</summary>
    [ObservableProperty] private string _homeworkBackupStatusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnroll))]
    [NotifyPropertyChangedFor(nameof(CanForget))]
    [NotifyPropertyChangedFor(nameof(EnrollButtonText))]
    [NotifyCanExecuteChangedFor(nameof(EnrollCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForgetCommand))]
    private bool _isEnrolling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanForget))]
    [NotifyPropertyChangedFor(nameof(EnrollDescription))]
    [NotifyCanExecuteChangedFor(nameof(ForgetCommand))]
    private bool _isConfigured;

    /// <summary>「接入」按钮是否可用：两个输入都非空且没有正在接入。</summary>
    public bool CanEnroll => CanEnrollCore();

    /// <summary>「清空」按钮是否可用：已接入且没有正在接入。</summary>
    public bool CanForget => !IsEnrolling && IsConfigured;

    /// <summary>详情行只在有内容时占位（在线时为空，页面就只剩「在线」两个字）。</summary>
    public bool HasDetail => DetailText.Length > 0;

    /// <summary>未接入时在卡头提示可以整句粘贴；已接入时这行提示没有意义。</summary>
    public string EnrollDescription => IsConfigured ? string.Empty : CR.Settings_Control_PasteHint;

    /// <summary>接入按钮的文字：执行中换成「正在接入」。</summary>
    public string EnrollButtonText =>
        IsEnrolling ? CR.Settings_Control_Enrolling : CR.Settings_Control_Enroll;

    // ---------------------------------------------------------------------------------------
    // 生命周期
    // ---------------------------------------------------------------------------------------

    /// <summary>页面进入可视树：用配置初始化草稿、订阅服务快照，并把当前状态画出来。</summary>
    public void Attach()
    {
        var settings = Config.ControlCenter;
        DisplayNameDraft = settings.DisplayName;
        SiteIdDraft = settings.SiteId;
        PairCodeDraft = settings.PairCode;

        // 先减后加，防止同一个 VM 被重复 Attach 时挂两遍。
        controlService.SnapshotChanged -= OnSnapshotChanged;
        controlService.SnapshotChanged += OnSnapshotChanged;
        controlService.HomeworkBackupChanged -= OnHomeworkBackupChanged;
        controlService.HomeworkBackupChanged += OnHomeworkBackupChanged;

        ApplySnapshot(controlService.Snapshot);
        ApplyHomeworkBackup(controlService.HomeworkBackup);

        // 已接入就顺手把班级树拉一次：离线时 Core 返回空列表，界面保留占位符，等连上再拉。
        if (IsConfigured)
            _ = RefreshClassesAsync();
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
    ///     把三个草稿写回 <see cref="Config.ControlCenter" />。
    ///     <para>
    ///         只在值真的变了时写：<c>ConfigHandlerBase</c> 每次属性变化都会落一次盘，
    ///         每次失焦都无脑赋值等于把 <c>settings.json</c> 重写一遍。
    ///     </para>
    /// </summary>
    public void CommitAll()
    {
        var settings = Config.ControlCenter;

        var displayName = (DisplayNameDraft ?? string.Empty).Trim();
        if (settings.DisplayName != displayName)
            settings.DisplayName = displayName;

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

    /// <summary>用系统浏览器打开集控网页控制台（地址跟着配置里的集控站点走）。</summary>
    [RelayCommand]
    private void OpenConsole()
    {
        var baseUrl = (Config.ControlCenter.ServerUrl ?? string.Empty).Trim().TrimEnd('/');
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
            CommitAll();
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

    /// <summary>清空本机接入：断开连接并删除凭证（确认框在页面 code-behind 里，确认后才执行这个命令）。</summary>
    [RelayCommand(CanExecute = nameof(CanForgetCore))]
    private async Task ForgetAsync()
    {
        try
        {
            await controlService.ForgetAsync();

            PairCodeDraft = string.Empty;
            SiteIdDraft = Config.ControlCenter.SiteId;

            // 清空之后设备就不再属于任何班，树和名字一起丢掉，免得下次接入前还挂着旧班级。
            _currentClassId = null;
            _classNames.Clear();
            ClassOptions.Clear();
            OnPropertyChanged(nameof(HasClassOptions));
            SyncClassSelection();
            RefreshClassDisplay();

            ApplySnapshot(controlService.Snapshot);
        }
        catch (Exception)
        {
            toastService.Show(CR.Settings_Control_Error_Unknown, ToastSeverity.Error);
        }
    }

    // ---------------------------------------------------------------------------------------
    // 班级
    // ---------------------------------------------------------------------------------------

    /// <summary>换班：选完立即生效（离线时 Core 只落盘，重连后自动重占），不需要再点按钮。</summary>
    partial void OnSelectedClassOptionChanged(ControlClassOption? value)
    {
        if (_suppressClassSelection || value is null || value.Id == _currentClassId)
            return;

        _ = ApplyClassSelectionAsync(value);
    }

    private async Task ApplyClassSelectionAsync(ControlClassOption option)
    {
        try
        {
            await controlService.SelectClassAsync(option.Id);

            _currentClassId = option.Id;
            _classNames[option.Id] = option;
            RefreshClassDisplay();
        }
        catch (Exception)
        {
            toastService.Show(CR.Settings_Control_Error_Unknown, ToastSeverity.Error);
        }
    }

    /// <summary>
    ///     拉设备结构树（年级 → 班级）。失败时 Core 返回空列表：这时**保留上一次拉到的名字**，
    ///     否则每次断线重连都会把「年级 / 班级」擦成占位符。
    /// </summary>
    private async Task RefreshClassesAsync()
    {
        if (!IsConfigured || _classesLoading)
            return;

        _classesLoading = true;
        try
        {
            var grades = await controlService.GetTreeAsync();

            var options = new List<ControlClassOption>();
            foreach (var grade in grades)
            foreach (var classNode in grade.Classes)
                options.Add(new ControlClassOption(classNode.Id, grade.Name, classNode.Name));

            if (options.Count == 0 && _classNames.Count > 0)
                return;

            foreach (var option in options)
                _classNames[option.Id] = option;

            _suppressClassSelection = true;
            try
            {
                ClassOptions.Clear();
                foreach (var option in options)
                    ClassOptions.Add(option);

                SelectedClassOption = FindClassOption(_currentClassId);
            }
            finally
            {
                _suppressClassSelection = false;
            }

            OnPropertyChanged(nameof(HasClassOptions));
            RefreshClassDisplay();
        }
        catch (Exception)
        {
            // 树拿不到不影响页面其它部分：显示沿用上一次的名字，下拉框暂时是空的。
        }
        finally
        {
            _classesLoading = false;
        }
    }

    /// <summary>把「年级」「班级」两行画出来；名字还没同步到时给一句待同步，而不是把 ID 直接怼给用户。</summary>
    private void RefreshClassDisplay()
    {
        if (_currentClassId is null)
        {
            GradeText = EmptyValue;
            ClassText = CR.Settings_Control_ClassNone;
            return;
        }

        if (_classNames.TryGetValue(_currentClassId, out var known))
        {
            GradeText = known.GradeName;
            ClassText = known.ClassName;
            return;
        }

        GradeText = EmptyValue;
        ClassText = CR.Settings_Control_ClassPending;
    }

    /// <summary>把下拉框的选中项对到当前班级（这次赋值不算用户换班，见 <c>_suppressClassSelection</c>）。</summary>
    private void SyncClassSelection()
    {
        var option = FindClassOption(_currentClassId);
        if (ReferenceEquals(option, SelectedClassOption))
            return;

        _suppressClassSelection = true;
        try
        {
            SelectedClassOption = option;
        }
        finally
        {
            _suppressClassSelection = false;
        }
    }

    private ControlClassOption? FindClassOption(string? classId) =>
        classId is null ? null : ClassOptions.FirstOrDefault(option => option.Id == classId);

    // ---------------------------------------------------------------------------------------
    // 快照 → 界面
    // ---------------------------------------------------------------------------------------

    /// <summary>快照可能在后台线程推送，先切回 UI 线程再改可观察属性。</summary>
    private void OnSnapshotChanged(object? sender, ControlNodeLinkState state)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ApplySnapshot(state);
        else
            Dispatcher.UIThread.Post(() => ApplySnapshot(state));
    }

    /// <summary>把服务给的不可变快照画到页面上。</summary>
    private void ApplySnapshot(ControlNodeLinkState state)
    {
        StatusText = LookupKey(state.StatusKey) ?? CR.Settings_Control_Status_Unknown;

        // 在线时只要一个「在线」：详情行留空，XAML 按 HasDetail 把它整行收起来。
        var online = state.Status == ControlNodeLinkStatus.Online;
        IsOnline = online;
        DetailText = online ? string.Empty : LookupKey(state.DetailKey) ?? DetailFallback(state);

        IsConfigured = controlService.IsConfigured;
        DeviceIdText = string.IsNullOrWhiteSpace(controlService.DeviceId)
            ? EmptyValue
            : controlService.DeviceId;
        SiteNameText = FirstNotEmpty(controlService.SiteName, controlService.SiteId) ?? EmptyValue;

        // 班级：快照里的是权威值（会话启动时从状态文件恢复过）；被顶掉 / 被踢出之后配置里可能还留着旧班，
        // 那两种情况不能再拿配置兜底，否则会显示一台已经不属于自己的班。
        var classId = state.SelectedClassId;
        if (classId is null && state.Status is not (ControlNodeLinkStatus.Superseded or ControlNodeLinkStatus.Revoked))
            classId = Config.ControlCenter.SelectedClassId;

        _currentClassId = classId;
        SyncClassSelection();
        RefreshClassDisplay();

        // 刚连上 / 第一次进页面时树还是空的：拉一次，好把年级和班级的名字写出来。
        if (online && IsConfigured && ClassOptions.Count == 0)
            _ = RefreshClassesAsync();

        NotifyCommandStates();
    }

    /// <summary>同步状态可能在后台线程推送，先切回 UI 线程再改可观察属性。</summary>
    private void OnHomeworkBackupChanged(object? sender, ControlHomeworkBackupState state)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ApplyHomeworkBackup(state);
        else
            Dispatcher.UIThread.Post(() => ApplyHomeworkBackup(state));
    }

    /// <summary>「自动同步作业」的状态行：最近一次成功的本地时间 / 上次失败 / 还没同步过。</summary>
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
        OnPropertyChanged(nameof(CanEnroll));
        OnPropertyChanged(nameof(CanForget));

        EnrollCommand.NotifyCanExecuteChanged();
        ForgetCommand.NotifyCanExecuteChanged();
    }

    private bool CanEnrollCore() =>
        !IsEnrolling && !string.IsNullOrWhiteSpace(SiteIdDraft) && !string.IsNullOrWhiteSpace(PairCodeDraft);

    private bool CanForgetCore() => !IsEnrolling && IsConfigured;
}