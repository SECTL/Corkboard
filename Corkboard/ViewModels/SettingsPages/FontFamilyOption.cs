using Avalonia.Media;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>字体下拉项：写进配置的值 + 下拉里显示的名字，以及用来预览该项的字体。</summary>
/// <param name="Value">
///     写进 <c>Appearance.Font</c> 的值：随包默认字体用
///     <see cref="Corkboard.Core.Services.Fonts.FontFamilyCatalog.DefaultFontSentinel" />，其余是家族名。
/// </param>
/// <param name="Family">下拉里把该项按自己的字体渲染，用来预览。</param>
/// <param name="DisplayName">显示名；默认项取本地化文案，系统字体直接显示家族名。</param>
public sealed record FontFamilyOption(string Value, FontFamily Family, string DisplayName);
