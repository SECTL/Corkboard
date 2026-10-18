using System.Globalization;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Desktop;
using Corkboard.Services.Stats;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>
///     关于页的 ViewModel：版本信息全部来自 <see cref="GlobalConstants" />，
///     页面上的外部链接交给 <see cref="IExternalLauncher" /> 用系统浏览器打开，
///     在线人数来自后台的 <see cref="OnlineStatusService" />（隐私设置选「停止上报」时显示占位符）。
/// </summary>
public partial class AboutSettingsPageViewModel : ViewModelBase
{
    private readonly IExternalLauncher _launcher;
    private readonly OnlineStatusService _onlineStatus;

    public AboutSettingsPageViewModel(
        MainConfigHandler configHandler,
        IExternalLauncher launcher,
        OnlineStatusService onlineStatus) : base(configHandler)
    {
        _launcher = launcher;
        _onlineStatus = onlineStatus;
        _onlineStatus.OnlineCountChanged += OnOnlineCountChanged;
    }

    public string AppName => GlobalConstants.AppName;

    public string Version => GlobalConstants.DisplayVersion;

    /// <summary>版权年份算到今年，免得每年手动改一次。</summary>
    public string Copyright => $"Copyright (c) 2025-{DateTime.Now.Year} 思拓创联";

    /// <summary>提交号：用户报问题时报的这一串，对着仓库能定位到具体一版。</summary>
    public string Commit => GlobalConstants.CommitHash;

    /// <summary>系统版本 + 架构：出问题时最先要知道的就是「在什么系统上」。</summary>
    public string Platform =>
        $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    /// <summary>运行库版本：自包含包实际跑在哪个 .NET 上。</summary>
    public string Runtime => RuntimeInformation.FrameworkDescription;

    /// <summary>
    ///     在线人数。还没拿到（或隐私设置是「停止上报」）时显示「—」，
    ///     不把 0 当真实数字写出来。
    /// </summary>
    public string OnlineUsersCount => _onlineStatus.HasOnlineCount
        ? _onlineStatus.OnlineCount.ToString("N0", CultureInfo.CurrentCulture)
        : "—";

    /// <summary>页面打开时主动查一次在线人数；失败保持占位符，不弹任何提示。</summary>
    public async Task RefreshOnlineStatusAsync()
    {
        await _onlineStatus.GetOnlineStatsAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(OnlineUsersCount));
    }

    /// <summary>离开可视树时断开对单例服务的订阅（页面是 transient 的，见 AGENTS 的设置页规矩）。</summary>
    public void Detach()
    {
        _onlineStatus.OnlineCountChanged -= OnOnlineCountChanged;
    }

    /// <summary>
    ///     打开链接。地址来自 XAML 里各按钮的 <c>CommandParameter</c>（都是常量字面量），
    ///     所以这里不再拼接，交给 <see cref="IExternalLauncher" /> 判断能不能开。
    /// </summary>
    [RelayCommand]
    private void OpenUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return;

        _launcher.TryOpenUri(uri);
    }

    private void OnOnlineCountChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(nameof(OnlineUsersCount));
}
