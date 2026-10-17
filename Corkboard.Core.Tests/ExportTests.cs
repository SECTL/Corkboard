using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Export;
using Corkboard.Core.Models;
using Corkboard.Core.Models.SubConfigs.Export;

namespace Corkboard.Core.Tests;

/// <summary>
///     导出层契约：默认文件名（时间戳 + 扩展名）、导出设置的落盘 / 归一、PDF 分页排版，
///     以及那个不依赖第三方库的极小 PDF 组装器（JPEG 整页 + 顶层书签 + 正确的 xref）。
///     <para>
///         这些用例全部在内存里跑：不碰本机时区 / 语言，也不写临时文件。
///     </para>
/// </summary>
public class ExportTests
{
    // ------------------------------------------------------------------
    // 默认文件名
    // ------------------------------------------------------------------

    [Fact]
    public void ExportFileName_Build_UsesFixedInvariantTimestamp()
    {
        var name = ExportFileNameBuilder.Build(new DateTime(2026, 10, 18, 14, 30, 5), "png");

        Assert.Equal("2026-10-18_14-30-05.png", name);
    }

    [Fact]
    public void ExportFileName_Build_NormalizesExtensionDotAndCase()
    {
        var now = new DateTime(2026, 10, 18, 14, 30, 5);

        // 带不带点、大小写、前后空白都应该归一成「一个小写扩展名」。
        Assert.Equal("2026-10-18_14-30-05.pdf", ExportFileNameBuilder.Build(now, ".pdf"));
        Assert.Equal("2026-10-18_14-30-05.png", ExportFileNameBuilder.Build(now, ".PNG"));
        Assert.Equal("2026-10-18_14-30-05.png", ExportFileNameBuilder.Build(now, "  .png  "));
    }

    [Fact]
    public void ExportFileName_Build_WithoutExtensionIsJustTimestamp()
    {
        var name = ExportFileNameBuilder.Build(new DateTime(2026, 10, 18, 14, 30, 5), string.Empty);

        Assert.Equal("2026-10-18_14-30-05", name);
        Assert.DoesNotContain('.', name);
    }

    [Fact]
    public void ExportFileName_EnsureExtension_AppendsWhenMissing()
    {
        Assert.Equal("作业板.png", ExportFileNameBuilder.EnsureExtension("作业板", "png"));
        Assert.Equal("作业板.pdf", ExportFileNameBuilder.EnsureExtension("作业板", ".pdf"));
    }

    [Fact]
    public void ExportFileName_EnsureExtension_KeepsMatchingExtension()
    {
        // 已经是目标扩展名（大小写不敏感）就原样返回，不要顺手把 .PNG 改成 .png。
        Assert.Equal("作业板.PNG", ExportFileNameBuilder.EnsureExtension("作业板.PNG", "png"));
        Assert.Equal("report.pdf", ExportFileNameBuilder.EnsureExtension("report.pdf", ".PDF"));
    }

    [Fact]
    public void ExportFileName_EnsureExtension_ReplacesDifferentExtension()
    {
        Assert.Equal("report.pdf", ExportFileNameBuilder.EnsureExtension("report.txt", "pdf"));
        Assert.Equal("作业板.png", ExportFileNameBuilder.EnsureExtension("作业板.jpeg", ".png"));
    }

    [Fact]
    public void ExportFileName_EnsureExtension_KeepsDirectoryAndDotsInName()
    {
        // 只去掉最后一段扩展名，目录里与文件名中间的点的不能碰。
        Assert.Equal("exports/v1.2/作业板.png", ExportFileNameBuilder.EnsureExtension("exports/v1.2/作业板", "png"));
        Assert.Equal("a.b.png", ExportFileNameBuilder.EnsureExtension("a.b.jpg", "png"));
        Assert.Equal("report.png", ExportFileNameBuilder.EnsureExtension("report.", "png"));
    }

    [Fact]
    public void ExportFileName_EnsureExtension_LeavesBlankInputsAlone()
    {
        // 没有文件名 / 没有扩展名可补时原样交回，交给保存对话框自己校验。
        Assert.Equal(string.Empty, ExportFileNameBuilder.EnsureExtension(string.Empty, "png"));
        Assert.Equal("   ", ExportFileNameBuilder.EnsureExtension("   ", "png"));
        Assert.Equal("report.pdf", ExportFileNameBuilder.EnsureExtension("report.pdf", string.Empty));
    }

    // ------------------------------------------------------------------
    // 导出设置
    // ------------------------------------------------------------------

    [Fact]
    public void ExportSettings_DefaultsMatchSpec()
    {
        var settings = new ExportSettingsConfig();

        Assert.True(settings.AskForFormat);
        Assert.Equal(ExportFormat.Image, settings.DefaultFormat);
        Assert.Equal(PdfPageSize.A4, settings.PdfPageSize);
        Assert.Equal(PdfOrientation.Auto, settings.PdfOrientation);
        Assert.Equal(2, settings.PdfColumns);
        Assert.Equal(PdfMargin.Normal, settings.PdfMargin);
        Assert.Equal(PdfSortMode.FollowBoard, settings.PdfSortMode);
        Assert.False(settings.PdfSubjectPageBreak);
        Assert.True(settings.PdfHeaderFooter);
    }

    [Fact]
    public void ExportSettings_RoundTripsThroughJsonAsSnakeCase()
    {
        var config = new MainConfigModel();
        var export = config.ExportSettings;
        export.AskForFormat = false;
        export.DefaultFormat = ExportFormat.Pdf;
        export.PdfPageSize = PdfPageSize.Letter;
        export.PdfOrientation = PdfOrientation.Landscape;
        export.PdfColumns = 4;
        export.PdfMargin = PdfMargin.Wide;
        export.PdfSortMode = PdfSortMode.Subject;
        export.PdfSubjectPageBreak = true;
        export.PdfHeaderFooter = false;

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);

        // 子配置必须挂在 export_settings 下（snake_case 由统一 JSON 选项生成），枚举按数字落盘。
        Assert.Contains("\"export_settings\"", json);
        Assert.Contains("\"ask_for_format\": false", json);
        Assert.Contains("\"default_format\": 1", json);
        Assert.Contains("\"pdf_page_size\": 2", json);
        Assert.Contains("\"pdf_orientation\": 2", json);
        Assert.Contains("\"pdf_columns\": 4", json);
        Assert.Contains("\"pdf_margin\": 2", json);
        Assert.Contains("\"pdf_sort_mode\": 4", json);
        Assert.Contains("\"pdf_subject_page_break\": true", json);
        Assert.Contains("\"pdf_header_footer\": false", json);

        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)!;
        var roundTripped = restored.ExportSettings;

        Assert.False(roundTripped.AskForFormat);
        Assert.Equal(ExportFormat.Pdf, roundTripped.DefaultFormat);
        Assert.Equal(PdfPageSize.Letter, roundTripped.PdfPageSize);
        Assert.Equal(PdfOrientation.Landscape, roundTripped.PdfOrientation);
        Assert.Equal(4, roundTripped.PdfColumns);
        Assert.Equal(PdfMargin.Wide, roundTripped.PdfMargin);
        Assert.Equal(PdfSortMode.Subject, roundTripped.PdfSortMode);
        Assert.True(roundTripped.PdfSubjectPageBreak);
        Assert.False(roundTripped.PdfHeaderFooter);
    }

    [Fact]
    public void ExportSettings_MissingSectionKeepsDefaults()
    {
        // 老版本 settings.json 里没有 export_settings：读回来必须是默认值，不能是 null 或 0。
        var restored = JsonSerializer.Deserialize<MainConfigModel>(
            """{ "board_settings": { "board_name": "作业" } }""", ConfigServiceBase.JsonOptions)!;

        Assert.Equal(2, restored.ExportSettings.PdfColumns);
        Assert.Equal(ExportFormat.Image, restored.ExportSettings.DefaultFormat);
        Assert.True(restored.ExportSettings.AskForFormat);
        Assert.True(restored.ExportSettings.PdfHeaderFooter);
    }

    [Fact]
    public void ExportSettings_Normalize_ClampsColumnsIntoRange()
    {
        var settings = new ExportSettingsConfig { PdfColumns = 0 };
        settings.Normalize();
        Assert.Equal(1, settings.PdfColumns);

        settings.PdfColumns = 99;
        settings.Normalize();
        Assert.Equal(4, settings.PdfColumns);

        settings.PdfColumns = -5;
        settings.Normalize();
        Assert.Equal(1, settings.PdfColumns);
    }

    [Fact]
    public void ExportSettings_Normalize_FallsBackFromUnknownEnums()
    {
        var settings = new ExportSettingsConfig
        {
            DefaultFormat = (ExportFormat)9,
            PdfPageSize = (PdfPageSize)99,
            PdfOrientation = (PdfOrientation)(-1),
            PdfMargin = (PdfMargin)7,
            PdfSortMode = (PdfSortMode)42
        };

        settings.Normalize();

        Assert.Equal(ExportFormat.Image, settings.DefaultFormat);
        Assert.Equal(PdfPageSize.A4, settings.PdfPageSize);
        Assert.Equal(PdfOrientation.Auto, settings.PdfOrientation);
        Assert.Equal(PdfMargin.Normal, settings.PdfMargin);
        Assert.Equal(PdfSortMode.FollowBoard, settings.PdfSortMode);
    }

    [Fact]
    public void MainConfig_NormalizeLoaded_NormalizesExportSettings()
    {
        var config = new MainConfigModel();
        config.ExportSettings.PdfColumns = 99;
        config.ExportSettings.PdfPageSize = (PdfPageSize)42;

        config.NormalizeLoaded();

        Assert.Equal(4, config.ExportSettings.PdfColumns);
        Assert.Equal(PdfPageSize.A4, config.ExportSettings.PdfPageSize);
    }

    [Fact]
    public void ExportSettings_DirtyJsonIsCleanedAfterNormalizeLoaded()
    {
        // 模拟读盘：手改坏的 settings.json 由 NormalizeLoaded 收干净，导出之前就已是合法值。
        var restored = JsonSerializer.Deserialize<MainConfigModel>(
            """{ "export_settings": { "pdf_columns": 99, "pdf_margin": 55, "default_format": 7 } }""",
            ConfigServiceBase.JsonOptions)!;

        restored.NormalizeLoaded();

        Assert.Equal(4, restored.ExportSettings.PdfColumns);
        Assert.Equal(PdfMargin.Normal, restored.ExportSettings.PdfMargin);
        Assert.Equal(ExportFormat.Image, restored.ExportSettings.DefaultFormat);
    }

    // ------------------------------------------------------------------
    // 纸张 / 边距 / 列数换算
    // ------------------------------------------------------------------

    [Fact]
    public void PdfOptions_GetPageSize_AutoFollowsBoardShape()
    {
        var portrait = PdfOptions.GetPageSize(PdfPageSize.A4, PdfOrientation.Auto, boardIsWide: false);
        Assert.Equal(PdfOptions.A4WidthDip, portrait.Width, 6);
        Assert.Equal(PdfOptions.A4HeightDip, portrait.Height, 6);

        var landscape = PdfOptions.GetPageSize(PdfPageSize.A4, PdfOrientation.Auto, boardIsWide: true);
        Assert.Equal(PdfOptions.A4HeightDip, landscape.Width, 6);
        Assert.Equal(PdfOptions.A4WidthDip, landscape.Height, 6);
    }

    [Fact]
    public void PdfOptions_GetPageSize_ExplicitOrientationWinsOverBoardShape()
    {
        var portrait = PdfOptions.GetPageSize(PdfPageSize.A4, PdfOrientation.Portrait, boardIsWide: true);
        Assert.Equal(PdfOptions.A4WidthDip, portrait.Width, 6);
        Assert.Equal(PdfOptions.A4HeightDip, portrait.Height, 6);

        var landscape = PdfOptions.GetPageSize(PdfPageSize.A4, PdfOrientation.Landscape, boardIsWide: false);
        Assert.Equal(PdfOptions.A4HeightDip, landscape.Width, 6);
        Assert.Equal(PdfOptions.A4WidthDip, landscape.Height, 6);
    }

    [Fact]
    public void PdfOptions_GetPageSize_SupportsAllPaperSizes()
    {
        var a5 = PdfOptions.GetPageSize(PdfPageSize.A5, PdfOrientation.Portrait, false);
        Assert.Equal(559.4, a5.Width, 3);
        Assert.Equal(793.7, a5.Height, 3);

        var letter = PdfOptions.GetPageSize(PdfPageSize.Letter, PdfOrientation.Portrait, false);
        Assert.Equal(816, letter.Width, 6);
        Assert.Equal(1056, letter.Height, 6);

        var letterLandscape = PdfOptions.GetPageSize(PdfPageSize.Letter, PdfOrientation.Landscape, false);
        Assert.Equal(1056, letterLandscape.Width, 6);
        Assert.Equal(816, letterLandscape.Height, 6);
    }

    [Fact]
    public void PdfOptions_GetMargin_ReturnsDipPerPreset()
    {
        Assert.Equal(24, PdfOptions.GetMargin(PdfMargin.Narrow));
        Assert.Equal(36, PdfOptions.GetMargin(PdfMargin.Normal));
        Assert.Equal(54, PdfOptions.GetMargin(PdfMargin.Wide));
        // 越界值按普通边距处理，不抛异常。
        Assert.Equal(36, PdfOptions.GetMargin((PdfMargin)99));
    }

    [Fact]
    public void PdfOptions_GetColumnCount_ClampsIntoRange()
    {
        Assert.Equal(1, PdfOptions.GetColumnCount(0));
        Assert.Equal(1, PdfOptions.GetColumnCount(-3));
        Assert.Equal(2, PdfOptions.GetColumnCount(2));
        Assert.Equal(4, PdfOptions.GetColumnCount(4));
        Assert.Equal(4, PdfOptions.GetColumnCount(99));
    }

    // ------------------------------------------------------------------
    // 分页排版
    // ------------------------------------------------------------------

    [Fact]
    public void PdfLayout_EmptyInput_YieldsNoPages()
    {
        var pages = PdfLayout.Build(Array.Empty<PdfBlock>(), CreateOptions(new Size(793.7, 1122.5)));

        Assert.Empty(pages);
    }

    [Fact]
    public void PdfLayout_GetColumnWidth_AccountsForMarginAndGaps()
    {
        // (793.7 - 36 * 2 - 20) / 2 = 350.85
        var twoColumns = CreateOptions(new Size(793.7, 1122.5), margin: 36, columns: 2, columnGap: 20);
        Assert.Equal(350.85, PdfLayout.GetColumnWidth(twoColumns), 6);

        // 单列没有列间距：(793.7 - 72) / 1 = 721.7
        var singleColumn = CreateOptions(new Size(793.7, 1122.5), margin: 36, columns: 1);
        Assert.Equal(721.7, PdfLayout.GetColumnWidth(singleColumn), 6);

        // 非法参数按默认值夹取，不抛异常、也不会算出非正宽度。
        var broken = new PdfLayoutOptions { PageSize = new Size(0, double.NaN), Columns = -1, Margin = -10 };
        Assert.True(PdfLayout.GetColumnWidth(broken) > 0);
    }

    [Fact]
    public void PdfLayout_SingleColumn_PlacesBlocksInOrderWithGaps()
    {
        var options = CreateOptions(new Size(793.7, 1122.5), margin: 36, columns: 1, blockGap: 10);
        var blocks = new PdfBlock[]
        {
            new(PdfBlockKind.Note, 100),
            new(PdfBlockKind.Note, 200),
            new(PdfBlockKind.Note, 50)
        };

        var pages = PdfLayout.Build(blocks, options);

        var page = Assert.Single(pages);
        Assert.Equal(793.7, page.Width, 6);
        Assert.Equal(1122.5, page.Height, 6);
        Assert.Equal(3, page.Placements.Count);

        // 顺序、起始位置与列宽。
        Assert.Equal(0, page.Placements[0].BlockIndex);
        Assert.Equal(36, page.Placements[0].X, 6);
        Assert.Equal(36, page.Placements[0].Y, 6);
        Assert.Equal(721.7, page.Placements[0].Width, 6);
        Assert.Equal(100, page.Placements[0].Height, 6);

        // 36 + 100 + 10 = 146；146 + 200 + 10 = 356。
        Assert.Equal(1, page.Placements[1].BlockIndex);
        Assert.Equal(146, page.Placements[1].Y, 6);
        Assert.Equal(2, page.Placements[2].BlockIndex);
        Assert.Equal(356, page.Placements[2].Y, 6);
    }

    [Fact]
    public void PdfLayout_TwoColumns_FillsFirstColumnThenSecond()
    {
        // 页面 400×300、边距 20：内容区 360×260，列宽 (360 - 20) / 2 = 170。
        var options = CreateOptions(new Size(400, 300), margin: 20, columns: 2, columnGap: 20);
        var blocks = new PdfBlock[]
        {
            new(PdfBlockKind.Note, 120),
            new(PdfBlockKind.Note, 120),
            new(PdfBlockKind.Note, 120)
        };

        var pages = PdfLayout.Build(blocks, options);

        var page = Assert.Single(pages);
        Assert.Equal(3, page.Placements.Count);

        // 第 1 列：y=20、y=140（140 + 120 = 260 ≤ 280 的底边）。
        Assert.Equal(20, page.Placements[0].X, 6);
        Assert.Equal(20, page.Placements[0].Y, 6);
        Assert.Equal(20, page.Placements[1].X, 6);
        Assert.Equal(140, page.Placements[1].Y, 6);

        // 第 2 列从 x = 20 + 170 + 20 = 210、y 回到 20 开始。
        Assert.Equal(2, page.Placements[2].BlockIndex);
        Assert.Equal(210, page.Placements[2].X, 6);
        Assert.Equal(20, page.Placements[2].Y, 6);
        Assert.Equal(170, page.Placements[2].Width, 6);
    }

    [Fact]
    public void PdfLayout_ColumnOverflow_StartsNewPageAtMargin()
    {
        var options = CreateOptions(new Size(400, 300), margin: 20, columns: 2, columnGap: 20);
        var blocks = new PdfBlock[]
        {
            new(PdfBlockKind.Note, 200),
            new(PdfBlockKind.Note, 200),
            new(PdfBlockKind.Note, 200)
        };

        var pages = PdfLayout.Build(blocks, options);

        // 前两块各占一列，第三块放不下就换页。
        Assert.Equal(2, pages.Count);
        Assert.Equal(2, pages[0].Placements.Count);
        Assert.Single(pages[1].Placements);

        var wrapped = pages[1].Placements[0];
        Assert.Equal(2, wrapped.BlockIndex);
        Assert.Equal(20, wrapped.X, 6);
        Assert.Equal(20, wrapped.Y, 6);
        Assert.Equal(170, wrapped.Width, 6);
        Assert.Equal(200, wrapped.Height, 6);
    }

    [Fact]
    public void PdfLayout_BlocksNeverSpanPagesOrColumns()
    {
        var pageSize = new Size(500, 400);
        const double margin = 30;
        var options = CreateOptions(pageSize, margin: margin, columns: 2, columnGap: 10, blockGap: 5);
        var blocks = new PdfBlock[]
        {
            new(PdfBlockKind.Note, 80),
            new(PdfBlockKind.Note, 120),
            new(PdfBlockKind.Note, 160),
            new(PdfBlockKind.Note, 90),
            new(PdfBlockKind.Note, 200),
            new(PdfBlockKind.SubjectHeader, 50),
            new(PdfBlockKind.Note, 110)
        };

        var pages = PdfLayout.Build(blocks, options);

        var contentHeight = pageSize.Height - margin * 2;
        Assert.Equal(blocks.Length, pages.Sum(page => page.Placements.Count));

        foreach (var page in pages)
        {
            Assert.Equal(pageSize.Width, page.Width, 6);
            Assert.Equal(pageSize.Height, page.Height, 6);

            foreach (var placement in page.Placements)
            {
                Assert.True(placement.Y >= margin - 1e-6, "块不能落进上边距");
                // 块要么整块落在内容区里，要么本身就比内容区高（超高块允许溢出，但不拆开）。
                Assert.True(
                    placement.Y + placement.Height <= margin + contentHeight + 1e-6
                    || placement.Height > contentHeight + 1e-6,
                    $"块 {placement.BlockIndex} 跨页裁开了");
            }
        }
    }

    [Fact]
    public void PdfLayout_SubjectStartsNewPage_PutsHeaderOnFreshPage()
    {
        var options = CreateOptions(new Size(500, 400), margin: 20, columns: 1, subjectStartsNewPage: true);
        var blocks = new PdfBlock[]
        {
            new(PdfBlockKind.Note, 100),
            new(PdfBlockKind.SubjectHeader, 40),
            new(PdfBlockKind.Note, 100)
        };

        var pages = PdfLayout.Build(blocks, options);

        Assert.Equal(2, pages.Count);
        Assert.Single(pages[0].Placements);
        Assert.Equal(0, pages[0].Placements[0].BlockIndex);

        Assert.Equal(2, pages[1].Placements.Count);
        Assert.Equal(1, pages[1].Placements[0].BlockIndex);
        Assert.Equal(20, pages[1].Placements[0].X, 6);
        Assert.Equal(20, pages[1].Placements[0].Y, 6);
        Assert.Equal(2, pages[1].Placements[1].BlockIndex);
        Assert.Equal(60, pages[1].Placements[1].Y, 6);
    }

    [Fact]
    public void PdfLayout_OrphanHeaderMovesToNextPageWithFollowingNote()
    {
        // 内容区高 360：第一张卡片占 200，剩 160。标题（50）自己放得下，
        // 但「标题 + 紧随卡片（120）」放不下，所以标题要整体挪走，不能孤零零留在页底。
        var options = CreateOptions(new Size(400, 400), margin: 20, columns: 1);
        var blocks = new PdfBlock[]
        {
            new(PdfBlockKind.Note, 200),
            new(PdfBlockKind.SubjectHeader, 50),
            new(PdfBlockKind.Note, 120)
        };

        var pages = PdfLayout.Build(blocks, options);

        Assert.Equal(2, pages.Count);
        Assert.Single(pages[0].Placements);
        Assert.Equal(0, pages[0].Placements[0].BlockIndex);

        Assert.Equal(2, pages[1].Placements.Count);
        Assert.Equal(1, pages[1].Placements[0].BlockIndex);
        Assert.Equal(20, pages[1].Placements[0].Y, 6);
        Assert.Equal(2, pages[1].Placements[1].BlockIndex);
        Assert.Equal(70, pages[1].Placements[1].Y, 6);
    }

    [Fact]
    public void PdfLayout_HeaderStaysWithFollowingNoteWhenTheyFitTogether()
    {
        // 反例：整体放得下时不能「多此一举」把标题挪走。
        var options = CreateOptions(new Size(400, 400), margin: 20, columns: 1);
        var blocks = new PdfBlock[]
        {
            new(PdfBlockKind.Note, 120),
            new(PdfBlockKind.SubjectHeader, 50),
            new(PdfBlockKind.Note, 100)
        };

        var pages = PdfLayout.Build(blocks, options);

        var page = Assert.Single(pages);
        Assert.Equal(3, page.Placements.Count);
        Assert.Equal(140, page.Placements[1].Y, 6);
        Assert.Equal(190, page.Placements[2].Y, 6);
    }

    [Fact]
    public void PdfLayout_OversizedBlockIsPlacedWholeAndDoesNotSplit()
    {
        var options = CreateOptions(new Size(400, 300), margin: 20, columns: 2, columnGap: 20);
        var blocks = new PdfBlock[]
        {
            new(PdfBlockKind.Note, 500),
            new(PdfBlockKind.Note, 100)
        };

        var pages = PdfLayout.Build(blocks, options);

        var page = Assert.Single(pages);
        Assert.Equal(2, page.Placements.Count);

        // 比整列还高的块也整块放在列首（溢出由调用方裁），绝不拆开。
        Assert.Equal(20, page.Placements[0].Y, 6);
        Assert.Equal(500, page.Placements[0].Height, 6);

        // 本列之后就算满，下一块换到第 2 列、同样从顶部开始。
        Assert.Equal(210, page.Placements[1].X, 6);
        Assert.Equal(20, page.Placements[1].Y, 6);
    }

    [Fact]
    public void PdfLayout_InvalidOptionsAreClampedInsteadOfThrowing()
    {
        var options = new PdfLayoutOptions
        {
            PageSize = new Size(double.NaN, -1),
            Margin = -10,
            Columns = 0,
            ColumnGap = double.NaN,
            BlockGap = -5
        };
        var blocks = new PdfBlock[] { new(PdfBlockKind.Note, 10) };

        var pages = PdfLayout.Build(blocks, options);

        // 页尺寸坏值回退 A4；负边距按 0；列数 0 夹成 1；间距坏值按 0。
        var page = Assert.Single(pages);
        Assert.Equal(793.7, page.Width, 6);
        Assert.Equal(1122.5, page.Height, 6);
        Assert.Equal(0, page.Placements[0].X, 6);
        Assert.Equal(0, page.Placements[0].Y, 6);
        Assert.Equal(793.7, page.Placements[0].Width, 6);
    }

    [Fact]
    public void PdfLayout_HugeMarginKeepsColumnWidthPositive()
    {
        var options = CreateOptions(new Size(400, 300), margin: 10_000, columns: 2, columnGap: 50);
        var blocks = new PdfBlock[] { new(PdfBlockKind.Note, 10) };

        var pages = PdfLayout.Build(blocks, options);

        var page = Assert.Single(pages);
        Assert.True(page.Placements[0].Width > 0, "边距再大也不能算出非正的列宽");
        Assert.True(page.Placements[0].X >= 0);
    }

    [Fact]
    public void PdfLayout_ColumnsAboveMaxAreClampedToFour()
    {
        var options = CreateOptions(new Size(1000, 500), margin: 10, columns: 99);
        var blocks = new PdfBlock[] { new(PdfBlockKind.Note, 10) };

        var pages = PdfLayout.Build(blocks, options);

        var page = Assert.Single(pages);
        Assert.Equal((1000 - 20) / 4.0, page.Placements[0].Width, 6);
    }

    // ------------------------------------------------------------------
    // PDF 组装器
    // ------------------------------------------------------------------

    [Fact]
    public void PdfWriter_OnePageWithoutBookmarks_IsAValidPdf()
    {
        var jpeg = MakeFakeJpeg(800, 600);
        var pages = new PdfPageImage[] { new(jpeg, 595.276, 841.89) };

        using var stream = new MemoryStream();
        PdfDocumentWriter.Write(stream, pages, "作业板");

        var pdf = ParsePdf(stream.ToArray());

        Assert.Single(Regex.Matches(pdf.Text, @"/Type /Page(?!s)").Cast<Match>());
        Assert.Contains("/Type /Catalog", pdf.Text);
        Assert.Contains("/Type /Pages", pdf.Text);
        Assert.Contains("/Count 1", pdf.Text);
        Assert.Contains("/MediaBox [0 0 595.276 841.89]", pdf.Text);
        Assert.Contains("/Filter /DCTDecode", pdf.Text);
        Assert.DoesNotContain("/Outlines", pdf.Text);

        // 图像字典里的 /Width /Height 是 JPEG 的像素尺寸（从 SOF 段读出来），不是页面 point 尺寸。
        Assert.Contains("/Width 800 /Height 600", pdf.Text);

        // Info 里的标题也必须走 UTF-16BE 十六进制串。
        var titleHex = Regex.Match(pdf.Text, @"/Title <([0-9A-F]+)>").Groups[1].Value;
        Assert.Equal("作业板", DecodeUtf16BeHex(titleHex));
        Assert.Contains("/Producer (Corkboard)", pdf.Text);
    }

    [Fact]
    public void PdfWriter_WritesChineseBookmarksPointingAtTheirPages()
    {
        var pages = new PdfPageImage[]
        {
            new(MakeFakeJpeg(400, 300), 400, 300, "第一章"),
            new(MakeFakeJpeg(400, 300), 400, 300),
            new(MakeFakeJpeg(400, 300), 300, 500, "作业清单")
        };

        using var stream = new MemoryStream();
        PdfDocumentWriter.Write(stream, pages);

        var pdf = ParsePdf(stream.ToArray());

        Assert.Equal(3, Regex.Matches(pdf.Text, @"/Type /Page(?!s)").Count);

        // Catalog 挂上目录并让阅读器默认显示书签栏。
        var catalog = pdf.Body(1);
        Assert.Contains("/Outlines", catalog);
        Assert.Contains("/PageMode /UseOutlines", catalog);

        var outlines = pdf.Objects().Single(item => item.Body.Contains("/Type /Outlines", StringComparison.Ordinal));
        Assert.Contains("/Count 2", outlines.Body);

        // 页面对象的对象号顺序就是页序（组装器按页依次编号）。
        var pageObjectNumbers = pdf.Objects()
            .Where(item => item.Body.Contains("/Type /Page ", StringComparison.Ordinal))
            .Select(item => item.Number)
            .ToArray();
        Assert.Equal(3, pageObjectNumbers.Length);

        var outlineItems = pdf.Objects()
            .Where(item => item.Body.Contains("/Dest [", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, outlineItems.Length);

        // 书签标题是 UTF-16BE 十六进制串，能解回中文。
        var titles = outlineItems
            .Select(item => DecodeUtf16BeHex(Regex.Match(item.Body, @"/Title <([0-9A-F]+)>").Groups[1].Value))
            .ToArray();
        Assert.Equal(new[] { "第一章", "作业清单" }, titles);

        // 第一项没有 Prev，最后一项没有 Next，中间两头都连着。
        Assert.DoesNotContain("/Prev", outlineItems[0].Body);
        Assert.Contains("/Next", outlineItems[0].Body);
        Assert.Contains("/Prev", outlineItems[1].Body);
        Assert.DoesNotContain("/Next", outlineItems[1].Body);

        // Dest 各自指向正确的页。
        AssertDestPage(outlineItems[0].Body, pageObjectNumbers[0]);
        AssertDestPage(outlineItems[1].Body, pageObjectNumbers[2]);
    }

    [Fact]
    public void PdfWriter_JpegStreamBoundaryMatchesDeclaredLength()
    {
        var jpeg = MakeFakeJpeg(640, 480);

        using var stream = new MemoryStream();
        PdfDocumentWriter.Write(stream, new PdfPageImage[] { new(jpeg, 640, 480) });

        var bytes = stream.ToArray();
        var pdf = ParsePdf(bytes);

        var image = pdf.Objects().Single(item => item.Body.Contains("/Subtype /Image", StringComparison.Ordinal));
        var declaredLength = int.Parse(
            Regex.Match(image.Body, @"/Length (\d+)").Groups[1].Value,
            CultureInfo.InvariantCulture);

        Assert.Equal(jpeg.Length, declaredLength);

        // /Length 后面紧跟的就是原始 JPEG 字节，之后才是换行 + endstream。
        var bodyStart = (int)pdf.ObjectOffsets[image.Number - 1];
        var dataStart = bodyStart + image.Body.IndexOf("stream\n", StringComparison.Ordinal) + "stream\n".Length;
        Assert.Equal(jpeg, bytes[dataStart..(dataStart + declaredLength)]);
        Assert.Equal((byte)'\n', bytes[dataStart + declaredLength]);
        Assert.Equal("endstream", Encoding.ASCII.GetString(bytes, dataStart + declaredLength + 1, "endstream".Length));
    }

    [Fact]
    public void PdfWriter_EmptyPageList_ProducesValidEmptyDocument()
    {
        using var stream = new MemoryStream();
        PdfDocumentWriter.Write(stream, Array.Empty<PdfPageImage>());

        var pdf = ParsePdf(stream.ToArray());

        Assert.Contains("/Type /Catalog", pdf.Text);
        Assert.Contains("/Type /Pages", pdf.Text);
        Assert.Contains("/Kids [] /Count 0", pdf.Text);
        Assert.DoesNotContain("/Type /Page ", pdf.Text);
        Assert.DoesNotContain("/Outlines", pdf.Text);
    }

    [Fact]
    public void PdfWriter_EmptyJpegDataThrowsWithPageNumber()
    {
        var firstEmpty = new PdfPageImage[]
        {
            new(Array.Empty<byte>(), 100, 100),
            new(MakeFakeJpeg(20, 10), 100, 100)
        };
        var firstError = Assert.Throws<ArgumentException>(
            () => PdfDocumentWriter.Write(new MemoryStream(), firstEmpty));
        Assert.Contains("第 1 页", firstError.Message, StringComparison.Ordinal);

        var secondEmpty = new PdfPageImage[]
        {
            new(MakeFakeJpeg(20, 10), 100, 100),
            new(Array.Empty<byte>(), 100, 100)
        };
        var secondError = Assert.Throws<ArgumentException>(
            () => PdfDocumentWriter.Write(new MemoryStream(), secondEmpty));
        Assert.Contains("第 2 页", secondError.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // 测试辅助
    // ------------------------------------------------------------------

    /// <summary>搭一份排版参数，只写关心的那几项。</summary>
    private static PdfLayoutOptions CreateOptions(
        Size pageSize,
        double margin = 0,
        int columns = 1,
        double columnGap = 0,
        double blockGap = 0,
        bool subjectStartsNewPage = false)
    {
        return new PdfLayoutOptions
        {
            PageSize = pageSize,
            Margin = margin,
            Columns = columns,
            ColumnGap = columnGap,
            BlockGap = blockGap,
            SubjectStartsNewPage = subjectStartsNewPage
        };
    }

    /// <summary>
    ///     造一份「结构足够」的假 JPEG：SOI + APP0 + SOF0（带宽高）+ EOI，后面再塞几个字节。
    ///     它不是可解码的图像，但组装器只需要原始字节与 SOF 里的像素宽高。
    /// </summary>
    private static byte[] MakeFakeJpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        // APP0（JFIF）：段长 16。
        bytes.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10 });
        bytes.AddRange(Encoding.ASCII.GetBytes("JFIF\0"));
        bytes.AddRange(new byte[] { 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 });

        // SOF0：段长 17、精度 8、高、宽、3 个分量。
        bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08 });
        bytes.Add((byte)(height >> 8));
        bytes.Add((byte)(height & 0xFF));
        bytes.Add((byte)(width >> 8));
        bytes.Add((byte)(width & 0xFF));
        bytes.AddRange(new byte[] { 0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01 });

        // EOI 之后故意混进 0x00 / 0xFF，验证流是按 /Length 截取的。
        bytes.AddRange(new byte[] { 0xFF, 0xD9, 0x11, 0x22, 0x00, 0xFF, 0x33 });
        return bytes.ToArray();
    }

    /// <summary>解析出来的 PDF：Latin1 文本（字符下标 == 字节偏移）、各对象偏移与原始字节。</summary>
    private sealed class ParsedPdf
    {
        public required string Text { get; init; }
        public required byte[] Bytes { get; init; }
        public required IReadOnlyList<long> ObjectOffsets { get; init; }

        /// <summary>取第 <paramref name="objectNumber" /> 号对象的正文（从对象头到 endobj 之前）。</summary>
        public string Body(int objectNumber)
        {
            var start = (int)ObjectOffsets[objectNumber - 1];
            var end = Text.IndexOf("endobj", start, StringComparison.Ordinal);
            Assert.True(end > start, $"PDF 对象 {objectNumber} 没有 endobj");
            return Text[start..end];
        }

        /// <summary>按对象号枚举 (对象号, 正文)。</summary>
        public IEnumerable<(int Number, string Body)> Objects()
        {
            for (var number = 1; number <= ObjectOffsets.Count; number++)
                yield return (number, Body(number));
        }
    }

    /// <summary>
    ///     极简 PDF 解析器：只验证结构——%PDF 头、startxref 定位、xref 每项 20 字节且偏移指向
    ///     「N 0 obj」、trailer 的 /Size 与 /Root、每个 stream 的 /Length 与实际字节数。
    /// </summary>
    private static ParsedPdf ParsePdf(byte[] bytes)
    {
        // Latin1 一个字符对应一个字节，于是字符下标可以直接当文件偏移用。
        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4\n", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF", text, StringComparison.Ordinal);

        var startXrefIndex = text.LastIndexOf("startxref", StringComparison.Ordinal);
        Assert.True(startXrefIndex >= 0, "PDF 里没有 startxref");

        var valueStart = startXrefIndex + "startxref".Length;
        var valueEnd = text.IndexOf("%%EOF", valueStart, StringComparison.Ordinal);
        Assert.True(valueEnd > valueStart, "startxref 后面没有偏移");

        var xrefOffset = long.Parse(text[valueStart..valueEnd].Trim(), CultureInfo.InvariantCulture);
        Assert.InRange(xrefOffset, 0, bytes.Length - 1);

        var xrefPos = (int)xrefOffset;
        Assert.Equal("xref\n", text.Substring(xrefPos, "xref\n".Length));

        var headerStart = xrefPos + "xref\n".Length;
        var headerEnd = text.IndexOf('\n', headerStart);
        var header = text[headerStart..headerEnd].Split(' ');
        Assert.Equal("0", header[0]);
        var entryCount = int.Parse(header[1], CultureInfo.InvariantCulture);
        Assert.True(entryCount >= 1);

        // xref 第 0 项固定是空闲项；每项恰好 20 字节。
        var entryPos = headerEnd + 1;
        Assert.Equal("0000000000 65535 f \n", text.Substring(entryPos, 20));
        entryPos += 20;

        var offsets = new long[entryCount - 1];
        for (var i = 0; i < offsets.Length; i++)
        {
            var entry = text.Substring(entryPos + i * 20, 20);
            Assert.Equal(20, entry.Length);
            Assert.EndsWith(" 00000 n \n", entry, StringComparison.Ordinal);
            Assert.True(entry[..10].All(char.IsAsciiDigit), $"xref 第 {i + 1} 项偏移不是数字：{entry}");
            offsets[i] = long.Parse(entry[..10], CultureInfo.InvariantCulture);
        }

        var trailer = text[(entryPos + offsets.Length * 20)..startXrefIndex];
        Assert.StartsWith("trailer\n<< ", trailer, StringComparison.Ordinal);
        Assert.Contains($"/Size {entryCount}", trailer, StringComparison.Ordinal);
        Assert.Contains("/Root ", trailer, StringComparison.Ordinal);

        var pdf = new ParsedPdf { Text = text, Bytes = bytes, ObjectOffsets = offsets };

        // 每个 xref 偏移都要正好落在「N 0 obj」上。
        for (var i = 0; i < offsets.Length; i++)
        {
            Assert.InRange(offsets[i], 1, bytes.Length - 1);
            Assert.StartsWith($"{i + 1} 0 obj\n", text[(int)offsets[i]..], StringComparison.Ordinal);
        }

        // 每个 stream 的 /Length 与实际字节数一致，数据后面紧跟换行 + endstream。
        foreach (var (number, body) in pdf.Objects())
        {
            var streamIndex = body.IndexOf("stream\n", StringComparison.Ordinal);
            if (streamIndex < 0)
                continue;

            var lengthMatch = Regex.Match(body[..streamIndex], @"/Length (\d+)");
            Assert.True(lengthMatch.Success, $"对象 {number} 的 stream 没有 /Length");

            var length = int.Parse(lengthMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var dataStart = (int)offsets[number - 1] + streamIndex + "stream\n".Length;
            Assert.True(dataStart + length < bytes.Length, $"对象 {number} 的 stream 长度越界");
            Assert.Equal('\n', text[dataStart + length]);
            Assert.Equal("endstream", text.Substring(dataStart + length + 1, "endstream".Length));
        }

        return pdf;
    }

    /// <summary>书签的 /Dest 必须指向给定页对象，并且落在页面顶端（/XYZ 0 H 0）。</summary>
    private static void AssertDestPage(string outlineBody, int expectedPageObjectNumber)
    {
        var match = Regex.Match(outlineBody, @"/Dest \[(\d+) 0 R /XYZ 0 ([\d.]+) 0\]");
        Assert.True(match.Success, $"书签没有合法的 Dest：{outlineBody}");
        Assert.Equal(
            expectedPageObjectNumber,
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>把 PDF 十六进制串（FEFF 开头的 UTF-16BE）解回字符串。</summary>
    private static string DecodeUtf16BeHex(string hex)
    {
        Assert.StartsWith("FEFF", hex, StringComparison.Ordinal);
        Assert.Equal(0, (hex.Length - 4) % 4);

        var chars = new char[(hex.Length - 4) / 4];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)int.Parse(
                hex.AsSpan(4 + i * 4, 4),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
        }

        return new string(chars);
    }
}
