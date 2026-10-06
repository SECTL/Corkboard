using Corkboard.Core;
using Corkboard.Core.Services.Config;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>关于页的 ViewModel：版本信息全部来自 <see cref="GlobalConstants" />。</summary>
public partial class AboutSettingsPageViewModel(MainConfigHandler configHandler) : ViewModelBase(configHandler)
{
    public string AppName => GlobalConstants.AppName;
    public string Version => GlobalConstants.DisplayVersion;
    public string Commit => GlobalConstants.CommitHash;
    public string Platform => $"{Environment.OSVersion.Platform} / {Environment.Version}";
}
