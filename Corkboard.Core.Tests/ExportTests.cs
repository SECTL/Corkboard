using System.Text.Json;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Export;
using Corkboard.Core.Models;
using Corkboard.Core.Models.SubConfigs.Export;

namespace Corkboard.Core.Tests;

/// <summary>
///     导出层契约：默认文件名（时间戳 + 扩展名）与导出设置（落盘键、默认值、归一）。
///     <para>
///         表格本身长什么样由 <see cref="ExportTableTests" /> 覆盖，这里只管「文件叫什么」与「设置怎么存」。
///     </para>
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
        Assert.Equal("2026-10-18_14-30-05.xlsx", ExportFileNameBuilder.Build(now, ".xlsx"));
        Assert.Equal("2026-10-18_14-30-05.png", ExportFileNameBuilder.Build(now, ".PNG"));
        Assert.Equal("2026-10-18_14-30-05.png", ExportFileNameBuilder.Build(now, "  .png  "));
    }

    [Fact]
    public void ExportFileName_EnsureExtension_AppendsWhenMissing()
    {
        Assert.Equal("作业板.xlsx", ExportFileNameBuilder.EnsureExtension("作业板", ".xlsx"));
        Assert.Equal("作业板.xlsx", ExportFileNameBuilder.EnsureExtension("作业板", "xlsx"));
    }

    [Fact]
    public void ExportFileName_EnsureExtension_KeepsExistingExtension()
    {
        // 扩展名比较不分大小写：已经是 .XLSX 就不再补一个。
        Assert.Equal("report.xlsx", ExportFileNameBuilder.EnsureExtension("report.xlsx", ".XLSX"));
    }

    [Fact]
    public void ExportFileName_EnsureExtension_ReplacesForeignExtension()
    {
        Assert.Equal("report.xlsx", ExportFileNameBuilder.EnsureExtension("report.txt", "xlsx"));
    }

    [Fact]
    public void ExportFileName_EnsureExtension_EmptyExtensionLeavesNameAlone()
    {
        Assert.Equal("report.xlsx", ExportFileNameBuilder.EnsureExtension("report.xlsx", string.Empty));
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

        // 表格：默认按科目分块、不带已清理的作业、跟随板面顺序。
        Assert.True(settings.TableGroupBySubject);
        Assert.False(settings.TableIncludeCleaned);
        Assert.Equal(ExportSortMode.FollowBoard, settings.TableSortMode);
    }

    [Fact]
    public void ExportFormat_KeepsItsOnDiskValues()
    {
        // 枚举按数字落盘：图片一直是 0，PDF 换成表格后仍然是 1（老配置读回来还是「第二种格式」）。
        Assert.Equal(0, (int)ExportFormat.Image);
        Assert.Equal(1, (int)ExportFormat.Table);
    }

    [Fact]
    public void ExportSettings_RoundTripsThroughJsonAsSnakeCase()
    {
        var config = new MainConfigModel();
        var export = config.ExportSettings;
        export.AskForFormat = false;
        export.DefaultFormat = ExportFormat.Table;
        export.TableGroupBySubject = false;
        export.TableIncludeCleaned = true;
        export.TableSortMode = ExportSortMode.Subject;

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);

        // 子配置必须挂在 export_settings 下（snake_case 由统一 JSON 选项生成），枚举按数字落盘。
        Assert.Contains("\"export_settings\"", json);
        Assert.Contains("\"ask_for_format\": false", json);
        Assert.Contains("\"default_format\": 1", json);
        Assert.Contains("\"table_group_by_subject\": false", json);
        Assert.Contains("\"table_include_cleaned\": true", json);
        Assert.Contains("\"table_sort_mode\": 4", json);

        // 旧的 PDF 字段必须彻底消失，别留下「读得到但不生效」的键。
        Assert.DoesNotContain("pdf_", json);

        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)!;
        var roundTripped = restored.ExportSettings;

        Assert.False(roundTripped.AskForFormat);
        Assert.Equal(ExportFormat.Table, roundTripped.DefaultFormat);
        Assert.False(roundTripped.TableGroupBySubject);
        Assert.True(roundTripped.TableIncludeCleaned);
        Assert.Equal(ExportSortMode.Subject, roundTripped.TableSortMode);
    }

    [Fact]
    public void ExportSettings_MissingSectionKeepsDefaults()
    {
        // 老版本 settings.json 里没有 export_settings：读回来必须是默认值，不能是 null 或 0。
        var restored = JsonSerializer.Deserialize<MainConfigModel>(
            """{ "board_settings": { "board_name": "作业" } }""", ConfigServiceBase.JsonOptions)!;

        Assert.Equal(ExportFormat.Image, restored.ExportSettings.DefaultFormat);
        Assert.True(restored.ExportSettings.AskForFormat);
        Assert.True(restored.ExportSettings.TableGroupBySubject);
        Assert.False(restored.ExportSettings.TableIncludeCleaned);
        Assert.Equal(ExportSortMode.FollowBoard, restored.ExportSettings.TableSortMode);
    }

    [Fact]
    public void ExportSettings_LegacyPdfKeysAreIgnored()
    {
        // 老配置里那一堆 pdf_* 现在没有对应字段：读的时候静默忽略，剩下的值照常生效。
        var restored = JsonSerializer.Deserialize<MainConfigModel>(
            """
            {
              "export_settings": {
                "ask_for_format": false,
                "default_format": 1,
                "pdf_page_size": 2,
                "pdf_columns": 4,
                "pdf_sort_mode": 4,
                "table_group_by_subject": false
              }
            }
            """, ConfigServiceBase.JsonOptions)!;

        var settings = restored.ExportSettings;
        Assert.False(settings.AskForFormat);
        Assert.Equal(ExportFormat.Table, settings.DefaultFormat);
        Assert.False(settings.TableGroupBySubject);
        Assert.False(settings.TableIncludeCleaned);
    }

    [Fact]
    public void ExportSettings_Normalize_FallsBackFromUnknownEnums()
    {
        var settings = new ExportSettingsConfig
        {
            DefaultFormat = (ExportFormat)9,
            TableSortMode = (ExportSortMode)42
        };

        settings.Normalize();

        Assert.Equal(ExportFormat.Image, settings.DefaultFormat);
        Assert.Equal(ExportSortMode.FollowBoard, settings.TableSortMode);
    }

    [Fact]
    public void MainConfig_NormalizeLoaded_NormalizesExportSettings()
    {
        var config = new MainConfigModel();
        config.ExportSettings.DefaultFormat = (ExportFormat)7;
        config.ExportSettings.TableSortMode = (ExportSortMode)(-3);

        config.NormalizeLoaded();

        Assert.Equal(ExportFormat.Image, config.ExportSettings.DefaultFormat);
        Assert.Equal(ExportSortMode.FollowBoard, config.ExportSettings.TableSortMode);
    }

    [Fact]
    public void ExportSettings_DirtyJsonIsCleanedAfterNormalizeLoaded()
    {
        // 模拟读盘：手改坏的 settings.json 由 NormalizeLoaded 收干净，导出之前就已是合法值。
        var restored = JsonSerializer.Deserialize<MainConfigModel>(
            """{ "export_settings": { "default_format": 7, "table_sort_mode": 99 } }""",
            ConfigServiceBase.JsonOptions)!;

        restored.NormalizeLoaded();

        Assert.Equal(ExportFormat.Image, restored.ExportSettings.DefaultFormat);
        Assert.Equal(ExportSortMode.FollowBoard, restored.ExportSettings.TableSortMode);
    }
}
