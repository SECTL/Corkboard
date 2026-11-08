using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using Corkboard.Core.Icons;
using Corkboard.Core.RichText;
using CoreResources = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Controls;

/// <summary>
///     给库自带格式工具条的尾巴补一颗「插入链接」按钮（<c>RichEditorToolbar.TrailingItems</c>）。
///     <para>
///         为什么要补：库只有<b>右键菜单</b>里的「插入链接」（而且要选中文字才可用），
///         工具栏上一个入口都没有；没有选中文字时更是完全插不了链接。
///         这颗按钮把两种用法都收进来：
///         <list type="bullet">
///             <item>有选中文字：把链接套在选中的文字上（<see cref="RichTextBlock.TrySetHyperlink" />）；</item>
///             <item>没有选中文字：在光标处插入「显示文字」+ 地址，文字留空就用地址本身。</item>
///         </list>
///         浮层里还有一颗「移除链接」，对选中文字去掉链接（库的右键菜单里也有，这里只是顺手）。
///     </para>
///     <para>
///         用反射的那部分见 <see cref="RichTextLinks" />；库版本对不上时整颗按钮不出现，
///         不会留下一个点了没反应的入口。重复调用是幂等的（只加一次）。
///     </para>
/// </summary>
public static class RichTextLinkButton
{
    /// <summary>已经补过按钮的工具条；工具条换语言 / 换级别重建时会重新 Add 一遍 TrailingItems，这里防重复。</summary>
    private static readonly ConditionalWeakTable<RichEditorToolbar, object> Attached = new();

    /// <summary>给工具条补「插入链接」按钮；已经补过就什么都不做。</summary>
    public static void Attach(RichEditorToolbar toolbar)
    {
        if (!RichTextBlock.IsHyperlinkSupported)
            return;

        if (Attached.TryGetValue(toolbar, out _))
            return;

        Attached.Add(toolbar, new object());
        toolbar.TrailingItems.Add(BuildButton(toolbar));
    }

    private static Button BuildButton(RichEditorToolbar toolbar)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = FluentIcons.LinkFilled,
                FontFamily = GlobalConstants.FluentIconsFontFamily,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 3),
            Margin = new Thickness(1, 0),
            MinWidth = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { RichTextToolbarTheme.KeepThemeClass },
        };
        ToolTip.SetTip(button, CoreResources.Board_Link);

        var urlBox = new TextBox
        {
            PlaceholderText = CoreResources.Board_LinkUrlPlaceholder,
            Classes = { RichTextToolbarTheme.KeepThemeClass },
        };

        var textBox = new TextBox
        {
            PlaceholderText = CoreResources.Board_LinkTextPlaceholder,
            Classes = { RichTextToolbarTheme.KeepThemeClass },
        };

        var textRow = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = CoreResources.Board_LinkText },
                textBox,
            },
        };

        var selectionHint = new TextBlock
        {
            Text = CoreResources.Board_LinkSelectionHint,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        var error = new TextBlock
        {
            Text = CoreResources.Board_LinkInvalid,
            Foreground = new SolidColorBrush(Color.Parse("#C42B1C")),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        var remove = new Button
        {
            Content = CoreResources.Board_LinkRemove,
            IsVisible = false,
        };

        var apply = new Button { Content = CoreResources.Board_LinkApply };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { remove, apply },
        };

        var panel = new StackPanel
        {
            Width = 300,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = CoreResources.Board_LinkUrl },
                urlBox,
                selectionHint,
                textRow,
                error,
                actions,
            },
        };

        // 浮层内容要标 KeepThemeClass：RichTextToolbarTheme 按颜色精确匹配替换库写死的浅色，
        // 我们这颗浮层里的正常前景色在暗色主题下正好命中，不排除就会字看不见。
        panel.Classes.Add(RichTextToolbarTheme.KeepThemeClass);

        var flyout = new Flyout { Content = panel };

        button.Flyout = flyout;

        // 打开时按「有没有选中文字」切换两种用法：有选区就只显示「套在选中文字上」，
        // 没选区才需要填显示文字。地址框每次清空，免得上一次的地址被误用。
        flyout.Opened += (_, _) =>
        {
            var hasSelection = toolbar.Target is RichTextBlock { HasTextSelection: true };
            textRow.IsVisible = !hasSelection;
            selectionHint.IsVisible = hasSelection;
            remove.IsVisible = hasSelection;
            error.IsVisible = false;
            urlBox.Text = string.Empty;
            textBox.Text = string.Empty;

            // 浮层刚打开时控件还没量过，直接 Focus 会被布局吞掉。
            Dispatcher.UIThread.Post(() => urlBox.Focus(), DispatcherPriority.Input);
        };

        // 关掉浮层把焦点还给编辑器，不然接着打字会落到别处。
        flyout.Closed += (_, _) => toolbar.Target?.Focus();

        void Apply()
        {
            if (toolbar.Target is not RichTextBlock editor)
                return;

            if (!HyperlinkUrl.TryNormalize(urlBox.Text, out var url))
            {
                error.IsVisible = true;
                return;
            }

            error.IsVisible = false;

            if (editor.HasTextSelection)
                editor.TrySetHyperlink(url);
            else
                editor.InsertHyperlink(url, textBox.Text);

            flyout.Hide();
        }

        apply.Click += (_, _) => Apply();
        remove.Click += (_, _) =>
        {
            (toolbar.Target as RichTextBlock)?.TrySetHyperlink(null);
            flyout.Hide();
        };

        // 回车即应用（地址框 / 显示文字框都行），Esc 交给 Flyout 自己关。
        urlBox.KeyDown += (_, e) => SubmitOnEnter(e, Apply);
        textBox.KeyDown += (_, e) => SubmitOnEnter(e, Apply);

        return button;
    }

    private static void SubmitOnEnter(KeyEventArgs e, System.Action apply)
    {
        if (e.Key != Key.Enter)
            return;

        apply();
        e.Handled = true;
    }
}
