using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Corkboard.Core.RichText;
using CoreResources = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Controls;

/// <summary>
///     编辑器里点中已有链接时弹的小浮层：打开 / 编辑 / 移除。
///     <para>
///         为什么要拦：库把「单击链接」直接绑成了打开浏览器（<c>OpenUrl</c>），布置作业时想改个地址
///         或者只是手滑点一下，浏览器就被拉起来了。这里在 <see cref="RichTextBlock.OnPointerPressed" />
///         里拦在库前面，先问用户要做什么；真要打开时再由宿主转给 <c>IExternalLauncher</c>
///         （库自己的 <c>Process.Start</c> 绕过了统一的打开入口）。
///     </para>
///     <para>
///         编辑 / 移除走 <see cref="RichTextLinks.TrySetHyperlinkAt" />；库版本对不上时
///         <see cref="RichTextBlock.IsHyperlinkEditSupported" /> 为假，<see cref="RichTextBlock" /> 根本不会拦，
///         行为退回「单击直接打开」。
///     </para>
/// </summary>
public static class RichTextLinkMenu
{
    /// <summary>在点击位置弹出链接菜单；<paramref name="open" /> 由宿主接到统一的打开入口。</summary>
    public static void Show(RichTextBlock editor, string url, Point position, Action<string> open)
    {
        var flyout = new Flyout { Placement = PlacementMode.Pointer };

        // 顶部先给一眼完整地址（卡片上悬停提示也是这一份），再放动作。
        var address = new TextBlock
        {
            Text = url,
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        };

        var menu = new StackPanel { Spacing = 2 };

        var urlBox = new TextBox
        {
            Text = url,
            PlaceholderText = CoreResources.Board_LinkUrlPlaceholder,
            Classes = { RichTextToolbarTheme.KeepThemeClass },
        };

        var error = new TextBlock
        {
            Text = CoreResources.Board_LinkInvalid,
            Foreground = new SolidColorBrush(Color.Parse("#C42B1C")),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        var apply = new Button { Content = CoreResources.Board_LinkApply };
        var cancel = new Button { Content = CoreResources.Common_Cancel };

        var edit = new StackPanel
        {
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                new TextBlock { Text = CoreResources.Board_LinkUrl },
                urlBox,
                error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, apply },
                },
            },
        };

        // 菜单项在所有控件都建好之后再挂：点击处理器会用到上面这些局部变量，
        // 提前放进集合初始化器会让编译器报「使用了未赋值的局部变量」。
        menu.Children.Add(MenuItem(CoreResources.Board_LinkOpen, () => OpenLink()));
        menu.Children.Add(MenuItem(CoreResources.Board_LinkEdit, () => StartEdit()));
        menu.Children.Add(MenuItem(CoreResources.Board_LinkRemove, () => RemoveLink()));

        var panel = new StackPanel
        {
            Width = 260,
            Spacing = 10,
            Children = { address, menu, edit },
        };

        // 浮层内容要标 KeepThemeClass：RichTextToolbarTheme 按颜色精确匹配替换库写死的浅色，
        // 不排除的话暗色主题下这里的前景会被换成深底、字看不见（见 RichTextLinkButton 同一处注释）。
        panel.Classes.Add(RichTextToolbarTheme.KeepThemeClass);

        flyout.Content = panel;

        // 关掉后把焦点还给编辑器，免得接着打字落在别处。
        flyout.Closed += (_, _) => editor.Focus();

        apply.Click += (_, _) => ApplyEdit();
        cancel.Click += (_, _) => flyout.Hide();

        urlBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            ApplyEdit();
            e.Handled = true;
        };

        flyout.ShowAt(editor);

        void ApplyEdit()
        {
            if (!HyperlinkUrl.TryNormalize(urlBox.Text, out var edited))
            {
                error.IsVisible = true;
                return;
            }

            error.IsVisible = false;
            editor.TrySetHyperlinkAt(position, edited);
            flyout.Hide();
        }

        void OpenLink()
        {
            flyout.Hide();
            open(url);
        }

        void RemoveLink()
        {
            editor.TrySetHyperlinkAt(position, null);
            flyout.Hide();
        }

        void StartEdit()
        {
            menu.IsVisible = false;
            edit.IsVisible = true;
            urlBox.Text = url;
            error.IsVisible = false;

            // 浮层刚展开时控件还没量过，直接 Focus 会被布局吞掉。
            Dispatcher.UIThread.Post(() => urlBox.Focus(), DispatcherPriority.Input);
        }
    }

    private static Button MenuItem(string text, System.Action click)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(4),
        };

        button.Click += (_, _) => click();
        return button;
    }
}
