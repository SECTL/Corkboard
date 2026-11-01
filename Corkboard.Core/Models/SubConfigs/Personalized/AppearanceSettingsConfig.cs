using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Services.Fonts;

namespace Corkboard.Core.Models.SubConfigs.Personalized;

/// <summary>个性化设置：主题模式、主题色、字体与动效开关。</summary>
public partial class AppearanceSettingsConfig : ObservableObject
{
    [ObservableProperty] private ThemeMode _theme = ThemeMode.FollowSystem;
    [ObservableProperty] private ThemeColorMode _themeColorMode = ThemeColorMode.Default;
    [ObservableProperty] private Color _themeColor = Color.Parse(GlobalConstants.DefaultThemeColor);

    /// <summary>字体族：<see cref="FontFamilyCatalog.DefaultFontSentinel" /> 表示随包分发的默认字体，其余是家族名。</summary>
    [ObservableProperty] private string _font = FontFamilyCatalog.DefaultFontSentinel;

    [ObservableProperty] private FontWeightMode _fontWeight = FontWeightMode.Normal;
    [ObservableProperty] private bool _enableAnimations = true;
}
