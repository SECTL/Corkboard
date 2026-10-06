using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Models.SubConfigs.Personalized;

/// <summary>个性化设置：主题模式、主题色、字体与动效开关。</summary>
public partial class AppearanceSettingsConfig : ObservableObject
{
    [ObservableProperty] private ThemeMode _theme = ThemeMode.FollowSystem;
    [ObservableProperty] private ThemeColorMode _themeColorMode = ThemeColorMode.Default;
    [ObservableProperty] private Color _themeColor = Color.Parse(GlobalConstants.DefaultThemeColor);
    [ObservableProperty] private string _font = "Default";
    [ObservableProperty] private FontWeightMode _fontWeight = FontWeightMode.Normal;
    [ObservableProperty] private bool _enableAnimations = true;
}
