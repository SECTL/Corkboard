using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using Corkboard.Core.Models.Board;

namespace Corkboard.Core.Controls;

/// <summary>
///     把「设置 → 作业板 → 快速颜色」里配的色板喂给库自带工具栏的颜色入口。
///     <para>
///         库把色板放在 <see cref="RichEditorToolbar.Palette" /> 这个**静态**数组上（文字颜色与高亮
///         两个弹出层共用），而且是**造工具栏的时候读一次**——已经建出来的工具栏不会跟着变。
///         所以约定是：设置里的色板一变就写一遍这个静态数组，下一个新建的工具栏（每次打开
///         布置作业表单都会新建一个）自然就是新色板，不需要自己去刷已经开着的那个。
///     </para>
///     <para>
///         库对数组有要求：<c>null</c> 与**空数组**会被直接拒绝（分别抛 <c>ArgumentNullException</c> /
///         <c>ArgumentException</c>），元素按 <c>Color.Parse</c> 解析、解析不了的画成黑色而不是报错。
///         我们这边的颜色都过 <see cref="BoardPalette.Normalize" />（丢全透明、去重、截断、空则回退默认色），
///         本来就不会空，这里再兜一层：真为空就原样留着库自带的 40 色色板。
///     </para>
/// </summary>
public static class RichTextToolbarPalette
{
    /// <summary>把一串颜色写进库的静态色板。<paramref name="colors" /> 为空时什么都不做。</summary>
    public static void Apply(IEnumerable<Color>? colors)
    {
        var entries = (colors ?? []).Select(ToHex).ToArray();

        if (entries.Length == 0)
            return;

        RichEditorToolbar.Palette = entries;
    }

    /// <summary>
    ///     转成库认的 hex 写法：不透明色写 6 位（与库自带色板一致），带透明度才写 8 位。
    /// </summary>
    private static string ToHex(Color color) =>
        color.A == byte.MaxValue
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
