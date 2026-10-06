using Avalonia.Media;
using Avalonia.Threading;
using Corkboard.Core;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models.SubConfigs.Personalized;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Services.Fonts;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>
///     外观设置页的 ViewModel：主题模式、主题色、字体族与字体粗细。
///     <para>
///         全都是「选了立刻改」，写进配置后由 <c>App.RefreshAppearanceSettings</c> 重新应用：
///         主题模式与主题色落到 <c>FluentAvaloniaTheme</c>，字体族与字重落到
///         <c>AppFontFamily</c> / <c>ContentControlThemeFontFamily</c> / <c>AppFontWeight</c> 三个资源上。
///     </para>
/// </summary>
public partial class AppearanceSettingsPageViewModel(MainConfigHandler configHandler) : ViewModelBase(configHandler)
{
    public AppearanceSettingsConfig Appearance => Config.Appearance;

    /// <summary>主题模式下拉项；第一项是跟随系统，与 <see cref="ThemeMode" /> 的默认值一致。</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(ThemeMode.FollowSystem, CR.Settings_Appearance_Theme_FollowSystem),
        new(ThemeMode.Light, CR.Settings_Appearance_Theme_Light),
        new(ThemeMode.Dark, CR.Settings_Appearance_Theme_Dark)
    ];

    /// <summary>主题色来源下拉项：跟随系统（系统里用户挑的主题色）或自定义。</summary>
    public IReadOnlyList<ThemeColorModeOption> ThemeColorModeOptions { get; } =
    [
        new(ThemeColorMode.Default, CR.Settings_Appearance_ThemeColorMode_Default),
        new(ThemeColorMode.Custom, CR.Settings_Appearance_ThemeColorMode_Custom)
    ];

    /// <summary>下拉选中项。配置里是越界枚举值时，退回跟随系统。</summary>
    public ThemeOption SelectedTheme
    {
        get => ThemeOptions.FirstOrDefault(option => option.Mode == Appearance.Theme) ?? ThemeOptions[0];
        set
        {
            if (value is not null)
                Appearance.Theme = value.Mode;
        }
    }

    /// <summary>下拉选中项。配置里是越界枚举值时，退回跟随系统。</summary>
    public ThemeColorModeOption SelectedThemeColorMode
    {
        get => ThemeColorModeOptions.FirstOrDefault(option => option.Mode == Appearance.ThemeColorMode)
               ?? ThemeColorModeOptions[0];
        set
        {
            if (value is null)
                return;

            Appearance.ThemeColorMode = value.Mode;

            // 颜色选择器只在「自定义」时显示，换档位要通知这一条。
            OnPropertyChanged(nameof(IsCustomThemeColor));
        }
    }

    /// <summary>自定义主题色的取色器是否显示（选「自定义」时才显示）。</summary>
    public bool IsCustomThemeColor => Appearance.ThemeColorMode == ThemeColorMode.Custom;

    /// <summary>取色器停手多久才把颜色写进配置。拖光谱时每帧都在变，写盘要攒一攒。</summary>
    private static readonly TimeSpan ThemeColorCommitDelay = TimeSpan.FromMilliseconds(400);

    private Color? _themeColorDraft;
    private DispatcherTimer? _themeColorCommitTimer;

    /// <summary>
    ///     取色器的草稿值。
    ///     <para>
    ///         <b>不直接双向绑配置</b>：<c>ColorPicker</c> 在光谱里拖一下会连发几十次 <c>Color</c> 变化，
    ///         而 <c>ConfigHandlerBase</c> 每次属性变化都会落一次盘（没有防抖），等于拖一次光谱写几十遍
    ///         <c>settings.json</c>。所以先进草稿，停手 <see cref="ThemeColorCommitDelay" /> 之后再落到配置上，
    ///         配置一变 <c>App.RefreshAppearanceSettings</c> 就把新主题色应用到主题上。
    ///     </para>
    /// </summary>
    public Color ThemeColorDraft
    {
        get => _themeColorDraft ?? Appearance.ThemeColor;
        set
        {
            if (_themeColorDraft == value || Appearance.ThemeColor == value)
                return;

            _themeColorDraft = value;
            OnPropertyChanged();
            ScheduleThemeColorCommit(value);
        }
    }

    private void ScheduleThemeColorCommit(Color value)
    {
        _themeColorCommitTimer?.Stop();

        _themeColorCommitTimer = new DispatcherTimer { Interval = ThemeColorCommitDelay };
        _themeColorCommitTimer.Tick += (_, _) =>
        {
            _themeColorCommitTimer?.Stop();
            _themeColorCommitTimer = null;
            Appearance.ThemeColor = value;
        };
        _themeColorCommitTimer.Start();
    }

    /// <summary>
    ///     把还压在草稿里的颜色立刻写进配置，并停掉待触发的防抖计时。
    ///     页面离开可视树时调用（见 <c>AppearanceSettingsPage</c> 的 <c>Unloaded</c>），
    ///     免得「选完颜色马上关页面」把最后一次选择丢掉。
    /// </summary>
    public void FlushThemeColorDraft()
    {
        _themeColorCommitTimer?.Stop();
        _themeColorCommitTimer = null;

        if (_themeColorDraft is { } draft && Appearance.ThemeColor != draft)
            Appearance.ThemeColor = draft;
    }

    /// <summary>
    ///     字体下拉项：第一项是随包分发的默认字体，后面按名称排序列出系统字体。
    ///     默认项放最前面，省得在一长串系统字体里往下翻。
    /// </summary>
    public IReadOnlyList<FontFamilyOption> FontOptions { get; } = BuildFontOptions();

    public IReadOnlyList<FontWeightOption> FontWeightOptions { get; } =
    [
        new(FontWeightMode.Thin, CR.Settings_Appearance_FontWeight_Thin),
        new(FontWeightMode.ExtraLight, CR.Settings_Appearance_FontWeight_ExtraLight),
        new(FontWeightMode.Light, CR.Settings_Appearance_FontWeight_Light),
        new(FontWeightMode.Normal, CR.Settings_Appearance_FontWeight_Normal),
        new(FontWeightMode.Medium, CR.Settings_Appearance_FontWeight_Medium),
        new(FontWeightMode.SemiBold, CR.Settings_Appearance_FontWeight_SemiBold),
        new(FontWeightMode.Bold, CR.Settings_Appearance_FontWeight_Bold),
        new(FontWeightMode.ExtraBold, CR.Settings_Appearance_FontWeight_ExtraBold),
        new(FontWeightMode.Black, CR.Settings_Appearance_FontWeight_Black)
    ];

    /// <summary>下拉选中项。配置里是空值或认不出来的字体值时，退回默认项。</summary>
    public FontFamilyOption SelectedFont
    {
        get => FontOptions.FirstOrDefault(option => option.Value == Appearance.Font) ?? FontOptions[0];
        set
        {
            if (value is not null)
                Appearance.Font = value.Value;
        }
    }

    /// <summary>下拉选中项。配置里是越界枚举值时，退回常规。</summary>
    public FontWeightOption SelectedFontWeight
    {
        get => FontWeightOptions.FirstOrDefault(option => option.Mode == Appearance.FontWeight)
               ?? FontWeightOptions.First(option => option.Mode == FontWeightMode.Normal);
        set
        {
            if (value is not null)
                Appearance.FontWeight = value.Mode;
        }
    }

    private static IReadOnlyList<FontFamilyOption> BuildFontOptions()
    {
        var options = new List<FontFamilyOption>
        {
            new(FontFamilyCatalog.DefaultFontSentinel, GlobalConstants.DefaultAvaFontFamily,
                CR.Settings_Appearance_FontFamilyDefault)
        };

        foreach (var familyName in FontFamilyCatalog.NormalizeFamilyNames(
                     FontManager.Current.SystemFonts.Select(font => font.Name)))
            options.Add(new FontFamilyOption(familyName, new FontFamily(familyName), familyName));

        return options;
    }
}
