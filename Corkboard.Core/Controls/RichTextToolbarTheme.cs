using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Corkboard.Core.Controls;

/// <summary>
///     把富文本工具栏里写死的浅色画刷换成本应用主题里的对应画刷。
///     <para>
///         库（AvaloniaRichEditor 1.2.1）完全不认主题：反编译整份程序集，<c>ThemeVariant</c> /
///         <c>DynamicResource</c> / <c>TryFindResource</c> 一处都没有。工具条底色写死 <c>#F5F6F8</c>、
///         下拉与输入框底色写死白、描边写死 <c>#DCDCDC</c>、图标墨色写死 <c>#3C4043</c> / <c>#70757A</c> /
///         <c>#80868B</c>（<c>RichEditorToolbar.cs</c> 与 <c>ToolbarIcons.cs</c> 里共四十来处硬编码颜色），
///         于是暗色主题下会浮出一条亮色工具条，跟深色卡片割裂。
///     </para>
///     <para>
///         这里按<b>颜色值精确匹配</b>把这几支画刷换成主题画刷（主题资源里没有同名键时退回按当前主题选的兜底色）：
///         亮色主题下顺带把库的灰换成应用的灰，暗色主题下跟着变深。刻意不按亮度猜、也不动色板格子
///         （<see cref="UniformGrid" /> 里的用户配色要保持原样），匹配不到的颜色一律不碰。
///     </para>
///     <para>
///         换过的画刷颜色不再等于表里的键，所以重复调用只对<b>新冒出来的</b>控件生效，可以安全地接在
///         <c>LayoutUpdated</c> 上：库在换 <c>ToolbarLevel</c> / 切语言时会整棵重建工具条，重建出来的
///         按钮会在下一次布局时被换一遍。代价是「工具条还在屏幕上时切主题」不会跟着变（换过的颜色已经不认识了）
///         ——表单是即用即弃的弹层，够用；哪天要支持，就得按属性记住原始颜色再重算。
///     </para>
/// </summary>
public static class RichTextToolbarTheme
{
    /// <summary>库写死的颜色 → 替换用的主题资源键，以及取不到资源时按主题用的兜底色。</summary>
    private static readonly Dictionary<Color, Replacement> Replacements = new()
    {
        // 工具条整条底色。换成**透明**：工具条跟它所在的「作业内容框」同色（用户要求「和背景一个色」），
        // 不再是一条有自己底色的横条；按钮/下拉各自的小底还在（见下面的白色条目），仍看得出是一排控件。
        [Color.Parse("#F5F6F8")] = Replacement.Transparent,

        // 下拉、输入框、弹出层的底色。
        [Colors.White] = new("ControlFillColorDefaultBrush", "#2D2D2D", "#FFFFFF"),

        // 描边。
        [Color.Parse("#DCDCDC")] = new("ControlStrokeColorDefaultBrush", "#3F3F3F", "#DCDCDC"),
        [Color.Parse("#DDDDDD")] = new("ControlStrokeColorDefaultBrush", "#3F3F3F", "#DDDDDD"),

        // 图标与文字墨色。
        [Color.Parse("#3C4043")] = new("TextFillColorPrimaryBrush", "#FFFFFF", "#1B1B1B"),
        [Colors.Black] = new("TextFillColorPrimaryBrush", "#FFFFFF", "#000000"),
        [Color.Parse("#70757A")] = new("TextFillColorSecondaryBrush", "#C5C5C5", "#70757A"),
        [Color.Parse("#80868B")] = new("TextFillColorSecondaryBrush", "#C5C5C5", "#80868B"),
        [Color.Parse("#BFC3C7")] = new("TextFillColorTertiaryBrush", "#8A8A8A", "#BFC3C7"),

        // 选中/激活态。
        [Color.Parse("#90CAF9")] = new("AccentFillColorDefaultBrush", "#60CDFF", "#0078D4"),
    };

    /// <summary>
    ///     带上这个样式类的子树整棵不换颜色。宿主往工具栏里塞自己的控件（比如「自定义颜色」那颗色轮按钮
    ///     与它的取色浮层）时用得上：那些控件本来就跟着应用主题走，再按库的硬编码表换一遍会把它们
    ///     正常的前景色（暗色主题下的纯白）当成库写死的白，换成深色底 → 暗色下字看不见。
    /// </summary>
    public const string KeepThemeClass = "rte-keep-theme";

    /// <summary>库写死的墨色，对不上表里任何替换时当兜底前景色。</summary>
    private static readonly ImmutableSolidColorBrush Ink = new(Color.Parse("#3C4043"));

    /// <summary>已经挂过弹出层监听的浮层，避免重复订阅。</summary>
    private static readonly ConditionalWeakTable<FlyoutBase, object> HookedFlyouts = new();

    private static readonly object Hook = new();

    /// <summary>把 <paramref name="root" /> 子树里库写死的浅色换成本应用主题的画刷。</summary>
    public static void Apply(Visual root)
    {
        ArgumentNullException.ThrowIfNull(root);

        Remap(root, root);
        foreach (var visual in root.GetVisualDescendants())
            Remap(visual, root);

        HookFlyouts(root);
    }

    private static void Remap(Visual visual, Visual host)
    {
        // 色板格子里的色块是用户自己配的颜色，一个都不能换；标了 KeepThemeClass 的子树同理（宿主自己的控件）。
        if (visual is UniformGrid || HasUniformGridAncestor(visual) || IsKeptSubtree(visual))
            return;

        switch (visual)
        {
            // 必须排在 TemplatedControl 前面：下拉框的显示文字（字体名 / 字号 / 段落样式 / 对齐）库一个
            // 前景色都没设，显示出来的是模板里那套**深色字**，暗色下就是深字贴深底。这里按当前主题
            // 显式给一次主文字色——亮色下等于原来的深灰，暗色下就是用户要的白色。
            case ComboBox combo:
                combo.Foreground = ThemedText(combo);
                combo.Background = Themed(combo.Background, host);
                combo.BorderBrush = Themed(combo.BorderBrush, host);
                break;

            case ComboBoxItem item:
                item.Foreground = ThemedText(item);
                item.Background = Themed(item.Background, host);
                break;

            case Border border:
                border.Background = Themed(border.Background, host);
                border.BorderBrush = Themed(border.BorderBrush, host);
                break;

            case Shape shape:
                shape.Fill = Themed(shape.Fill, host);
                shape.Stroke = Themed(shape.Stroke, host);
                break;

            case TextBlock textBlock:
                textBlock.Foreground = Themed(textBlock.Foreground, host);
                break;

            case ContentPresenter presenter:
                presenter.Background = Themed(presenter.Background, host);
                presenter.BorderBrush = Themed(presenter.BorderBrush, host);
                break;

            case TemplatedControl templated:
                templated.Background = Themed(templated.Background, host);
                templated.BorderBrush = Themed(templated.BorderBrush, host);
                templated.Foreground = Themed(templated.Foreground, host);
                break;

            case Panel panel:
                panel.Background = Themed(panel.Background, host);
                break;
        }
    }

    /// <summary>颜色对上表里的键就换成主题画刷，否则原样返回。</summary>
    private static IBrush? Themed(IBrush? brush, Visual host)
    {
        if (brush is not ISolidColorBrush solid || !Replacements.TryGetValue(solid.Color, out var replacement))
            return brush;

        // 空资源键 = 这条要换成透明（工具条整条底色）。
        if (replacement.ResourceKey.Length == 0)
            return Brushes.Transparent;

        // ⚠️ 必须显式把主题变体传进去：不带变体的 TryFindResource 走的是「默认」变体，
        // 暗色应用里查到的是**亮色**那一套（SolidBackgroundFillColorSecondary 恰好就是 #F9F9F9，
        // 跟库写死的 #F5F6F8 几乎一样），于是工具条在暗色下依然是亮的——踩过这个坑。
        var variant = ResolveVariant(host);
        if (host.TryFindResource(replacement.ResourceKey, variant, out var resource) && resource is IBrush themed)
            return themed;

        return new ImmutableSolidColorBrush(variant == ThemeVariant.Dark ? replacement.Dark : replacement.Light);
    }

    /// <summary>当前主题下的主文字色（亮色深灰、暗色白），给库没设前景色的下拉框用。</summary>
    private static IBrush ThemedText(Visual host) => Themed(Ink, host) ?? Ink;

    /// <summary>
    ///     这个控件当前按哪套主题渲染。控件还没挂进窗口时 <see cref="StyledElement.ActualThemeVariant" />
    ///     可能是 <see cref="ThemeVariant.Default" />，这时退回应用级主题，免得在暗色界面里烘出一套亮色兜底色。
    /// </summary>
    private static ThemeVariant ResolveVariant(Visual host)
    {
        var variant = host.ActualThemeVariant;
        if (variant == ThemeVariant.Default && Application.Current is { } app)
            variant = app.ActualThemeVariant;

        return variant == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static bool HasUniformGridAncestor(Visual visual)
    {
        for (var parent = visual.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            if (parent is UniformGrid)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     自己或任一祖先标了 <see cref="KeepThemeClass" /> 就整棵跳过：宿主往工具栏里塞的控件
    ///     （色轮按钮、取色浮层）本来就是跟着应用主题走的，不该按库的硬编码颜色表再换一遍。
    /// </summary>
    private static bool IsKeptSubtree(Visual visual)
    {
        if (IsMarked(visual))
            return true;

        for (var parent = visual.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            if (IsMarked(parent))
                return true;
        }

        return false;

        static bool IsMarked(Visual candidate) =>
            candidate is Control control && control.Classes.Contains(KeepThemeClass);
    }

    /// <summary>
    ///     颜色 / 高亮的弹出层是独立的视觉树（挂在弹出宿主下），不在工具栏子树里，
    ///     而且要等打开那一刻才建好，所以只能等它打开再去换一遍。
    /// </summary>
    private static void HookFlyouts(Visual root)
    {
        foreach (var button in root.GetVisualDescendants().OfType<Button>())
        {
            if (button.Flyout is not { } flyout || HookedFlyouts.TryGetValue(flyout, out _))
                continue;

            HookedFlyouts.Add(flyout, Hook);
            flyout.Opened += (_, _) =>
            {
                if (flyout is Flyout { Content: Visual content })
                    Apply(content);
            };
        }
    }

    /// <summary>一支颜色替换：主题资源键 + 暗色兜底色 + 亮色兜底色（十六进制串，构造时解析成颜色）。</summary>
    private sealed record Replacement(string ResourceKey, string DarkHex, string LightHex)
    {
        /// <summary>资源键为空 = 换成透明（跟所在容器同色）。</summary>
        public static readonly Replacement Transparent = new(string.Empty, "#00000000", "#00000000");

        public Color Dark { get; } = Color.Parse(DarkHex);

        public Color Light { get; } = Color.Parse(LightHex);
    }
}
