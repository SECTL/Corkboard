using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Desktop;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>
///     关于页的 ViewModel：版本信息全部来自 <see cref="GlobalConstants" />，
///     页面上的外部链接交给 <see cref="IExternalLauncher" /> 用系统浏览器打开。
/// </summary>
public partial class AboutSettingsPageViewModel(
    MainConfigHandler configHandler,
    IExternalLauncher launcher) : ViewModelBase(configHandler)
{
    public string AppName => GlobalConstants.AppName;

    public string Version => GlobalConstants.DisplayVersion;

    /// <summary>版权年份算到今年，免得每年手动改一次。</summary>
    public string Copyright => $"Copyright (c) 2025-{DateTime.Now.Year} SECTL";

    /// <summary>提交号：用户报问题时报的这一串，对着仓库能定位到具体一版。</summary>
    public string Commit => GlobalConstants.CommitHash;

    /// <summary>系统版本 + 架构：出问题时最先要知道的就是「在什么系统上」。</summary>
    public string Platform =>
        $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    /// <summary>运行库版本：自包含包实际跑在哪个 .NET 上。</summary>
    public string Runtime => RuntimeInformation.FrameworkDescription;

    /// <summary>
    ///     打开链接。地址来自 XAML 里各按钮的 <c>CommandParameter</c>（都是常量字面量），
    ///     所以这里不再拼接，交给 <see cref="IExternalLauncher" /> 判断能不能开。
    /// </summary>
    [RelayCommand]
    private void OpenUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return;

        launcher.TryOpenUri(uri);
    }
}
