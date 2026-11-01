using Avalonia.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Display;

namespace Corkboard.Views.SettingsPages.Display;

/// <summary>
///     「设置 → 看板模式 → 背景」页。
///     <para>
///         编辑控件与考试看板「看板显示 → 背景」那张卡是同一个
///         （<c>Corkboard/Views/Common/BoardBackgroundEditor</c>）：两块屏都各自存一份配置，
///         界面与落盘规则一样，只有传进去的 <c>Settings</c> 不同。
///     </para>
/// </summary>
[PageInfo("settings.display.background", FluentIcons.ColorFilled, "settings.display.group")]
public partial class DisplayBackgroundSettingsPage : UserControl
{
    public DisplayBackgroundSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<DisplayBackgroundSettingsPageViewModel>();
    }
}
