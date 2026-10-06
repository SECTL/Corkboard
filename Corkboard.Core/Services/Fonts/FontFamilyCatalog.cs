using Avalonia.Media;

namespace Corkboard.Core.Services.Fonts;

/// <summary>
///     字体族候选表：把系统字体列表整理成能放进下拉的家族名，并解析配置里存的字体值。
///     <para>
///         纯逻辑 + 一个 <see cref="FontFamily"/> 构造，不碰 UI，便于单测。
///     </para>
/// </summary>
public static class FontFamilyCatalog
{
    /// <summary>配置里代表「随包分发的默认字体」的哨兵值（见 <see cref="Models.SubConfigs.Personalized.AppearanceSettingsConfig.Font" />）。</summary>
    public const string DefaultFontSentinel = "Default";

    /// <summary>
    ///     过滤并排序家族名：
    ///     <list type="bullet">
    ///         <item>去掉 <c>compositefont:</c> 前缀的合成字体——那不是真实家族，按名字解析不到；</item>
    ///         <item>去掉 <c>@</c> 开头的竖排变体（Windows 上的 <c>@微软雅黑</c> 这类）；</item>
    ///         <item>去掉空白项与重复项（忽略大小写），最后按当前区域排序。</item>
    ///     </list>
    /// </summary>
    public static IReadOnlyList<string> NormalizeFamilyNames(IEnumerable<string?> familyNames)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var familyName in familyNames)
        {
            var trimmed = familyName?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (trimmed.StartsWith("compositefont:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith('@'))
                continue;

            if (seen.Add(trimmed))
                result.Add(trimmed);
        }

        result.Sort(StringComparer.CurrentCultureIgnoreCase);
        return result;
    }

    /// <summary>
    ///     把配置里存的字体值解析成可用的 <see cref="FontFamily"/>：
    ///     空值或哨兵值取随包分发的默认字体（<see cref="GlobalConstants.DefaultFontFamily" />），
    ///     其余按家族名解析。
    /// </summary>
    public static FontFamily Resolve(string? font)
    {
        return string.IsNullOrWhiteSpace(font) || font == DefaultFontSentinel
            ? GlobalConstants.DefaultAvaFontFamily
            : new FontFamily(font);
    }
}
