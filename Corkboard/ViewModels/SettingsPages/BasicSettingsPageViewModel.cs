using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Services.Config;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>基础设置页的 ViewModel：直接暴露配置子树，页面双向绑定即可。</summary>
public partial class BasicSettingsPageViewModel(MainConfigHandler configHandler) : ViewModelBase(configHandler)
{
    public BasicSettingsConfig Basic => Config.Basic;

    /// <summary>语言下拉项的来源。</summary>
    public LanguageMode[] LanguageModes { get; } = Enum.GetValues<LanguageMode>();
}
