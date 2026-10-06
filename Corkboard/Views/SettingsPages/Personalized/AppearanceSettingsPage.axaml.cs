using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.SettingsPages;

namespace Corkboard.Views.SettingsPages.Personalized;

/// <summary>外观设置页：主题模式、主题色、字体族与字体粗细，改完立即生效，不需要重启。</summary>
[PageInfo("settings.personalized.appearance", FluentIcons.ColorFilled)]
public partial class AppearanceSettingsPage : UserControl
{
    private readonly AppearanceSettingsPageViewModel _viewModel;

    public AppearanceSettingsPage()
    {
        InitializeComponent();

        _viewModel = IAppHost.GetService<AppearanceSettingsPageViewModel>();
        DataContext = _viewModel;

        // 主题/主题色/字体/字重都是「选了立刻改」：配置一变就把新值应用到主题与字体资源上。
        // 配置是单例、页面是 transient，所以离开可视树时必须摘掉订阅（见 AGENTS.md）。
        _viewModel.Config.Appearance.PropertyChanged += OnAppearanceChanged;
        Unloaded += (_, _) =>
        {
            _viewModel.Config.Appearance.PropertyChanged -= OnAppearanceChanged;

            // 取色器的最后一次选择可能还压在防抖里，离开页面前补一次。
            _viewModel.FlushThemeColorDraft();
        };
    }

    private static void OnAppearanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        App.Current.RefreshAppearanceSettings();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
