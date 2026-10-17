using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Corkboard.Core.Export;
using Corkboard.Core.Models.SubConfigs.Export;
using Corkboard.ViewModels.MainPages.Board;
using SkiaSharp;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Services.Ui.Export;

/// <summary>
///     一页排好版的 PDF 页面：控件树已经按「1 DIP = 1 单位」摆好，可以反复渲染成位图。
/// </summary>
/// <param name="Root">页面根控件（浅色主题作用域 + 白底画布）。渲染前已 Measure / Arrange 过。</param>
/// <param name="WidthDip">整页宽（DIP）。</param>
/// <param name="HeightDip">整页高（DIP）。</param>
/// <param name="OutlineTitle">这一页要不要在 PDF 书签里挂一项（科目名）；<c>null</c> 表示不挂。</param>
public sealed record PdfPageVisual(Control Root, double WidthDip, double HeightDip, string? OutlineTitle);

/// <summary>
///     PDF 渲染会话：把作业卡片按设置排到 A4 页面上，渲染成图片。
///     <para>
///         卡片直接复用主页面上的 <c>BoardNoteTemplate</c>（富文本、手写、截止日期、标签全套都在），
///         所以导出的样子和板子上看到的一致；页面是离屏挂到作业板页面树上的临时 <see cref="Canvas" />，
///         这样模板里的主题资源、页面样式才能正常解析（完全脱离可视树的控件拿不到 DynamicResource）。
///     </para>
///     <para>
///         生命周期：<see cref="Build" /> 之后先 <see cref="RenderPreview" /> 给预览窗口看，
///         用户确认后再 <see cref="EncodePages" /> 出 PDF 用的 JPEG；最后必须 <see cref="Dispose" />
///         把临时控件树摘掉。
///     </para>
///     <para>必须在 UI 线程上使用（要量控件、要渲染）。</para>
/// </summary>
public sealed class BoardPdfRenderSession : IDisposable
{
    /// <summary>PDF 正文墨色：写死而不是跟主题走（PDF 一定是白纸）。</summary>
    private static readonly IBrush PdfInkBrush = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));

    /// <summary>PDF 里的次要文字（二级分类标题）。</summary>
    private static readonly IBrush PdfMutedBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    /// <summary>科目标题的浅灰底条。</summary>
    private static readonly IBrush PdfSubjectBandBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5));

    /// <summary>PDF 正文默认文字色：卡片内容没带颜色、或设置里配的浅色（深色主题常见）时换成这个。</summary>
    private static readonly Color PdfContentColor = Color.FromRgb(0x11, 0x11, 0x11);

    private readonly Canvas _host;
    private readonly Panel _attachTo;
    private readonly List<PdfPageVisual> _pages;
    private bool _disposed;

    private BoardPdfRenderSession(Canvas host, Panel attachTo, List<PdfPageVisual> pages)
    {
        _host = host;
        _attachTo = attachTo;
        _pages = pages;
    }

    /// <summary>排好版的页（按阅读顺序）。</summary>
    public IReadOnlyList<PdfPageVisual> Pages => _pages;

    /// <summary>页数。</summary>
    public int PageCount => _pages.Count;

    /// <summary>
    ///     把每一页渲染成 1 倍大小的预览位图（给预览窗口显示）。
    ///     调用方负责 Dispose 返回的位图。
    /// </summary>
    public IReadOnlyList<Bitmap> RenderPreview()
    {
        var previews = new List<Bitmap>(_pages.Count);
        foreach (var page in _pages)
            previews.Add(RenderPage(page, 1));
        return previews;
    }

    /// <summary>
    ///     把指定页渲染成一张位图（「只导出一部分」的图片导出走这条路：整页就是那张长图）。
    /// </summary>
    /// <param name="pageIndex">页下标，0 开始。</param>
    /// <param name="scale">渲染倍率；1 就是 DIP 尺寸，高清屏可以给 2。</param>
    public Bitmap RenderPageBitmap(int pageIndex, double scale = 1)
    {
        if (pageIndex < 0 || pageIndex >= _pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        return RenderPage(_pages[pageIndex], scale);
    }

    /// <summary>
    ///     把每一页渲染成 2 倍大小并编码成 JPEG，交给 <see cref="PdfDocumentWriter" /> 组装 PDF。
    /// </summary>
    public IReadOnlyList<PdfPageImage> EncodePages()
    {
        var images = new List<PdfPageImage>(_pages.Count);
        foreach (var page in _pages)
        {
            using var bitmap = RenderPage(page, 2);
            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);

            var jpeg = ToJpeg(stream.ToArray());
            // PDF 的坐标单位是 1/72 英寸，DIP 是 1/96 英寸，所以乘 72/96。
            images.Add(new PdfPageImage(jpeg, page.WidthDip * 0.75, page.HeightDip * 0.75, page.OutlineTitle));
        }

        return images;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _attachTo.Children.Remove(_host);
        _host.Children.Clear();
        _pages.Clear();
    }

    /// <summary>
    ///     按设置把「科目分组 → 作业卡片」排成 PDF 页。
    /// </summary>
    /// <param name="attachTo">临时控件树挂到哪个面板（作业板页面的根 Grid）。</param>
    /// <param name="cardTemplate">主页面上的 <c>BoardNoteTemplate</c>。</param>
    /// <param name="groups">作业板当前的科目分组（与页面显示同一份顺序）。</param>
    /// <param name="noteIds">只导这些作业；<c>null</c> 表示全部。</param>
    /// <param name="settings">导出设置（纸张 / 方向 / 边距 / 列数 / 排序 / 科目分页 / 页眉页脚）。</param>
    /// <param name="boardName">页眉页脚上显示的作业板名称。</param>
    /// <param name="boardIsWide">板子是不是横向的（方向设为自动时用）。</param>
    /// <param name="now">页眉页脚里的导出时间。</param>
    public static BoardPdfRenderSession Build(
        Panel attachTo,
        IDataTemplate cardTemplate,
        IReadOnlyList<BoardSectionItem> groups,
        IReadOnlyList<Guid>? noteIds,
        ExportSettingsConfig settings,
        string boardName,
        bool boardIsWide,
        DateTime now)
    {
        var pageSize = PdfOptions.GetPageSize(settings.PdfPageSize, settings.PdfOrientation, boardIsWide);
        var margin = PdfOptions.GetMargin(settings.PdfMargin);
        var requestedColumns = PdfOptions.GetColumnCount(settings.PdfColumns);

        PdfLayoutOptions OptionsOf(int columns) => new()
        {
            PageSize = pageSize,
            Margin = margin,
            Columns = columns,
            ColumnGap = 16,
            BlockGap = 10,
            SubjectStartsNewPage = settings.PdfSubjectPageBreak
        };

        // 临时宿主 + 量板：**必须先把控件挂到页面树上再量**。卡片是 ContentControl + DataTemplate，
        // 不挂树就拿不到主题与模板，DesiredSize 高度会是 0，排版出来的块全叠在页顶（踩过）。
        var host = new Canvas
        {
            Width = 0,
            Height = 0,
            Opacity = 0,
            IsHitTestVisible = false,
            ClipToBounds = false
        };
        attachTo.Children.Add(host);

        var measureCanvas = new Canvas();
        host.Children.Add(measureCanvas);

        // 卡片高度跟列宽走（手写卡按比例缩放、文字卡按折行变高），所以列数直接决定一页塞得下多少。
        // 先按用户设的列数排；如果多一列能让页数变少（手写卡多的板子很常见），就自动用紧凑的那一档，
        // 免得一列只放得下一张卡、列底空掉半屏。
        BuildCandidate? best = null;
        var maxCandidate = Math.Min(4, requestedColumns + 1);
        for (var candidateColumns = requestedColumns; candidateColumns <= maxCandidate; candidateColumns++)
        {
            measureCanvas.Children.Clear();
            var candidateWidth = PdfLayout.GetColumnWidth(OptionsOf(candidateColumns));
            measureCanvas.Width = candidateWidth;

            var built = BuildBlockList(
                cardTemplate, groups, noteIds, settings.PdfSortMode, candidateWidth, measureCanvas);
            attachTo.UpdateLayout();
            for (var index = 0; index < built.Blocks.Count; index++)
            {
                var height = built.Payloads[index] is Control control && double.IsFinite(control.DesiredSize.Height)
                    ? control.DesiredSize.Height
                    : 0;
                built.Blocks[index] = new PdfBlock(built.Blocks[index].Kind, height > 0 ? height : 1);
            }

            var candidateLayout = PdfLayout.Build(built.Blocks, OptionsOf(candidateColumns));
            var candidateContentHeight = built.Blocks.Sum(block => (double)block.Height);
            var candidate = new BuildCandidate(candidateColumns, built.Blocks, built.Payloads,
                built.SubjectOfBlock, built.SubjectFontSizes, candidateLayout, candidateContentHeight);

            if (best is null
                || candidateLayout.Count < best.Layout.Count
                || (candidateLayout.Count == best.Layout.Count && candidateContentHeight < best.ContentHeight))
                best = candidate;
        }

        var chosen = best ?? throw new InvalidOperationException("PDF 排版没有产生任何候选。");
        var options = OptionsOf(chosen.Columns);
        var columnWidth = PdfLayout.GetColumnWidth(options);
        var blocks = chosen.Blocks;
        var payloads = chosen.Payloads;
        var subjectOfBlock = chosen.SubjectOfBlock;
        var subjectFontSizes = chosen.SubjectFontSizes;
        var layout = chosen.Layout;

        // 选中的那一档控件可能已经被后一个候选的 Children.Clear() 摘下树，这里重新挂回量板，
        // 后面补续标题、搬进页面画布都要靠它。
        measureCanvas.Children.Clear();
        measureCanvas.Width = columnWidth;
        foreach (var payload in payloads)
            if (payload is Control control)
                measureCanvas.Children.Add(control);
        attachTo.UpdateLayout();

        // 续标题：一个科目的内容跨到新的一列 / 新的一页时，在列首补一条科目标题；
        // 否则那一列开头就是一叠「看不出属于哪个科目」的卡片。补完重新排版，直到不再新增。
        for (var pass = 0; pass < 4; pass++)
        {
            var insertions = FindContinuationHeaders(layout, blocks, subjectOfBlock);
            if (insertions.Count == 0)
                break;

            var newBlocks = new List<PdfBlock>(blocks.Count + insertions.Count);
            var newPayloads = new List<object>(payloads.Count + insertions.Count);
            var newSubjects = new List<string?>(subjectOfBlock.Count + insertions.Count);
            var insertionIndex = 0;
            for (var index = 0; index <= blocks.Count; index++)
            {
                while (insertionIndex < insertions.Count && insertions[insertionIndex].BlockIndex == index)
                {
                    var insertion = insertions[insertionIndex++];
                    var fontSize = subjectFontSizes.TryGetValue(insertion.Subject, out var sizeOfSubject) ? sizeOfSubject : 16;
                    var continuation = CreateSubjectHeader(insertion.Subject, fontSize);
                    continuation.Width = columnWidth;
                    measureCanvas.Children.Add(continuation);
                    attachTo.UpdateLayout();
                    var continuationHeight = continuation.DesiredSize.Height;
                    newBlocks.Add(new PdfBlock(PdfBlockKind.SubjectHeader,
                        double.IsFinite(continuationHeight) && continuationHeight > 0 ? continuationHeight : 1));
                    newPayloads.Add(continuation);
                    newSubjects.Add(insertion.Subject);
                }

                if (index == blocks.Count)
                    break;

                newBlocks.Add(blocks[index]);
                newPayloads.Add(payloads[index]);
                newSubjects.Add(subjectOfBlock[index]);
            }

            blocks = newBlocks;
            payloads = newPayloads;
            subjectOfBlock = newSubjects;
            layout = PdfLayout.Build(blocks, options);
        }

        // 书签：每个科目挂到它最早出现的那一页（一页最多一项，PDF outline 是挂在页面上的）。
        var bookmarked = new HashSet<string>(StringComparer.Ordinal);
        var pageOutlines = new string?[layout.Count];
        for (var pageIndex = 0; pageIndex < layout.Count; pageIndex++)
            foreach (var placement in layout[pageIndex].Placements)
            {
                if (blocks[placement.BlockIndex].Kind != PdfBlockKind.SubjectHeader
                    || subjectOfBlock[placement.BlockIndex] is not { } subject
                    || !bookmarked.Add(subject))
                    continue;

                pageOutlines[pageIndex] = subject;
                break;
            }

        var pages = new List<PdfPageVisual>(layout.Count);
        for (var pageIndex = 0; pageIndex < layout.Count; pageIndex++)
        {
            var pageLayout = layout[pageIndex];
            var placements = pageLayout.Placements;

            // 页面根：浅色主题作用域 + 白底画布。PDF 要打印，不能跟深色主题走。
            var canvas = new Canvas { Background = Brushes.White };
            var scope = new ThemeVariantScope
            {
                RequestedThemeVariant = ThemeVariant.Light,
                Width = pageLayout.Width,
                Height = pageLayout.Height,
                Child = canvas
            };

            foreach (var placement in placements)
            {
                if (payloads[placement.BlockIndex] is not Control control)
                    continue;

                measureCanvas.Children.Remove(control);
                Canvas.SetLeft(control, placement.X);
                Canvas.SetTop(control, placement.Y);
                control.Width = placement.Width;
                control.Height = placement.Height;
                canvas.Children.Add(control);
            }

            if (settings.PdfHeaderFooter)
                AddHeaderFooter(canvas, pageLayout, boardName, now, pageIndex + 1, layout.Count);

            host.Children.Add(scope);
            pages.Add(new PdfPageVisual(scope, pageLayout.Width, pageLayout.Height, pageOutlines[pageIndex]));
        }

        host.Children.Remove(measureCanvas);

        // 页子树的样式 / 模板在挂树后才会真正套上，再跑一次布局并重新 Arrange，
        // 这样渲染时每个控件都有确定的尺寸（预览和 PDF 都走这条路）。
        attachTo.UpdateLayout();
        foreach (var page in pages)
        {
            page.Root.Measure(new Size(page.WidthDip, page.HeightDip));
            page.Root.Arrange(new Rect(0, 0, page.WidthDip, page.HeightDip));
        }

        return new BoardPdfRenderSession(host, attachTo, pages);
    }

    /// <summary>
    ///     把选中的作业排成一张竖长图（「只导出一部分」时导出图片走这条路）。
    ///     宽度固定 760 DIP、单列，四周留 24 DIP；只生成一页，调用方把它当普通图片保存。
    /// </summary>
    /// <param name="attachTo">临时控件树挂到哪个面板。</param>
    /// <param name="cardTemplate">主页面上的 <c>BoardNoteTemplate</c>。</param>
    /// <param name="groups">作业板当前的科目分组（与页面显示同一份顺序）。</param>
    /// <param name="noteIds">只导出这些作业；<c>null</c> 表示全部。</param>
    /// <param name="sortMode">组内排序方式。</param>
    public static BoardPdfRenderSession? BuildComposedImage(
        Panel attachTo,
        IDataTemplate cardTemplate,
        IReadOnlyList<BoardSectionItem> groups,
        IReadOnlyList<Guid>? noteIds,
        PdfSortMode sortMode = PdfSortMode.FollowBoard)
    {
        const double width = 760;
        const double margin = 24;
        const double blockGap = 10;
        var columnWidth = width - margin * 2;

        var host = new Canvas
        {
            Width = 0,
            Height = 0,
            Opacity = 0,
            IsHitTestVisible = false,
            ClipToBounds = false
        };
        attachTo.Children.Add(host);

        var measureCanvas = new Canvas { Width = columnWidth };
        host.Children.Add(measureCanvas);

        var (blocks, payloads, _, _) = BuildBlockList(cardTemplate, groups, noteIds, sortMode, columnWidth, measureCanvas);
        if (blocks.Count == 0)
        {
            attachTo.Children.Remove(host);
            return null;
        }

        attachTo.UpdateLayout();
        for (var index = 0; index < blocks.Count; index++)
        {
            var blockHeight = payloads[index] is Control control && double.IsFinite(control.DesiredSize.Height)
                ? control.DesiredSize.Height
                : 0;
            blocks[index] = new PdfBlock(blocks[index].Kind, blockHeight > 0 ? blockHeight : 1);
        }

        // 一张图：高度 = 上下边距 + 所有块高 + 块间距。
        var height = margin * 2 + blocks.Sum(block => Math.Max(0, block.Height))
                     + Math.Max(0, blocks.Count - 1) * blockGap;

        var canvas = new Canvas { Background = Brushes.White };
        var scope = new ThemeVariantScope
        {
            RequestedThemeVariant = ThemeVariant.Light,
            Width = width,
            Height = height,
            Child = canvas
        };

        var y = margin;
        for (var index = 0; index < blocks.Count; index++)
        {
            if (payloads[index] is not Control control)
                continue;

            if (index > 0)
                y += blockGap;

            measureCanvas.Children.Remove(control);
            Canvas.SetLeft(control, margin);
            Canvas.SetTop(control, y);
            control.Width = columnWidth;
            control.Height = Math.Max(0, blocks[index].Height);
            canvas.Children.Add(control);

            y += Math.Max(0, blocks[index].Height);
        }

        host.Children.Remove(measureCanvas);
        host.Children.Add(scope);

        attachTo.UpdateLayout();
        scope.Measure(new Size(width, height));
        scope.Arrange(new Rect(0, 0, width, height));

        var pages = new List<PdfPageVisual> { new(scope, width, height, null) };
        return new BoardPdfRenderSession(host, attachTo, pages);
    }

    /// <summary>
    ///     按当前排序设置把「科目 → 小节 → 作业」摊平成排版块；返回块、对应控件与
    ///     「块下标 → 科目名」的科目归属表与科目字号表（续标题要用）。控件会先挂到
    ///     <paramref name="measureHost" /> 上等着量高度。
    /// </summary>
    private static (List<PdfBlock> Blocks, List<object> Payloads, List<string?> SubjectOfBlock,
        Dictionary<string, double> SubjectFontSizes) BuildBlockList(
            IDataTemplate cardTemplate,
            IReadOnlyList<BoardSectionItem> groups,
            IReadOnlyList<Guid>? noteIds,
            PdfSortMode sortMode,
            double columnWidth,
            Panel measureHost)
    {
        var allow = noteIds is null ? null : new HashSet<Guid>(noteIds);
        var subjectGroups = OrderGroups(groups, sortMode, allow, out var flattenSections);

        var blocks = new List<PdfBlock>();
        var payloads = new List<object>();
        var subjectOfBlock = new List<string?>();
        var subjectFontSizes = new Dictionary<string, double>(StringComparer.Ordinal);

        void Add(Control control, PdfBlockKind kind, string subject)
        {
            AddBlock(blocks, payloads, subjectOfBlock, measureHost, columnWidth, control, kind, subject);
        }

        foreach (var group in subjectGroups)
        {
            if (group.Notes.Count == 0)
                continue;

            subjectFontSizes[group.Subject] = group.SubjectFontSize;

            var header = CreateSubjectHeader(group.Subject, group.SubjectFontSize);
            Add(header, PdfBlockKind.SubjectHeader, group.Subject);

            if (!flattenSections)
            {
                foreach (var subsection in group.Subsections)
                {
                    if (subsection.Notes.Count == 0)
                        continue;

                    if (subsection.ShowTitle)
                    {
                        var title = CreateSubsectionTitle(subsection.Title, subsection.TitleFontSize);
                        Add(title, PdfBlockKind.Note, group.Subject);
                    }

                    foreach (var note in subsection.Notes)
                    {
                        var card = CreateCard(cardTemplate, note, columnWidth);
                        Add(card, PdfBlockKind.Note, group.Subject);
                    }
                }

                continue;
            }

            foreach (var note in group.Notes)
            {
                var card = CreateCard(cardTemplate, note, columnWidth);
                Add(card, PdfBlockKind.Note, group.Subject);
            }
        }

        return (blocks, payloads, subjectOfBlock, subjectFontSizes);
    }

    /// <summary>把块控件挂到量板上；高度先记 0，等布局跑完再回填；同时记下它属于哪个科目。</summary>
    private static void AddBlock(
        List<PdfBlock> blocks,
        List<object> payloads,
        List<string?> subjectOfBlock,
        Panel measureHost,
        double columnWidth,
        Control control,
        PdfBlockKind kind,
        string subject)
    {
        if (double.IsNaN(control.Width))
            control.Width = columnWidth;

        measureHost.Children.Add(control);
        blocks.Add(new PdfBlock(kind, 0));
        payloads.Add(control);
        subjectOfBlock.Add(subject);
    }

    /// <summary>
    ///     找出「一列 / 一页的开头是作业卡片，但那个科目的标题没在它上面」的位置，
    ///     给它们各补一条科目标题（续标题）；返回的 <see cref="ContinuationHeader.BlockIndex" />
    ///     是插在这一块之前的位置。
    /// </summary>
    private static List<ContinuationHeader> FindContinuationHeaders(
        IReadOnlyList<PdfPageLayout> layout,
        IReadOnlyList<PdfBlock> blocks,
        IReadOnlyList<string?> subjectOfBlock)
    {
        var insertions = new List<ContinuationHeader>();
        string? previousColumnSubject = null;

        foreach (var page in layout)
        {
            var column = new List<PdfPlacement>();
            var columnStartX = double.NaN;
            var firstColumnOfPage = true;

            void FlushColumn()
            {
                if (column.Count == 0)
                    return;

                var first = column[0];
                var subject = subjectOfBlock[first.BlockIndex];

                // 列首本来就有科目标题就不用补；否则「换页」或「换科目」都补一条，
                // 保证每一列 / 每一页的第一批卡片上面都有科目。
                if (blocks[first.BlockIndex].Kind != PdfBlockKind.SubjectHeader
                    && subject is not null
                    && (firstColumnOfPage || !string.Equals(subject, previousColumnSubject, StringComparison.Ordinal)))
                {
                    insertions.Add(new ContinuationHeader(first.BlockIndex, subject));
                }

                previousColumnSubject = subjectOfBlock[column[^1].BlockIndex];
                firstColumnOfPage = false;
                column = [];
            }

            foreach (var placement in page.Placements)
            {
                if (column.Count == 0 || Math.Abs(placement.X - columnStartX) > 0.5)
                {
                    FlushColumn();
                    columnStartX = placement.X;
                }

                column.Add(placement);
            }

            FlushColumn();
        }

        return insertions;
    }

    /// <summary>一条要补的续标题：插在 <paramref name="BlockIndex" /> 之前。</summary>
    private readonly record struct ContinuationHeader(int BlockIndex, string Subject);

    /// <summary>某一档列数的候选排版结果：按页数 / 内容总高挑最紧凑的那档。</summary>
    private sealed record BuildCandidate(
        int Columns,
        List<PdfBlock> Blocks,
        List<object> Payloads,
        List<string?> SubjectOfBlock,
        Dictionary<string, double> SubjectFontSizes,
        IReadOnlyList<PdfPageLayout> Layout,
        double ContentHeight);

    /// <summary>按排序设置重排科目分组；日期排序时把组内作业拍平（不再画二级分类小标题）。</summary>
    private static List<RenderedGroup> OrderGroups(
        IReadOnlyList<BoardSectionItem> groups,
        PdfSortMode sortMode,
        HashSet<Guid>? allow,
        out bool flattenSections)
    {
        flattenSections = sortMode is PdfSortMode.CreatedDescending or PdfSortMode.CreatedAscending
            or PdfSortMode.UpdatedDescending or PdfSortMode.Fixed;

        var ordered = groups.ToList();
        if (sortMode == PdfSortMode.Subject)
            ordered = ordered.OrderBy(group => group.Subject, StringComparer.CurrentCulture).ToList();

        var result = new List<RenderedGroup>(ordered.Count);
        foreach (var group in ordered)
        {
            var subsections = group.Sections
                .Select(section => new RenderedSubsection(
                    section.Title,
                    section.ShowTitle,
                    section.TitleFontSize,
                    FilterAndSort(section.Notes, allow, sortMode)))
                .Where(subsection => subsection.Notes.Count > 0)
                .ToList();

            var notes = subsections.SelectMany(subsection => subsection.Notes).ToList();
            result.Add(new RenderedGroup(group.Subject, group.SubjectFontSize, subsections, notes));
        }

        return result;
    }

    private static List<BoardNoteCardItem> FilterAndSort(
        IReadOnlyList<BoardNoteCardItem> notes,
        HashSet<Guid>? allow,
        PdfSortMode sortMode)
    {
        IEnumerable<BoardNoteCardItem> query = notes;
        if (allow is not null)
            query = query.Where(note => allow.Contains(note.Note.Id));

        query = sortMode switch
        {
            PdfSortMode.CreatedDescending => query.OrderByDescending(note => note.Note.CreatedAt),
            PdfSortMode.CreatedAscending => query.OrderBy(note => note.Note.CreatedAt),
            PdfSortMode.UpdatedDescending => query.OrderByDescending(note => note.Note.UpdatedAt),
            PdfSortMode.Fixed => query.OrderBy(note => note.Note.Order).ThenBy(note => note.Note.CreatedAt),
            _ => query
        };

        return query.ToList();
    }

    /// <summary>
    ///     科目标题：浅灰底条 + 深色字。颜色写死而不是跟主题走——PDF 一定是白纸，
    ///     深色主题下「主题文字色」是白的，跟着主题会白字白底（用户报过「PDF 里看不到科目」）。
    /// </summary>
    private static Control CreateSubjectHeader(string subject, double fontSize)
    {
        var text = new TextBlock
        {
            Text = subject,
            FontSize = Math.Max(14, fontSize),
            FontWeight = FontWeight.Bold,
            Foreground = PdfInkBrush,
            TextWrapping = TextWrapping.Wrap
        };

        return new Border
        {
            Background = PdfSubjectBandBrush,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 10, 0, 0),
            Child = text
        };
    }

    /// <summary>二级分类小标题：深灰字，同样不跟主题色走。</summary>
    private static TextBlock CreateSubsectionTitle(string title, double fontSize)
    {
        return new TextBlock
        {
            Text = title,
            FontSize = Math.Max(11, fontSize),
            FontWeight = FontWeight.SemiBold,
            Foreground = PdfMutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };
    }

    private static ContentControl CreateCard(IDataTemplate cardTemplate, BoardNoteCardItem note, double columnWidth)
    {
        // PDF 是白纸打印，但「内容默认色」是用户按深色主题配的（经常接近白色）。
        // RichTextBlock 会把这个默认色补成行内 span——白字落在白纸上等于内容整段消失，
        // 只剩截止日期/标签这些跟主题走的元素（用户报过「卡片只剩一行截止日期、内容没了」）。
        // 这里在导出时换成深色副本；卡片里本来就带行内颜色的片段不受影响。
        var sourceColor = note.ContentColor;
        var printableColor = sourceColor is { } color && !IsLightColor(color) ? sourceColor : PdfContentColor;
        var cardNote = printableColor == sourceColor
            ? note
            : new BoardNoteCardItem(
                note.Note,
                note.Html,
                note.ContentFontSize,
                printableColor,
                note.MetaFontSize,
                note.NoteTagFontSize,
                note.CanModify,
                note.DueText,
                note.IsOverdue,
                note.Handwriting,
                note.HandwritingAspect,
                note.IsHandwriting,
                note.Tags);

        var card = new ContentControl
        {
            Content = cardNote,
            ContentTemplate = cardTemplate,
            Width = columnWidth,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        // 卡片对象是页面列表项那份，导出时不能把「点开操作按钮」的状态带进去。
        cardNote.IsActionsVisible = false;
        cardNote.IsPointerOver = false;
        return card;
    }

    /// <summary>颜色是否太浅（白纸上当文字色会看不见）。</summary>
    private static bool IsLightColor(Color color)
    {
        return (color.R * 0.299 + color.G * 0.587 + color.B * 0.114) / 255.0 > 0.72;
    }

    /// <summary>量一个控件在给定列宽下要占多高；量不出来时给 0，交给排版把块挨着排。</summary>
    private static double MeasureHeight(Control control, double columnWidth)
    {
        control.Measure(new Size(columnWidth, double.PositiveInfinity));
        var height = control.DesiredSize.Height;
        return double.IsFinite(height) && height > 0 ? height : 0;
    }

    private static void AddHeaderFooter(
        Canvas canvas,
        PdfPageLayout pageLayout,
        string boardName,
        DateTime now,
        int pageNumber,
        int pageCount)
    {
        var headerFontSize = 10d;
        var header = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(boardName) ? CR.App_Title : boardName,
            FontSize = headerFontSize,
            Foreground = PdfMutedBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Canvas.SetLeft(header, PdfOptions.NormalMarginDip * 0.6);
        Canvas.SetTop(header, PdfOptions.NormalMarginDip * 0.3);
        header.Width = pageLayout.Width - PdfOptions.NormalMarginDip * 1.2;
        canvas.Children.Add(header);

        var footer = new TextBlock
        {
            Text = $"{now:yyyy-MM-dd HH:mm}    {pageNumber} / {pageCount}",
            FontSize = headerFontSize,
            Foreground = PdfMutedBrush
        };
        Canvas.SetLeft(footer, PdfOptions.NormalMarginDip * 0.6);
        Canvas.SetTop(footer, pageLayout.Height - PdfOptions.NormalMarginDip * 0.45);
        footer.Width = pageLayout.Width - PdfOptions.NormalMarginDip * 1.2;
        footer.TextAlignment = TextAlignment.Right;
        canvas.Children.Add(footer);
    }

    /// <summary>渲染一页。倍率 1 给预览，倍率 2 给 PDF（A4 竖版约 1587×2245 像素，打印够用）。</summary>
    private static Bitmap RenderPage(PdfPageVisual page, double scale)
    {
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(page.WidthDip * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(page.HeightDip * scale));

        // 位图 DPI 给 96 × scale、Render 会按这个 DPI 放大一遍，所以内容正好铺满整张位图
        // （跟导出图片那里踩过的「放大两遍」是反过来的：那里内容位图的 DPI 必须是 96）。
        using var target = new RenderTargetBitmap(
            new PixelSize(pixelWidth, pixelHeight),
            new Vector(96 * scale, 96 * scale));
        target.Render(page.Root);

        // 位图 -> PNG -> JPEG：PDF 只认 /DCTDecode（JPEG），Avalonia 只能存 PNG，用 Skia 转一道。
        using var pngStream = new MemoryStream();
        target.Save(pngStream, PngBitmapEncoderOptions.Default);
        return new Bitmap(new MemoryStream(ToJpeg(pngStream.ToArray())));
    }

    /// <summary>把 PNG 字节重编码成 JPEG（质量 90）。</summary>
    private static byte[] ToJpeg(byte[] pngBytes)
    {
        using var bitmap = SKBitmap.Decode(pngBytes)
                           ?? throw new InvalidOperationException("页面位图编码失败。");
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90)
                         ?? throw new InvalidOperationException("页面位图编码失败。");
        return data.ToArray();
    }

    /// <summary>渲染用的科目分组：科目名 / 字号 / 小节 / 全部作业（拍平排序时直接用）。</summary>
    private sealed record RenderedGroup(
        string Subject,
        double SubjectFontSize,
        IReadOnlyList<RenderedSubsection> Subsections,
        IReadOnlyList<BoardNoteCardItem> Notes);

    private sealed record RenderedSubsection(
        string Title,
        bool ShowTitle,
        double TitleFontSize,
        IReadOnlyList<BoardNoteCardItem> Notes);
}

