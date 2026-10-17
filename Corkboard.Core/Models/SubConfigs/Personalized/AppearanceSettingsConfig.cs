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

    /// <summary>
    ///     界面字号：整窗的默认字号（按钮、列表、提示都跟着它走）。
    ///     作业内容与科目标题另有自己的字号设置，不受这一项影响。
    ///     读的时候过一遍 <see cref="ClampUiFontSize" />。
    /// </summary>
    [ObservableProperty] private double _uiFontSize = DefaultUiFontSize;

    /// <summary>没设过时的界面字号。</summary>
    public const double DefaultUiFontSize = 14;

    public const double MinUiFontSize = 10;

    public const double MaxUiFontSize = 24;

    /// <summary>把界面字号夹进合法区间；坏值退回默认值。</summary>
    public static double ClampUiFontSize(double size)
    {
        return double.IsFinite(size) ? Math.Clamp(size, MinUiFontSize, MaxUiFontSize) : DefaultUiFontSize;
    }
}
