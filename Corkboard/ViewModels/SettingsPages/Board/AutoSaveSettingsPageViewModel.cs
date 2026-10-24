using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Board.AutoSave;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Board;
using Corkboard.Services.Desktop;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>
///     「自动保存」：板子改完等几秒把整块板面存成一张 PNG，存在哪、多久存一次、留多少天。
///     <para>
///         真正的存档动作在 <see cref="BoardAutoSaveService" />（单例，订阅板子改动），这里只改配置、
///         顺手把「上次存到哪」显示出来，以及给一个「立即保存一张」的手动入口。
///     </para>
///     <para>
///         目录不给手输，只能从文件夹选择器挑或者一键恢复默认：手输一个不存在的盘符 / 打错的路径，
///         结果是「开了自动保存却什么都没存」，而失败只会记日志，用户根本发现不了。
///         保留天数仍然是草稿式的（<c>NumericUpDown</c> 每动一格就发一次变更，直接双向绑配置
///         等于每动一格写一遍 settings.json），失焦 / 离开页面再提交。
///     </para>
///     <para>
///         本 VM 由键控 DI 以 transient 创建，而服务是单例：订阅了 <c>StatusChanged</c> 就必须在
///         页面离开可视树时 <see cref="Detach" />，否则每开一次页面就往单例上多挂一个处理器。
///     </para>
/// </summary>
public partial class AutoSaveSettingsPageViewModel : ViewModelBase
{
    private readonly BoardAutoSaveService _autoSave;
    private readonly IExternalLauncher _launcher;

    public AutoSaveSettingsPageViewModel(
        MainConfigHandler configHandler,
        BoardAutoSaveService autoSave,
        IExternalLauncher launcher)
        : base(configHandler)
    {
        _autoSave = autoSave;
        _launcher = launcher;

        // 手改过 settings.json 的越界值先夹一次：界面显示的就是真正生效的那个数。
        _retentionDaysDraft = BoardAutoSavePolicy.ClampRetentionDays(Settings.RetentionDays);
        CommitAutoSave();

        RefreshStatus();
    }

    public BoardAutoSaveSettingsConfig Settings => Config.BoardAutoSave;

    #region 保存时机

    /// <summary>「每天一张」/「每次修改都存」两个选项。</summary>
    public IReadOnlyList<BoardAutoSaveModeOption> ModeOptions { get; } =
    [
        new(BoardAutoSaveMode.Daily, CR.Settings_Board_AutoSave_ModeDaily),
        new(BoardAutoSaveMode.EveryChange, CR.Settings_Board_AutoSave_ModeEveryChange)
    ];

    public BoardAutoSaveModeOption SelectedModeOption
    {
        get => ModeOptions.FirstOrDefault(option => option.Mode == Settings.Mode) ?? ModeOptions[0];
        set
        {
            if (value is null || Settings.Mode == value.Mode)
                return;

            // 写配置即触发服务那边排一次存档（换了时机，今天该有的图可能还没按新命名生成）。
            Settings.Mode = value.Mode;
            RefreshStatus();
        }
    }

    #endregion

    #region 保留天数（草稿）

    // 数值框的上下限与 BoardAutoSavePolicy 同源：不要在 XAML 里另写一套数字。
    public decimal MinRetentionDays => BoardAutoSavePolicy.MinRetentionDays;

    public decimal MaxRetentionDays => BoardAutoSavePolicy.MaxRetentionDays;

    /// <summary>保留天数的草稿。</summary>
    [ObservableProperty] private decimal? _retentionDaysDraft;

    /// <summary>
    ///     提交保留天数。夹过的值同步回界面，界面显示的永远是真正生效的那个数。
    ///     <para>数值框失焦、离开页面时都会调。</para>
    /// </summary>
    public void CommitAutoSave()
    {
        var days = BoardAutoSavePolicy.ClampRetentionDays(
            RetentionDaysDraft is { } value
                ? (int)Math.Round(value, MidpointRounding.AwayFromZero)
                : Settings.RetentionDays);

        if (Settings.RetentionDays != days)
            Settings.RetentionDays = days;
        if (RetentionDaysDraft != (decimal)days)
            RetentionDaysDraft = days;
    }

    #endregion

    #region 保存位置

    /// <summary>现在生效的存档目录（绝对路径）。没配过就是软件数据目录下的 <c>autosave</c>。</summary>
    public string Folder => Settings.ResolveFolder();

    /// <summary>用户是不是自己挑过目录；没挑过时界面标一句「默认位置」。</summary>
    public bool HasCustomFolder => Settings.HasCustomFolder;

    /// <summary>把目录换成用户挑的那一个（空串 / 空白等于恢复默认）。</summary>
    public void SetFolder(string? folder)
    {
        var normalized = BoardAutoSavePolicy.NormalizeFolder(folder);
        if (Settings.Folder == normalized)
            return;

        Settings.Folder = normalized;
        OnPropertyChanged(nameof(Folder));
        OnPropertyChanged(nameof(HasCustomFolder));
        RefreshStatus();
    }

    /// <summary>恢复默认目录（软件数据目录下的 <c>autosave</c>）。</summary>
    [RelayCommand]
    private void ResetFolder() => SetFolder(null);

    /// <summary>在资源管理器里打开存档目录；还没建目录时先建出来再打开，否则系统会弹「找不到」。</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        var folder = Folder;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 建不出来就直接尝试打开：目录可能已经存在但不可写，能打开就先让用户看到东西。
        }

        _launcher.TryOpenDirectory(folder);
    }

    #endregion

    #region 状态与立即保存

    /// <summary>上一次存档的结果（时间 + 文件名 / 失败原因 / 还没存过），一行字。</summary>
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>上一次存档失败了才标红。</summary>
    [ObservableProperty] private bool _hasError;

    /// <summary>存档目录里现在有几张图。</summary>
    [ObservableProperty] private string _countText = "0";

    /// <summary>存档目录一共占多少空间，已经换算成人看的单位。</summary>
    [ObservableProperty] private string _sizeText = "0 B";

    /// <summary>
    ///     用户点了「立即保存一张」：不看开关（关着也能手动存一张留个底），失败原因写进状态行。
    /// </summary>
    [RelayCommand]
    private async Task SaveNowAsync()
    {
        CommitAutoSave();
        await _autoSave.SaveNowAsync(manual: true);
        RefreshStatus();
    }

    /// <summary>进页面 / 状态变化 / 改完目录时重读一次状态与占用。</summary>
    public void RefreshStatus()
    {
        HasError = _autoSave.LastError is not null;

        if (_autoSave.LastError is { } error)
        {
            StatusText = error;
        }
        else if (_autoSave.LastSavedAt is { } savedAt && _autoSave.LastSavedPath is { } path)
        {
            // 只显示文件名：完整路径太长了，而且上面「保存位置」那行已经写着目录。
            StatusText = string.Format(
                CR.Settings_Board_AutoSave_LastSavedFormat,
                savedAt.LocalDateTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                Path.GetFileName(path));
        }
        else
        {
            StatusText = CR.Settings_Board_AutoSave_LastSavedNever;
        }

        var statistics = _autoSave.GetStatistics();
        CountText = statistics.Count.ToString(CultureInfo.CurrentCulture);
        SizeText = FormatSize(statistics.Bytes);
    }

    /// <summary>进页面：订阅服务状态并现读一次。重复调用无副作用。</summary>
    public void Attach()
    {
        _autoSave.StatusChanged -= OnStatusChanged;
        _autoSave.StatusChanged += OnStatusChanged;
        RefreshStatus();
    }

    /// <summary>页面离开可视树时断开订阅（见类型注释）。</summary>
    public void Detach() => _autoSave.StatusChanged -= OnStatusChanged;

    private void OnStatusChanged(object? sender, EventArgs e) => RefreshStatus();

    /// <summary>字节数换成人看的单位；一屏存档大多在几 MB 量级，超过 MB 仍按 MB 显示。</summary>
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

/// <summary>「保存时机」下拉项：枚举值 + 本地化名称。</summary>
public sealed record BoardAutoSaveModeOption(BoardAutoSaveMode Mode, string DisplayName);
