using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Models;
using Corkboard.Core.Services.Config;

namespace Corkboard.ViewModels;

/// <summary>
///     所有 ViewModel 的基类：把主配置直接暴露成 <see cref="Config" />，
///     页面绑定 <c>Config.xxx</c> 即可，改属性会自动落盘（见 ConfigHandlerBase）。
/// </summary>
public class ViewModelBase(MainConfigHandler configHandler) : ObservableRecipient
{
    public MainConfigModel Config { get; } = configHandler.Data;
}
