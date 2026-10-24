using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Corkboard.Core.Export;
using Corkboard.Core.Notes;
using Corkboard.Core.Services.Config;
using Corkboard.Services.Ui.Export;
using Microsoft.Extensions.Logging;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Services.Ui;

/// <summary>
///     导出作业板：把板子上面那块内容（不是整窗）渲染成 PNG，或者把作业写成一张 xlsx 表格。
///     <para>
///         只渲染传入的控件，所以不会带上窗口底色、自绘标题栏和桌面背景；
///         内容比窗口高时控件本身是完整排版的，渲染整块就得到完整长图。
///     </para>
///     <para>
///         约定：用户取消保存对话框返回 <c>false</c>；用户取消之外失败一律抛
///         <see cref="InvalidOperationException" />（调用方显示 <c>Export_Failed</c>）；
///         成功返回 <c>true</c>。必须在 UI 线程上调用（渲染与保存对话框都要求）。
///     </para>
/// </summary>
public sealed class BoardExportService(ILogger<BoardExportService> logger, MainConfigHandler configHandler)
{
    /// <summary>
    ///     单边像素上限。整块板子可能很长，乘上高清屏的 <c>RenderScaling</c> 后会超过 GPU 纹理上限，
    ///     所以整体等比缩小，宁可分辨率低一点也不要导出失败或者被裁掉。
    /// </summary>
    private const int MaxPixelDimension = 16384;

    /// <summary>
    ///     导出图左右各留的边距（DIP）。贴边导出时卡片描边正好压在图片边缘上，
    ///     看起来像被裁掉一块；留一条底色的边，整张图才立得住。
    /// </summary>
    private const double HorizontalPadding = 24;

    /// <summary>PNG 文件类型（保存对话框里的「文件类型」下拉）。</summary>
    private static readonly FilePickerFileType PngFileType = new("PNG")
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"],
        AppleUniformTypeIdentifiers = ["public.png"]
    };

    /// <summary>XLSX 文件类型（保存对话框里的「文件类型」下拉）。</summary>
    private static readonly FilePickerFileType XlsxFileType = new("Excel")
    {
        Patterns = ["*.xlsx"],
        MimeTypes = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
        AppleUniformTypeIdentifiers = ["org.openxmlformats.spreadsheetml.sheet"]
    };

    /// <summary>底色候选资源：Fluent 的「实体底色」是纯色、不透明，在浅色/深色字典里都有。</summary>
    private static readonly string[] BackgroundResourceKeys =
    [
        "SolidBackgroundFillColorBaseBrush",
        "SolidBackgroundFillColorQuarternaryBrush",
        "SystemRegionBrush"
    ];

    #region 图片

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
                Title = CR.Export_FormatImage,
                SuggestedFileName = ExportFileNameBuilder.EnsureExtension(suggestedFileName, "png"),
                DefaultExtension = "png",
                ShowOverwritePrompt = true,
                FileTypeChoices = [PngFileType]
            }).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "打开导出图片的保存对话框失败");
            throw new InvalidOperationException(CR.Export_Failed, exception);
        }

        // 用户按了取消：不是失败，也不用写日志。
        if (file is null)
            return false;

        cancellationToken.ThrowIfCancellationRequested();

        using var bitmap = await RenderContentBitmapAsync(content, topLevel, cancellationToken).ConfigureAwait(true);
        return await SavePngAsync(bitmap, file, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    ///     把已经渲染好的位图存成 PNG（导出对话框里「复制到剪贴板」那条路、
    ///     以及调用方自己渲染好位图再落盘的场合共用）。返回是否保存成功。
    /// </summary>
    public async Task<bool> SaveImageAsync(
        Bitmap bitmap,
        TopLevel topLevel,
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(topLevel);

        IStorageFile? file;
        try
        {
            file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = CR.Export_FormatImage,
                SuggestedFileName = ExportFileNameBuilder.EnsureExtension(suggestedFileName, "png"),
                DefaultExtension = "png",
                ShowOverwritePrompt = true,
                FileTypeChoices = [PngFileType]
            }).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "打开导出图片的保存对话框失败");
            throw new InvalidOperationException(CR.Export_Failed, exception);
        }

        if (file is null)
            return false;

        return await SavePngAsync(bitmap, file, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    ///     把图片放进剪贴板（不落盘，直接能粘到聊天窗口里）。
    /// </summary>
    /// <remarks>
    ///     Windows 上优先走自己写的 CF_DIB 写入（<see cref="WindowsClipboardImage" />）：
    ///     Avalonia 的剪贴板用的是 OLE 延迟渲染，位图在方法返回后就被释放，粘贴时是空的。
    /// </remarks>
    public async Task CopyImageToClipboardAsync(Bitmap bitmap, TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(topLevel);

        if (OperatingSystem.IsWindows()
            && WindowsClipboardImage.TrySet(bitmap, topLevel.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero))
        {
            logger.LogInformation("已把作业板图片复制到剪贴板（CF_DIB）");
            return;
        }

        if (topLevel.Clipboard is not { } clipboard)
            throw new InvalidOperationException(CR.Export_Failed);

        await clipboard.SetBitmapAsync(bitmap).ConfigureAwait(true);
        logger.LogInformation("已把作业板图片复制到剪贴板（Avalonia 剪贴板）");
    }

    /// <summary>
    ///     把板子的内容容器渲染成一张位图（带左右留白与不透明底）。
    ///     内容还没排版时抛 <see cref="InvalidOperationException" />。
    /// </summary>
    /// <remarks>
    ///     ⚠️ 必须 async：里面要等一轮 Render 优先级的排版。以前写成同步方法用
    ///     <c>InvokeAsync(...).GetAwaiter().GetResult()</c> 阻塞 UI 线程等自己队列里的任务，
    ///     在按钮点击（UI 线程）里调用会直接死锁——用户点「复制图片」后整个软件卡死就是这么来的。
    /// </remarks>
    public async Task<Bitmap> RenderContentBitmapAsync(
        Control content,
        TopLevel topLevel,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 等这一轮排版跑完再量：窗口刚缩放完就点导出时，控件可能还挂着上一次的排布，
        // 量出来会和真正画出来的样子对不上（导出的图里区块被裁在画布外就是这么来的）。
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Render);
        cancellationToken.ThrowIfCancellationRequested();

        var contentSize = MeasureContent(content);
        if (contentSize.Width < 1 || contentSize.Height < 1)
            throw new InvalidOperationException(CR.Export_NoContent);

        // 整张图的尺寸 = 内容尺寸 + 左右边距；内容本身仍按自己的宽度排版、渲染，再贴到中间。
        var size = new Size(contentSize.Width + HorizontalPadding * 2, contentSize.Height);

        // 尺寸换算成像素：控件是 DIP，高清屏要乘 RenderScaling，否则导出的图在 2x 屏上偏小发糊。
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
        // 直接给它 96 × RenderScaling 的 DPI，控件就被放大两遍，右半边区块被画到画布外。
        // 放大到像素尺寸放在下一步合成时做。
        var dipBitmapSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(contentSize.Width)),
            Math.Max(1, (int)Math.Ceiling(contentSize.Height)));
        using var contentBitmap = new RenderTargetBitmap(dipBitmapSize, new Vector(96, 96));
        contentBitmap.Render(content);

        // 先铺一层不透明的主题底色，再把内容叠上去（顺便按 DPI 放大）——控件自身的底色多半是透明的，
        // 直接输出会是透明底 PNG，粘到白色文档上字就看不见了。
        var outputBitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scale, 96 * scale));
        using (var context = outputBitmap.CreateDrawingContext())
        {
            context.FillRectangle(ResolveBackgroundBrush(content), new Rect(size));
            context.DrawImage(contentBitmap,
                new Rect(HorizontalPadding, 0, contentSize.Width, contentSize.Height));
        }

        return outputBitmap;
    }

    /// <summary>把位图按 PNG 写进用户已经选好的文件（先截断，避免留着旧内容）。</summary>
    private async Task<bool> SavePngAsync(Bitmap bitmap, IStorageFile file, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            if (stream.CanSeek)
            {
                stream.SetLength(0);
                stream.Position = 0;
            }

            // Avalonia 12 的 Bitmap.Save(Stream) 已过时，显式给 PNG 编码器选项。
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
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
            throw new InvalidOperationException(CR.Export_Failed, exception);
        }
    }

    #endregion

    #region 表格

    /// <summary>
    ///     把作业导成一张 xlsx 表格：先弹保存对话框，再用 <see cref="HomeworkTableBuilder" /> 排版、
    ///     <see cref="XlsxTableWriter" /> 写文件。返回是否真的保存了文件（用户取消返回 <c>false</c>）。
    /// </summary>
    /// <param name="notes">要导出的作业（含已清理的也行，要不要写由设置决定）。</param>
    /// <param name="boardName">表格第 1 行的板名。</param>
    /// <param name="topLevel">弹保存对话框用。</param>
    /// <param name="suggestedFileName">默认文件名；为空时用「时间戳.xlsx」。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<bool> ExportTableAsync(
        IReadOnlyList<Note> notes,
        string boardName,
        TopLevel topLevel,
        string? suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(topLevel);
        cancellationToken.ThrowIfCancellationRequested();

        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? ExportFileNameBuilder.Build(DateTime.Now, "xlsx")
            : suggestedFileName;

        IStorageFile? file;
        try
        {
            file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = CR.Export_FormatTable,
                SuggestedFileName = ExportFileNameBuilder.EnsureExtension(fileName, "xlsx"),
                DefaultExtension = "xlsx",
                ShowOverwritePrompt = true,
                FileTypeChoices = [XlsxFileType]
            }).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "打开导出表格的保存对话框失败");
            throw new InvalidOperationException(CR.Export_Failed, exception);
        }

        // 用户按了取消：不是失败，也不用写日志。
        if (file is null)
            return false;

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // 设置现读：用户刚在设置里改完样式，这一次导出就该按新样式来。
            var settings = configHandler.Data.ExportSettings;
            var table = new HomeworkTableBuilder().Build(
                notes,
                boardName,
                DateTime.Now,
                settings.TableGroupBySubject,
                settings.TableIncludeCleaned,
                settings.TableSortMode);

            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            if (stream.CanSeek)
            {
                stream.SetLength(0);
                stream.Position = 0;
            }

            new XlsxTableWriter().Write(table, stream);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(true);

            logger.LogInformation("作业板表格已导出：{FileName}（{RowCount} 行）", file.Name, table.Rows.Count);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "导出作业板表格失败");
            throw new InvalidOperationException(CR.Export_Failed, exception);
        }
    }

    #endregion

    #region 尺寸与底色

    /// <summary>
    ///     算「整张图」应有的尺寸（DIP）：宽度跟当前视口一样，高度用内容自己的自然高度。
    ///     <para>
    ///         滚动区里的内容会被排到「至少视口那么高」，所以 <see cref="Layoutable.Bounds" /> 的高度
    ///         在内容比窗口矮时是视口高——直接用会把下面一大片空白也导出去。这里用
    ///         <see cref="Layoutable.DesiredSize" />（内容自然高度，含外边距）减掉外边距，再拿
    ///         <c>Bounds</c> 兜底；内容比窗口高时 <c>Bounds</c> 本来就是完整高度，两者取小的那个即可。
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

    #endregion
}
