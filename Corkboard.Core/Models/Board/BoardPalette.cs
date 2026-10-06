using Avalonia.Media;

namespace Corkboard.Core.Models.Board;

/// <summary>
///     工具浮窗色板的规则：预设几个颜色、最多几个、怎么把配置里读到的一堆颜色收拾干净。
///     <para>
///         纯逻辑、不碰界面，有单测（见 <c>BoardPaletteTests</c>）。设置页与浮窗都读这里，
///         免得「设置里能加几个」和「浮窗上画几个」两处各写一个数字，改一处漏一处。
///     </para>
/// </summary>
public static class BoardPalette
{
    /// <summary>色板上最多摆几个预设色。到顶之后设置页的「新增」按钮就该禁掉。</summary>
    public const int MaxColors = 5;

    /// <summary>
    ///     没有配置过时用的预设色板。深浅两个主题下都要看得清，所以取的是 Office 那套标准色。
    ///     <para>
    ///         默认给满 <see cref="MaxColors" /> 个：普通用户打开浮窗第一眼就有得选，
    ///         不需要先去设置页配一遍。
    ///     </para>
    /// </summary>
    public static IReadOnlyList<Color> DefaultColors { get; } =
    [
        Color.Parse("#FFFFFFFF"),
        Color.Parse("#FFD13438"),
        Color.Parse("#FFFFB900"),
        Color.Parse("#FF107C10"),
        Color.Parse("#FF0078D4")
    ];

    /// <summary>
    ///     把配置里的颜色收拾成能直接摆上色板的样子，依次做三件事：
    ///     <list type="number">
    ///         <item>丢掉全透明色（画出来什么都没有，等于一个看不见的色块）；</item>
    ///         <item>去掉重复色，保留先出现的那一个（用户看不出两个一样的色块有什么区别）；</item>
    ///         <item>截断到 <see cref="MaxColors" /> 个。</item>
    ///     </list>
    ///     <para>
    ///         一个都不剩（配置为空、或全是非法值）时回退到 <see cref="DefaultColors" />：
    ///         色板空着的话浮窗上就只剩「+」，用户会以为颜色功能坏了。
    ///     </para>
    /// </summary>
    public static List<Color> Normalize(IEnumerable<Color>? colors)
    {
        var result = new List<Color>();

        foreach (var color in colors ?? [])
        {
            // 全透明色画出来什么都没有，等于一个看不见的色块。
            if (color.A == 0)
                continue;

            if (result.Contains(color))
                continue;

            result.Add(color);
            if (result.Count == MaxColors)
                break;
        }

        return result.Count > 0 ? result : [.. DefaultColors];
    }

    /// <summary>还能不能再加一个。设置页据此禁用「新增」按钮。</summary>
    public static bool CanAdd(int currentCount) => currentCount < MaxColors;
}
