using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Corkboard.Services.Ui;

namespace Corkboard.Views.Controls;

/// <summary>
///     右下角的轻提示容器（挂在设置壳最上层，铺满窗口但不拦鼠标）。
///     <para>
///         每条提示是一个带淡入淡出的卡片，3 秒后自动收起；最多同时显示 4 条，再多就顶掉最旧的一条。
///         由 <see cref="ToastService" /> 统一转发，页面/ViewModel 不需要知道这个控件。
///     </para>
/// </summary>
public partial class ToastHost : UserControl
{
    /// <summary>同时最多显示几条，超过就把最旧的挤掉。</summary>
    private const int MaxVisible = 4;

    private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(160);

    public ToastHost()
    {
        InitializeComponent();
    }

    /// <summary>弹一条提示；必须在 UI 线程调用（<see cref="ToastService" /> 会负责切线程）。</summary>
    public void Show(string message, ToastSeverity severity = ToastSeverity.Success)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (this.FindControl<StackPanel>("ToastStack") is not { } stack)
            return;

        while (stack.Children.Count >= MaxVisible)
            stack.Children.RemoveAt(0);

        var (iconText, iconClass) = severity switch
        {
            ToastSeverity.Error => ("!", "error"),
            ToastSeverity.Info => ("i", "info"),
            _ => ("✓", "success")
        };

        var icon = new TextBlock { Text = iconText };
        icon.Classes.Add("toast-icon");
        icon.Classes.Add(iconClass);

        var text = new TextBlock { Text = message };
        text.Classes.Add("toast-text");

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { icon, text }
        };

        var card = new Border { Child = content, Opacity = 0 };
        card.Classes.Add("toast-card");

        stack.Children.Add(card);

        card.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = FadeDuration }
        };

        Dispatcher.UIThread.Post(() => card.Opacity = 1, DispatcherPriority.Background);

        var timer = new DispatcherTimer { Interval = VisibleDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            card.Opacity = 0;
            DispatcherTimer.RunOnce(() => stack.Children.Remove(card), FadeDuration);
        };
        timer.Start();
    }
}