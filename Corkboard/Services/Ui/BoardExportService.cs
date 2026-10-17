using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Services.Ui;

/// <summary>
///     导出作业板图片：把板子上面那块内容（不是整窗）渲染成 PNG 交给用户保存。
///     <para>
///         只渲染传入的控件，所以不会带上窗口底色、自绘标题栏和桌面背景；
///         内容比窗口高时控件本身是完整排版的，渲染整块就得到完整长图。
///     </para>
///     <para>
///         约定：用户取消保存对话框返回 <c>false</c>；用户取消之外失败一律抛
///         <see cref="InvalidOperationException" />（调用方显示 <c>CR.Board_ExportFailed</c>）；
///         成功返回 <c>true</c>。必须在 UI 线程上调用（渲染与保存对话框都要求）。
///     </para>
/// </summary>
public sealed class BoardExportService(ILogger<BoardExportService> logger)
{
    /// <summary>
    ///     单边像素上限。整块板子可能很长，乘上高清屏的 <c>RenderScaling</c> 后会超过 GPU 纹理上限，
    ///     所以整体等比缩小，宁可分辨率低一点也不要导出失败或者被裁掉。
    /// </summary>
    private const int MaxPixelDimension = 16384;

    /// <summary>PNG 文件类型（保存对话框里的「文件类型」下拉）。</summary>
    private static readonly FilePickerFileType PngFileType = new("PNG")
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"],
        AppleUniformTypeIdentifiers = ["public.png"]
    };

    /// <summary>底色候选资源：Fluent 的「实体底色」是纯色、不透明，在浅色/深色字典里都有。</summary>
    private static readonly string[] BackgroundResourceKeys =
    [
        "SolidBackgroundFillColorBaseBrush",
        "SolidBackgroundFillColorQuarternaryBrush",
        "SystemRegionBrush"
    ];

    /// <summary>
    ///     把 <paramref name="content" /> 存成 PNG。返回是否真的保存了文件（用户取消返回 <c>false</c>）。
    /// </summary>
    public async Task<bool> ExportImageAsync(
        Control content,
        TopLevel topLevel,
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(topLevel);
        cancellationToken.ThrowIfCancellationRequested();

        // 保存对话框要先弹：用户在对话框里取消就不折腾排版了。
        // 尺寸等对话框关掉之后再量——对话框显示期间窗口可能被激活/改变尺寸，那时排好的版会失效。
        IStorageFile? file;
        try
        {
            // 1) 让用户选保存路径。文件类型只给 PNG，文件名带上日期与时间。
            file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = CR.Board_ExportImage,
                SuggestedFileName = BuildSuggestedFileName(suggestedFileName),
                DefaultExtension = "png",
                ShowOverwritePrompt = true,
                FileTypeChoices = [PngFileType]
            }).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "打开导出图片的保存对话框失败");
            throw new InvalidOperationException(CR.Board_ExportFailed, exception);
        }

        // 用户按了取消：不是失败，也不用写日志。
        if (file is null)
            return false;

        cancellationToken.ThrowIfCancellationRequested();

        // 2) 等这一轮排版跑完再量：窗口刚缩放完就点导出时，控件可能还挂着上一次的排布，
        //    量出来会和真正画出来的样子对不上（导出的图里区块被裁在画布外就是这么来的）。
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Render);

        var size = MeasureContent(content);
        if (size.Width < 1 || size.Height < 1)
            throw new InvalidOperationException("作业板内容还没有排版，导不出图片。");

        try
        {
            // 3) 尺寸换算成像素：控件是 DIP，高清屏要乘 RenderScaling，否则导出的图在 2x 屏上偏小发糊。
            var scale = ResolveScale(topLevel.RenderScaling, size, out var wasCapped);
            if (wasCapped)
                logger.LogWarning(
                    "作业板内容太长（{Width:F0}x{Height:F0}），导出图片按 {Scale:F2} 缩放过",
                    size.Width, size.Height, scale);

            var pixelWidth = Math.Max(1, (int)Math.Ceiling(size.Width * scale));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(size.Height * scale));
            var pixelSize = new PixelSize(pixelWidth, pixelHeight);

            // 先把控件画进一张「1 像素 = 1 DIP」的位图。
            // ⚠️ 这张位图的 DPI 必须是 96：RenderTargetBitmap.Render 会再乘一次位图的 DPI 缩放，
            // 直接给它 96 × RenderScaling 的 DPI，控件就被放大两遍（1.5 倍屏上一次 1.5 倍、一次 1.5 倍，
            // 实际 2.25 倍），右半边区块被画到画布外——用户看到的「导出图片右边被裁」就是这么来的。
            // 放大到像素尺寸放在下一步合成时做。
            var dipBitmapSize = new PixelSize(
                Math.Max(1, (int)Math.Ceiling(size.Width)),
                Math.Max(1, (int)Math.Ceiling(size.Height)));
            using var contentBitmap = new RenderTargetBitmap(dipBitmapSize, new Vector(96, 96));
            contentBitmap.Render(content);

            // 4) 先铺一层不透明的主题底色，再把内容叠上去（顺便按 DPI 放大）——控件自身的底色多半是透明的，
            //    直接输出会是透明底 PNG，粘到白色文档上字就看不见了。
            using var outputBitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scale, 96 * scale));
            using (var context = outputBitmap.CreateDrawingContext())
            {
                var bounds = new Rect(size);
                context.FillRectangle(ResolveBackgroundBrush(content), bounds);
                context.DrawImage(contentBitmap, bounds);
            }

            // 5) 存成 PNG。用户选的路径可能是个已有文件，先截断再写，避免留着旧内容。
            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            if (stream.CanSeek)
            {
                stream.SetLength(0);
                stream.Position = 0;
            }

            // Avalonia 12 的 Bitmap.Save(Stream) 已过时，显式给 PNG 编码器选项。
            outputBitmap.Save(stream, PngBitmapEncoderOptions.Default);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(true);

            logger.LogInformation("作业板图片已导出：{FileName}", file.Name);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "导出作业板图片失败");
            throw new InvalidOperationException(CR.Board_ExportFailed, exception);
        }
    }

    /// <summary>
    ///     算「整张图」应有的尺寸（DIP）：宽度跟当前视口一样，高度用内容自己的自然高度。
    ///     <para>
    ///         滚动区里的内容会被排到「至少视口那么高」，所以 <see cref="Layoutable.Bounds" /> 的高度
    ///         在内容比窗口矮时是视口高——直接用会把下面一大片空白也导出去。这里用
    ///         <see cref="Layoutable.DesiredSize" />（内容自然高度，含外边距）减掉外边距，再拿
    ///         <c>Bounds</c> 兜底；内容比窗口高时 <c>Bounds</c> 本来就是完整高度，两者取小的那个即可，
    ///         因为内容是从上往下排的，多出来的只可能是底部的空白。
    ///     </para>
    /// </summary>
    private static Size MeasureContent(Control content)
    {
        var width = content.Bounds.Width;
        if (!(width >= 1))
            width = content.DesiredSize.Width;

        var margin = content.Margin;
        var height = content.DesiredSize.Height - margin.Top - margin.Bottom;
        var boundsHeight = content.Bounds.Height;
        if (!(height >= 1) || height > boundsHeight)
            height = boundsHeight;

        if (!(width >= 1) || !(height >= 1))
            return default;

        return new Size(width, height);
    }

    /// <summary>
    ///     输出缩放系数：跟窗口一致（高清屏不糊），但单边超过纹理上限时整体等比缩小，
    ///     保证整块内容都在图里、不会被裁掉。
    /// </summary>
    private static double ResolveScale(double renderScaling, Size size, out bool wasCapped)
    {
        var scale = double.IsFinite(renderScaling) && renderScaling > 0 ? renderScaling : 1;
        wasCapped = false;

        var longestSide = Math.Max(size.Width, size.Height);
        if (longestSide * scale > MaxPixelDimension)
        {
            scale = MaxPixelDimension / longestSide;
            wasCapped = true;
        }

        return Math.Max(scale, 0.01);
    }

    /// <summary>保存对话框里预填的文件名：保证是 .png，空名时用「导出图片-日期-时间」兜底。</summary>
    private static string BuildSuggestedFileName(string suggestedFileName)
    {
        var name = string.IsNullOrWhiteSpace(suggestedFileName)
            ? $"{CR.Board_ExportImage}-{DateTime.Now:yyyyMMdd-HHmm}"
            : suggestedFileName.Trim();

        return name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.png";
    }

    /// <summary>
    ///     取导出图的底笔刷：控件自己的底色 → 主题的实体底色 → 白色。
    ///     只认完全不透明的纯色，半透明底色叠出来还是透明的，不能要。
    /// </summary>
    private static IBrush ResolveBackgroundBrush(Control content)
    {
        if (TryGetContentBackground(content, out var contentBackground))
            return contentBackground;

        var themeVariant = content.ActualThemeVariant;
        foreach (var key in BackgroundResourceKeys)
            if (content.TryFindResource(key, themeVariant, out var resource)
                && TryGetOpaqueBrush(resource, out var brush))
                return brush;

        if (Application.Current is { } application)
            foreach (var key in BackgroundResourceKeys)
                if (application.TryFindResource(key, themeVariant, out var resource)
                    && TryGetOpaqueBrush(resource, out var brush))
                    return brush;

        // 什么资源都没找到（主题异常自定义之类）：白底最稳，至少保证不透明。
        return Brushes.White;
    }

    /// <summary>内容容器自己要是铺了不透明底色，导出图就跟着它走，看起来跟页面上一样。</summary>
    private static bool TryGetContentBackground(Control content, out IBrush background)
    {
        IBrush? candidate = content switch
        {
            Panel panel => panel.Background,
            Border border => border.Background,
            TemplatedControl templatedControl => templatedControl.Background,
            _ => null
        };

        return TryGetOpaqueBrush(candidate, out background);
    }

    /// <summary>只接受纯色且 alpha 满值的笔刷，保证导出图一定有实体底。</summary>
    private static bool TryGetOpaqueBrush(object? candidate, out IBrush brush)
    {
        switch (candidate)
        {
            case ISolidColorBrush { Color.A: byte.MaxValue } solid:
                brush = solid;
                return true;
            default:
                brush = Brushes.White;
                return false;
        }
    }
}
