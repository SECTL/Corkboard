using System.Globalization;
using Corkboard.Core.Models.Board;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.MainPages;

/// <summary>
///     字号下拉里的一项。<paramref name="Size" /> 为空表示「不定字号」——只有「整篇字号」用得上，
///     选中它就是跟随「设置 → 作业板 → 内容默认样式」里的默认字号。
/// </summary>
public sealed record BoardFontSizeOption(double? Size, string DisplayName)
{
    /// <summary>
    ///     造一份档位表：预设档位，外加设置里的默认字号（不在预设里就补进去，
    ///     免得用户手输过 15 之后下拉里挑不回当前值）。
    /// </summary>
    /// <param name="defaultFontSize">设置里的默认字号。</param>
    /// <param name="includeFollowDefault">是否在最前面加一项「跟随默认」。</param>
    public static IReadOnlyList<BoardFontSizeOption> Build(double defaultFontSize, bool includeFollowDefault)
    {
        var fallback = BoardContentStyle.ClampFontSize(defaultFontSize);

        var sizes = BoardContentStyle.PresetFontSizes.ToList();
        if (!sizes.Contains(fallback))
        {
            sizes.Add(fallback);
            sizes.Sort();
        }

        var options = new List<BoardFontSizeOption>(sizes.Count + 1);
        if (includeFollowDefault)
        {
            options.Add(new BoardFontSizeOption(
                null,
                string.Format(CultureInfo.CurrentCulture, CR.Board_FontSizeFollowDefault, Format(fallback))));
        }

        options.AddRange(sizes.Select(size => new BoardFontSizeOption(size, Format(size))));
        return options;
    }

    /// <summary>档位显示成「14」而不是「14.0」。</summary>
    public static string Format(double size)
    {
        return size.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
