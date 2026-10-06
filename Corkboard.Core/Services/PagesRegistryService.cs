using System.Collections.ObjectModel;
using Corkboard.Core.Attributes;
using Corkboard.Core.Models;

namespace Corkboard.Core.Services;

/// <summary>
///     页面注册表的静态集合。导航壳只读这里，注册动作发生在 DI 装配阶段
///     （见 <c>PagesRegistryExtensions.AddMainPage</c>）。
///     <para>
///         用完即弃的静态集合意味着它只在进程启动阶段被写入一次；运行期增删导航项会破坏
///         FluentAvalonia 导航控件的选择源，因此运行期只能切换可见性，不能改集合。
///     </para>
/// </summary>
public static class PagesRegistryService
{
    public static ObservableCollection<PageInfo> MainItems { get; } = [];
    public static ObservableCollection<PageInfo> SettingsItems { get; } = [];
    public static ObservableCollection<PageGroupInfo> GroupItems { get; } = [];
}
