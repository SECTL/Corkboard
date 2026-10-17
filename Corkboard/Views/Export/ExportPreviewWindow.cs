using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.Export;

/// <summary>预览窗口的三种结果。</summary>
public enum ExportPreviewChoice
{
    /// <summary>关窗 / 取消。<b>必须是 0</b>：用户直接点窗口关闭时 ShowDialog 返回默认值。</summary>
    Cancel = 0,

    /// <summary>返回对话框改设置。</summary>
    Back = 1,

    /// <summary>确认导出。</summary>
    Export = 2
}

/// <summary>
///     导出预览窗口：把排好版的每一页画出来，确认排版没问题再保存。
///     <para>
///         用独立窗口而不是壳内弹层：一屏要放好几页缩略图，弹层卡片里塞滚动区太局促；
///         而且这是「看一眼就走」的临时窗口，不承载输入，置底到桌面时也不受影响。
///     </para>
///     <para>
///         页面位图由调用方渲染并负责释放；窗口只显示，不持有它们的生命周期。
///     </para>
/// </summary>
public sealed class ExportPreviewWindow : Window
{
    /// <summary>按页位图搭出预览窗口。</summary>
    /// <param name="pages">每一页的 1 倍预览位图（调用方负责 Dispose）。</param>
    /// <param name="boardName">标题栏里显示的作业板名称。</param>
    public ExportPreviewWindow(IReadOnlyList<Bitmap> pages, string boardName)
    {
        Title = string.IsNullOrWhiteSpace(boardName)
            ? CR.Export_PreviewTitle
            : $"{CR.Export_PreviewTitle} - {boardName}";
        // 预览图固定 560 DIP 宽；窗口高度按「一页多高」算，页数少时不要一个大空壳（用户反馈过）。
        const double previewWidth = 560;
        var pageAspect = 1.414;
        if (pages.Count > 0 && pages[0].PixelSize.Width > 0)
            pageAspect = pages[0].PixelSize.Height / (double)pages[0].PixelSize.Width;

        var visiblePages = Math.Min(pages.Count, 2);
        var pageHeight = previewWidth * pageAspect;
        Width = 860;
        Height = Math.Clamp(visiblePages * (pageHeight + 14) + 170, 420, 960);
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;

        // CenterOwner 在无边框 / 置底的主窗口上不总是算得准，开出来再按 owner 的中心硬摆一次。
        Opened += (_, _) => CenterOnOwner();

        var pagesPanel = new StackPanel { Spacing = 14 };
        foreach (var page in pages)
        {
            pagesPanel.Children.Add(new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Image
                {
                    // 预览按固定宽度画，页数多的时候窗口里往下滚就行，不缩成一堆看不清的小图。
                    Width = previewWidth,
                    Stretch = Stretch.Uniform,
                    Source = page
                }
            });
        }

        var backButton = new Button
        {
            Content = CR.Export_PreviewBack,
            MinWidth = 96
        };
        backButton.Click += (_, _) => Close(ExportPreviewChoice.Back);

        var exportButton = new Button
        {
            Content = CR.Export_PreviewExport,
            Classes = { "accent" },
            MinWidth = 120
        };
        exportButton.Click += (_, _) => Close(ExportPreviewChoice.Export);

        var pageCount = new TextBlock
        {
            Text = string.Format(CR.Export_PreviewPageCount, pages.Count),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7
        };

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,8,Auto"),
            Margin = new Thickness(16, 8, 16, 16)
        };
        Grid.SetColumn(pageCount, 0);
        footer.Children.Add(pageCount);
        Grid.SetColumn(backButton, 1);
        footer.Children.Add(backButton);
        Grid.SetColumn(exportButton, 3);
        footer.Children.Add(exportButton);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(16, 16, 16, 0)
        };
        Grid.SetRow(footer, 1);
        root.Children.Add(new ScrollViewer
        {
            Content = pagesPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });
        root.Children.Add(footer);

        Content = root;
    }

    /// <summary>以模态方式打开；返回用户的选择（直接关窗时返回 <see cref="ExportPreviewChoice.Cancel" />）。</summary>
    public new Task<ExportPreviewChoice> ShowDialog(Window owner)
    {
        return ShowDialog<ExportPreviewChoice>(owner);
    }

    /// <summary>把窗口摆到 owner 的正中间（按屏幕像素算，DIP 尺寸乘缩放）。</summary>
    private void CenterOnOwner()
    {
        if (Owner is not { } owner)
            return;

        var center = owner.PointToScreen(new Point(owner.Bounds.Width / 2, owner.Bounds.Height / 2));
        var scaling = owner.RenderScaling <= 0 ? 1 : owner.RenderScaling;
        Position = new PixelPoint(
            center.X - (int)(Bounds.Width * scaling / 2),
            center.Y - (int)(Bounds.Height * scaling / 2));
    }
}
