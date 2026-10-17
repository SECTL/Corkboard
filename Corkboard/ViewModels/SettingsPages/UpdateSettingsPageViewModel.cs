using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Models.SubConfigs.Update;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Updates;
using Corkboard.Shared.Updates;
using Microsoft.Extensions.Logging;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>
///     更新设置页：把更新客户端的状态画出来，并编辑自动更新模式与更新通道。
/// </summary>
/// <remarks>
///     状态机（检查中 / 发现新版本 / 下载中 / 待安装……）全在 <see cref="UpdateCenterService" /> 里，
///     页面只负责显示与转发动作，不在 ViewModel 里另判一套，免得两边说的不是一回事。
///     自动更新模式、通道这类偏好就是配置（<see cref="UpdateSettingsConfig" />）：界面直接改，
///     <see cref="MainConfigHandler" /> 会自动落盘，这里不再显式 <c>Save</c>。
///     <para>
///         ViewModel 是 transient、配置句柄是单例，所以离开页面时必须在 <see cref="Detach" /> 里退订；
///         订阅「配置重载」是因为从云端恢复设置会把整个配置对象换掉，手里那份旧引用不能再留着。
///     </para>
/// </remarks>
public sealed partial class UpdateSettingsPageViewModel : ViewModelBase
{
    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<UpdateSettingsPageViewModel>? _logger;

    /// <summary>当前持有的子配置。配置重载后会被换成新对象（见 <see cref="OnConfigReloaded" />）。</summary>
    private UpdateSettingsConfig _settings;

    /// <param name="configHandler">配置句柄，交给 <see cref="ViewModelBase" /> 持有。</param>
    /// <param name="updateCenter">更新客户端（单例），状态与动作都从它来。</param>
    /// <param name="logger">日志，可选（测试与设计器场景可能没有）。</param>
    public UpdateSettingsPageViewModel(MainConfigHandler configHandler,
        UpdateCenterService updateCenter,
        ILogger<UpdateSettingsPageViewModel>? logger = null) : base(configHandler)
    {
        _configHandler = configHandler;
        _logger = logger;
        UpdateCenter = updateCenter;

        _settings = configHandler.Data.UpdateSettings;
        _selectedChannelIndex = ChannelIndexOf(_settings.UpdateChannel);
        _lastCheckTimeText = FormatLastCheckTime();
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        _configHandler.Reloaded += OnConfigReloaded;
    }

    /// <summary>
    ///     更新客户端：状态文案、可用安装包、动作可用性都直接绑它（它自己实现了
    ///     <see cref="INotifyPropertyChanged" />，服务层改状态界面就跟着变）。
    /// </summary>
    public UpdateCenterService UpdateCenter { get; }

    /// <summary>更新这一块的配置（自动更新模式、更新通道、上次检查时间）。</summary>
    public UpdateSettingsConfig Settings => _settings;

    /// <summary>更新通道下拉框的选中项（0 稳定版 / 1 测试版 / 2 开发版）。</summary>
    [ObservableProperty] private int _selectedChannelIndex;

    /// <summary>上次检查时间，没检查过时是「尚未检查更新」。</summary>
    [ObservableProperty] private string _lastCheckTimeText;

    /// <summary>
    ///     动作按钮的统一入口：能直接下载安装时就走下载安装，否则先检查。
    ///     （上游页面也是这么接的：主按钮文案由服务给，点下去做什么由当前状态决定。）
    /// </summary>
    [RelayCommand]
    private Task CheckOrInstallAsync()
    {
        return UpdateCenter.CanDownloadAndInstall
            ? RunAsync(() => UpdateCenter.DownloadAndInstallAsync())
            : RunAsync(() => UpdateCenter.CheckAsync());
    }

    /// <summary>安装已经下载好的更新（安装完会重启应用）。</summary>
    [RelayCommand]
    private Task InstallAsync() => RunAsync(() => UpdateCenter.ApplyDownloadedUpdateAsync());

    /// <summary>不管手里是不是最新版，都去当前通道上要一份清单（用于回退到更低版本）。</summary>
    [RelayCommand]
    private Task ForceCheckAsync() => RunAsync(() => UpdateCenter.CheckAsync(force: true));

    /// <summary>
    ///     下拉框改通道：编号认不出来时退回稳定版，配置里不会留下非法枚举值。
    /// </summary>
    partial void OnSelectedChannelIndexChanged(int value)
    {
        var channel = Enum.IsDefined((UpdateChannel)value) ? (UpdateChannel)value : UpdateChannel.Release;
        if (_settings.UpdateChannel != channel)
            _settings.UpdateChannel = channel;
    }

    /// <summary>
    ///     页面离开可视树时断开单例事件（配置句柄与更新客户端都是单例）。
    /// </summary>
    public void Detach()
    {
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        _configHandler.Reloaded -= OnConfigReloaded;
    }

    /// <summary>检查时间由更新客户端写回配置，这里只负责把「上次检查时间」那行重新排一下。</summary>
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or nameof(UpdateSettingsConfig.LastCheckTime)))
            return;

        if (Dispatcher.UIThread.CheckAccess())
            RefreshLastCheckTime();
        else
            Dispatcher.UIThread.Post(RefreshLastCheckTime);
    }

    /// <summary>配置对象被整个换掉（从云端恢复了设置）：重新挂订阅、重取通道与检查时间。</summary>
    private void OnConfigReloaded(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ApplyReloadedConfig();
        else
            Dispatcher.UIThread.Post(ApplyReloadedConfig);
    }

    private void ApplyReloadedConfig()
    {
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        _settings = _configHandler.Data.UpdateSettings;
        _settings.PropertyChanged += OnSettingsPropertyChanged;

        OnPropertyChanged(nameof(Settings));
        SelectedChannelIndex = ChannelIndexOf(_settings.UpdateChannel);
        RefreshLastCheckTime();
    }

    private void RefreshLastCheckTime() => LastCheckTimeText = FormatLastCheckTime();

    private string FormatLastCheckTime()
    {
        return _settings.LastCheckTime is { } lastCheck
            ? lastCheck.ToLocalTime().ToString("g")
            : CR.Update_M_StatusNotChecked;
    }

    private static int ChannelIndexOf(UpdateChannel channel)
    {
        return Enum.IsDefined(channel) ? (int)channel : (int)UpdateChannel.Release;
    }

    /// <summary>
    ///     用户的动作：更新客户端把失败原因写进自己的状态里，这里只兜住意外异常（取消不算错误），
    ///     免得 async 命令把异常抛到 UI 线程上。
    /// </summary>
    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // 用户取消或窗口关闭：不是错误。
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "更新操作失败。");
        }
    }
}
