using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaRichEditor.Controls;
using CoreResources = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Controls;

/// <summary>
///     给库自带的两个颜色浮层（文字颜色 / 高亮）添一个「自定义颜色」入口，点一下弹出我们自己的取色器。
///     <para>
///         库那两个浮层只有「固定色板 + 手输 #RRGGBB」（<c>RichEditorToolbar.BuildColorButton</c>：<c>Palette</c>
///         里的色块 + 一个 <c>PlaceholderText = "#RRGGBB"</c> 的输入框与「应用」按钮），想自己挑个颜色没地方
///         ——手输色值不算「挑色」（用户提的）。固定色板要保留，所以只<b>在浮层尾巴上补一颗按钮</b>，
///         按钮自己带一个浮层，里面才是 Avalonia 的 <see cref="ColorView" />（光谱 + 分量，始终展开；
///         用 <c>ColorPicker</c> 的话它自带下拉浮层，套在这里会变成两层浮层）。
///     </para>
///     <para>
///         浮层里那支取色器挑完按「应用」，走的是库的公开命令 <c>SetForeground</c> / <c>SetHighlight</c>
///         （作用在编辑器当前选区上）。要说清自己是哪一个浮层：库造高亮浮层时会多塞一个「清除高亮」按钮，
///         而色板格子在 <c>UniformGrid</c> 里、手输入框那一行在 <c>StackPanel</c> 里，所以
///         <b>浮层根 <c>StackPanel</c> 的直接子级里有按钮</b>就说明是高亮那一份。
///     </para>
///     <para>
///         添上去的控件要标 <see cref="RichTextToolbarTheme.KeepThemeClass" />：主题替换是「颜色值精确匹配」的，
///         我们自己的正常前景色（暗色下的纯白）正好命中那张表，不排除会被换成深底、字就看不见了。
///         重复调用是安全的（工具条换语言/换级别会整棵重建、浮层内容也是新造的一份）。
///     </para>
/// </summary>
public static class RichTextCustomColor
{
    /// <summary>已经补过入口的浮层内容，用来识别重建后新造的那一份。</summary>
    private static readonly ConditionalWeakTable<Panel, object> Attached = new();

    /// <summary>给工具条自带的两个颜色浮层各补一颗「自定义颜色」按钮；重复调用只处理新冒出来的浮层。</summary>
    public static void Attach(RichEditorToolbar toolbar)
    {
        foreach (var button in toolbar.GetVisualDescendants().OfType<Button>())
        {
            // 颜色浮层的判据：浮层内容里有那个手输色值的输入框
            // （字号/字体/段落样式/对齐都是下拉，不是浮层；插入表格的浮层没有输入框）。
            if (button.Flyout is not Flyout { Content: Panel root } flyout) continue;
            if (Attached.TryGetValue(root, out _)) continue;
            if (!Descendants(root).OfType<TextBox>().Any()) continue;

            Attached.Add(root, new object());
            root.Children.Add(BuildEntryButton(toolbar, IsHighlightFlyout(root)));
        }
    }

    /// <summary>高亮那一份浮层会多一个「清除高亮」按钮，而且它是浮层根面板的直接子级。</summary>
    private static bool IsHighlightFlyout(Panel root) => root.Children.OfType<Button>().Any();

    private static Button BuildEntryButton(RichEditorToolbar toolbar, bool highlight)
    {
        var entry = new Button
        {
            Content = CoreResources.Board_CustomColor,
            HorizontalAlignment = HorizontalAlignment.Left,
            Classes = { RichTextToolbarTheme.KeepThemeClass },
        };

        var picker = new ColorView
        {
            IsAlphaEnabled = false,
            Color = Color.Parse("#FF0078D4"),
            Classes = { RichTextToolbarTheme.KeepThemeClass },
        };

        var apply = new Button
        {
            Content = CoreResources.Board_ApplyColor,
            HorizontalAlignment = HorizontalAlignment.Right,
            Classes = { RichTextToolbarTheme.KeepThemeClass },
        };

        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 8,
                Classes = { RichTextToolbarTheme.KeepThemeClass },
                Children = { picker, apply },
            },
        };

        // 打开时先对齐当前颜色：光标处那个 run 的颜色（还没有就用取色器自己的默认色）。
        flyout.Opened += (_, _) =>
        {
            if (toolbar.Target?.GetCaretFormat().Foreground is ISolidColorBrush brush)
                picker.Color = brush.Color;
        };

        apply.Click += (_, _) =>
        {
            if (toolbar.Target is { } editor)
            {
                var brush = new SolidColorBrush(picker.Color);
                if (highlight)
                    editor.SetHighlight(brush);
                else
                    editor.SetForeground(brush);
            }

            flyout.Hide();
        };

        entry.Flyout = flyout;
        return entry;
    }

    /// <summary>浮层内容在打开之前不在可视树上，只能按逻辑子树找那只输入框。</summary>
    private static IEnumerable<Control> Descendants(ILogical root)
    {
        foreach (var child in root.LogicalChildren)
        {
            if (child is not Control control) continue;

            yield return control;
            foreach (var descendant in Descendants(control)) yield return descendant;
        }
    }
}
