using Avalonia.Controls;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages.Export;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.SettingsPages.Export;

/// <summary>
///     「导出」设置页：导出时问不问格式、默认格式，以及 PDF 排版（纸张、方向、边距、列数、排序、
///     科目分页、页眉页脚）。
///     <para>
///         按用户要求放在「作业板」分组里（导出的是作业板），别挪成顶层项。
///     </para>
/// </summary>
[PageInfo("settings.board.export", FluentIcons.SaveFilled, "settings.board.group")]
public partial class ExportSettingsPage : UserControl
{
    public ExportSettingsPage()
    {
        InitializeComponent();
        DataContext = IAppHost.GetService<ExportSettingsPageViewModel>();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
