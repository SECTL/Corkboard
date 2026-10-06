using System.Text.Json;
using Avalonia.Media;
using Corkboard.Core;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Enums;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Models.SubConfigs.Board;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Core.Tests;

/// <summary>
///     配置序列化契约：磁盘上是 snake_case、颜色是十六进制字符串、旧版平铺的 basic 段要能被迁移。
///     这几条一旦破了，老用户的配置文件就会静默回退成默认值。
/// </summary>
public class ConfigSerializationTests
{
    [Fact]
    public void MainConfig_RoundTripsWithSnakeCasePropertyNames()
    {
        var config = new MainConfigModel();
        config.Basic.Language = LanguageMode.English;
        config.Basic.MainWindowWidth = 1440;
        config.Appearance.Theme = ThemeMode.Dark;
        config.Appearance.ThemeColor = Color.Parse("#FF112233");

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);

        Assert.Contains("\"main_window_width\": 1440", json);
        // 枚举按上游约定写成数字（JsonOptions 里没有字符串枚举转换器），改枚举顺序等价于改配置格式。
        Assert.Contains("\"language\": 1", json);

        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)!;
        Assert.Equal(LanguageMode.English, restored.Basic.Language);
        Assert.Equal(1440, restored.Basic.MainWindowWidth);
        Assert.Equal(ThemeMode.Dark, restored.Appearance.Theme);
        // 颜色的序列化形态是 #RRGGBBAA（alpha 在尾部），只要求往返一致。
        Assert.Equal(config.Appearance.ThemeColor, restored.Appearance.ThemeColor);
    }

    [Fact]
    public void MainConfig_PersistsDesktopBottomPreferenceAsSnakeCase()
    {
        var config = new MainConfigModel();
        config.Basic.PinToDesktop = true;

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);

        Assert.Contains("\"pin_to_desktop\": true", json);

        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)!;
        Assert.True(restored.Basic.PinToDesktop);
    }

    [Fact]
    public void MainConfig_PersistsAutostartPreferenceAsSnakeCase()
    {
        var config = new MainConfigModel();
        config.Basic.Autostart = true;

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);

        Assert.Contains("\"autostart\": true", json);

        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)!;
        Assert.True(restored.Basic.Autostart);
    }

    [Fact]
    public void MainConfig_PersistsWindowOpacityAndClickThroughAsSnakeCase()
    {
        var config = new MainConfigModel();
        config.Basic.MainWindowOpacity = 0.7;
        config.Basic.ClickThrough = true;

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);

        Assert.Contains("\"main_window_opacity\": 0.7", json);
        Assert.Contains("\"click_through\": true", json);

        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)!;
        Assert.Equal(0.7, restored.Basic.MainWindowOpacity);
        Assert.True(restored.Basic.ClickThrough);
    }

    [Fact]
    public void MainConfig_WindowAppearanceDefaultsKeepWindowUsable()
    {
        var basic = new MainConfigModel().Basic;

        // 默认必须完全不透明、不穿透：新用户的主窗口要能看清、能点。
        Assert.Equal(1.0, basic.MainWindowOpacity);
        Assert.False(basic.ClickThrough);
    }

    [Fact]
    public void MainConfig_IgnoresRemovedBackgroundResidentField()
    {
        // 「关闭窗口后继续驻留」已移除：旧配置里的字段必须被静默忽略，而不是把反序列化炸掉。
        const string legacy = """
        {
          "general": { "basic": { "background_resident": false, "autostart": true } }
        }
        """;

        var restored = JsonSerializer.Deserialize<MainConfigModel>(legacy, ConfigServiceBase.JsonOptions)!;

        Assert.True(restored.Basic.Autostart);
    }

    [Fact]
    public void MainConfig_MigratesLegacyBasicSectionAndDropsRemovedField()
    {
        // 旧版把 basic 平铺在根上；迁移后段里的有效字段保留，已移除的字段被忽略且不抛异常。
        const string legacy = """
        {
          "basic": { "background_resident": true, "show_startup_window": false }
        }
        """;

        var restored = JsonSerializer.Deserialize<MainConfigModel>(legacy, ConfigServiceBase.JsonOptions)!;

        Assert.False(restored.Basic.ShowStartupWindow);
        // 迁移来的段里没有 autostart，必须落回默认关，而不是继承别的值。
        Assert.False(restored.Basic.Autostart);
    }

    [Fact]
    public void MainConfig_AutostartDefaultsToOff()
    {
        // 开机自启是系统集成，默认必须是关：没碰过设置的用户不该被写进注册表 / 自启目录。
        Assert.False(new MainConfigModel().Basic.Autostart);
    }

    [Fact]
    public void MainConfig_ShowStartupWindowDefaultsToOn()
    {
        // 启动即显示主窗口是默认行为（含开机自启场景）；关掉后才是只常驻托盘。
        Assert.True(new MainConfigModel().Basic.ShowStartupWindow);
    }

    [Fact]
    public void MainConfig_DeserializesLegacyBasicSectionIntoGeneral()
    {
        // language=2 是已移除的日语；旧配置必须能读进来并归一到默认中文，而不是变成未定义枚举值。
        const string legacy = """
        {
          "basic": { "language": 2, "show_startup_window": false },
          "appearance": { "theme": 2 }
        }
        """;

        var restored = JsonSerializer.Deserialize<MainConfigModel>(legacy, ConfigServiceBase.JsonOptions)!;

        Assert.Equal(LanguageMode.ChineseSimplified, restored.Basic.Language);
        Assert.False(restored.Basic.ShowStartupWindow);
        Assert.Equal(ThemeMode.Dark, restored.Appearance.Theme);
    }

    [Fact]
    public void LanguageMode_SerializesAsNumberAndDropsRemovedJapanese()
    {
        var config = new MainConfigModel();
        config.Basic.Language = LanguageMode.ChineseSimplified;

        Assert.Contains("\"language\": 0", JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions));

        // 越界值与字符串形态都要落到中文，不能让设置页拿到枚举未定义值。
        var outOfRange = JsonSerializer.Deserialize<MainConfigModel>("""{ "basic": { "language": 9 } }""",
            ConfigServiceBase.JsonOptions)!;
        Assert.Equal(LanguageMode.ChineseSimplified, outOfRange.Basic.Language);

        var named = JsonSerializer.Deserialize<MainConfigModel>("""{ "basic": { "language": "English" } }""",
            ConfigServiceBase.JsonOptions)!;
        Assert.Equal(LanguageMode.English, named.Basic.Language);
    }

    [Fact]
    public void ColorJsonConverter_AcceptsLegacyObjectForm()
    {
        const string legacy = """{ "appearance": { "theme_color": { "A": 255, "R": 1, "G": 2, "B": 3 } } }""";

        var restored = JsonSerializer.Deserialize<MainConfigModel>(legacy, ConfigServiceBase.JsonOptions)!;

        Assert.Equal(new Color(255, 1, 2, 3), restored.Appearance.ThemeColor);
    }

    [Fact]
    public void BoardSettings_RoundTripsNameAndModes()
    {
        var config = new MainConfigModel();
        config.BoardSettings.BoardName = "我的作业";
        config.BoardSettings.LayoutMode = BoardLayoutMode.Flow;
        config.BoardSettings.SortMode = BoardSortMode.Subject;

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);

        Assert.Contains("\"board_name\": \"我的作业\"", json);
        // 排布与排序同样是数字落盘：改枚举顺序等于改配置格式，只能在末尾追加。
        Assert.Contains("\"layout_mode\": 2", json);
        Assert.Contains("\"sort_mode\": 3", json);

        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)!;
        Assert.Equal("我的作业", restored.BoardSettings.BoardName);
        Assert.Equal(BoardLayoutMode.Flow, restored.BoardSettings.LayoutMode);
        Assert.Equal(BoardSortMode.Subject, restored.BoardSettings.SortMode);
    }

    [Fact]
    public void BoardSettings_BoardNameFallsBackToDefaultWhenBlank()
    {
        var settings = new BoardSettingsConfig();

        // 名称留空时用本地化资源里的默认名，这样没自定义过的用户升级界面语言会跟着走。
        Assert.Equal(CR.Board_Title, settings.ResolveBoardName());

        settings.BoardName = "  我的作业  ";
        Assert.Equal("我的作业", settings.ResolveBoardName());
    }

    [Fact]
    public void BoardNote_IgnoresRemovedLegacyFields()
    {
        // 老数据里的 title / color_hex / is_pinned 必须被静默忽略：
        // JsonOptions 没有开 UnmappedMemberHandling.Disallow，所以不需要写迁移代码。
        // content 现在是正经字段（作业内容），照常读回来。
        const string legacy = """
        { "notes": [ { "title": "旧便签", "content": "正文", "color_hex": "#FFFFFF", "is_pinned": true } ] }
        """;

        var restored = JsonSerializer.Deserialize<BoardDayFile>(legacy, ConfigServiceBase.JsonOptions)!;

        Assert.Single(restored.Notes);
        Assert.Equal("正文", restored.Notes[0].Content);
    }

    [Fact]
    public void BoardNote_NoLongerSerializesTitleOrCompletion()
    {
        var day = new BoardDayFile();
        day.Notes.Add(new BoardNote { Content = "正文", Subject = "数学" });

        var json = JsonSerializer.Serialize(day, ConfigServiceBase.JsonOptions);

        Assert.DoesNotContain("\"title\"", json);
        Assert.DoesNotContain("\"is_completed\"", json);
    }

    [Fact]
    public void BoardConfig_RoundTripsTypesAndFieldValues()
    {
        var config = new BoardConfig();
        config.Types.Clear();
        config.Types.Add(new BoardTypeDef
        {
            Name = "练习册",
            Fields = [new BoardTypeField { Label = "页数", Kind = BoardFieldKind.PageRange }]
        });

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);

        // 字段类型同样按数字落盘，只能在末尾增删成员。
        Assert.Contains("\"kind\": 3", json);
        // 计算属性不写进配置。
        Assert.DoesNotContain("display_name", json);
        Assert.DoesNotContain("display_label", json);
        Assert.DoesNotContain("kind_index", json);
        // 作业本身不在这个文件里：按创建日期分文件存（见 BoardNoteStore）。
        Assert.DoesNotContain("\"notes\"", json);

        var restored = JsonSerializer.Deserialize<BoardConfig>(json, ConfigServiceBase.JsonOptions)!;
        Assert.Single(restored.Types);
        Assert.Equal("练习册", restored.Types[0].Name);
        Assert.Equal(BoardFieldKind.PageRange, restored.Types[0].Fields[0].Kind);
    }

    [Fact]
    public void BoardDayFile_RoundTripsNotesWithValues()
    {
        var day = new BoardDayFile();
        day.Notes.Add(new BoardNote { Content = "数学", Values = { ["field-1"] = "12-15" } });

        var json = JsonSerializer.Serialize(day, ConfigServiceBase.JsonOptions);
        var restored = JsonSerializer.Deserialize<BoardDayFile>(json, ConfigServiceBase.JsonOptions)!;

        Assert.Single(restored.Notes);
        Assert.Equal("数学", restored.Notes[0].Content);
        Assert.Equal("12-15", restored.Notes[0].Values["field-1"]);
    }

    [Fact]
    public void LegacyBoardFile_ReadsNotesTypesAndSubjects()
    {
        // 旧版把三样东西塞在一个文件里，搬迁时必须都能读出来。
        const string legacy = """
        {
          "notes": [ { "content": "旧作业", "subject": "数学" } ],
          "types": [ { "name": "作文" } ],
          "subjects": [ "物理" ]
        }
        """;

        var restored = JsonSerializer.Deserialize<LegacyBoardFile>(legacy, ConfigServiceBase.JsonOptions)!;

        Assert.Single(restored.Notes);
        Assert.Equal("旧作业", restored.Notes[0].Content);
        Assert.Equal("作文", restored.Types[0].Name);
        Assert.Contains("物理", restored.Subjects);
    }

    [Fact]
    public void BoardConfig_SeedsCommonTypesOnFirstRun()
    {
        // 首次运行给几种常见作业类型打底，用户不用从零开始建类型。
        var config = new BoardConfig();

        Assert.NotEmpty(config.Types);
        Assert.Contains(config.Types, type => type.Fields.Any(field => field.Kind == BoardFieldKind.PageRange));
        Assert.NotEmpty(config.Subjects);
    }

    [Fact]
    public void BoardTypeField_DisplayHelpersFallBackWhenBlank()
    {
        var field = new BoardTypeField();

        Assert.Equal(CR.Board_UntitledField, field.DisplayLabel);
        Assert.Equal(CR.Board_Untitled, new BoardTypeDef().DisplayName);

        field.Label = "  页数  ";
        Assert.Equal("页数", field.DisplayLabel);
    }

    [Fact]
    public void BoardTypeField_KindIndexTracksKindAndClampsOutOfRange()
    {
        var field = new BoardTypeField { Kind = BoardFieldKind.Number };
        Assert.Equal((int)BoardFieldKind.Number, field.KindIndex);

        // 设置页绑的是 SelectedIndex，越界值不能把字段带到未定义枚举上。
        field.KindIndex = 99;
        Assert.Equal(BoardFieldKinds.All[^1], field.Kind);
    }

    [Fact]
    public void GlobalConstants_VersionIsVPrefixed()
    {
        Assert.StartsWith("v", GlobalConstants.Version);
        Assert.False(string.IsNullOrWhiteSpace(GlobalConstants.DisplayVersion));
    }

    [Fact]
    public void ConfigFilePath_PointsIntoConfigDirectory()
    {
        var path = new MainConfigModel().ConfigFilePath;

        Assert.EndsWith(Path.Combine("config", "settings.json"), path);
    }

    [Fact]
    public void BoardNote_RoundTripsContentFontSizeAndFormatRanges()
    {
        var day = new BoardDayFile();
        day.Notes.Add(new BoardNote
        {
            Content = "数学 **练习册**",
            ContentFontSize = 22,
            Formats =
            [
                new BoardTextFormatRange
                {
                    Start = 3,
                    Length = 5,
                    Color = Color.Parse("#FFFF0000"),
                    FontSize = 30
                }
            ]
        });

        var json = JsonSerializer.Serialize(day, ConfigServiceBase.JsonOptions);

        Assert.Contains("\"content_font_size\": 22", json);
        Assert.Contains("\"font_size\": 30", json);
        // 结束下标是从 start + length 推出来的，不进磁盘格式。
        Assert.DoesNotContain("\"end\"", json);

        var restored = JsonSerializer.Deserialize<BoardDayFile>(json, ConfigServiceBase.JsonOptions)!;
        var note = Assert.Single(restored.Notes);
        Assert.Equal("数学 **练习册**", note.Content);
        Assert.Equal(22, note.ContentFontSize);

        var range = Assert.Single(note.Formats);
        Assert.Equal(3, range.Start);
        Assert.Equal(5, range.Length);
        Assert.Equal(8, range.End);
        Assert.Equal(Color.Parse("#FFFF0000"), range.Color);
        Assert.Equal(30, range.FontSize);
    }

    [Fact]
    public void BoardNote_OldDataWithoutFormatRangesStillLoads()
    {
        // 旧数据里没有 formats / content_font_size：读出来是空表与空值，不需要写迁移代码。
        const string legacy = """{ "notes": [ { "content": "旧作业" } ] }""";

        var restored = JsonSerializer.Deserialize<BoardDayFile>(legacy, ConfigServiceBase.JsonOptions)!;

        Assert.Empty(restored.Notes[0].Formats);
        Assert.Null(restored.Notes[0].ContentFontSize);
    }

    [Fact]
    public void BoardSettings_RoundTripsContentDefaultsAndKeepsThemeFollowingAsNull()
    {
        var config = new MainConfigModel();
        config.BoardSettings.DefaultContentFontSize = 18;
        config.BoardSettings.DefaultContentColor = Color.Parse("#FF00AAFF");

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);

        Assert.Contains("\"default_content_font_size\": 18", json);
        Assert.Contains("\"default_content_color\"", json);

        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)!;
        Assert.Equal(18, restored.BoardSettings.DefaultContentFontSize);
        Assert.Equal(Color.Parse("#FF00AAFF"), restored.BoardSettings.DefaultContentColor);

        // 默认颜色为空 = 跟随主题，读回来还得是空，不能变成黑色。
        var following = JsonSerializer.Deserialize<MainConfigModel>(
            """{ "board_settings": { "default_content_color": null } }""", ConfigServiceBase.JsonOptions)!;
        Assert.Null(following.BoardSettings.DefaultContentColor);
    }

    [Fact]
    public void BoardSettings_PaletteRoundTripsAsHexArray()
    {
        var config = new MainConfigModel();
        config.BoardSettings.ApplyPaletteColors([Color.Parse("#FFD13438"), Color.Parse("#FF0078D4")]);

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);

        // ColorJsonConverter 在集合元素上同样生效：写出的是十六进制字符串数组，不是 {A,R,G,B} 对象。
        // 只断言「#D13438」这段，因为磁盘上的 alpha 顺序是收在尾部的（#RRGGBBAA），不要绑死完整写法。
        Assert.Contains("\"palette_colors\"", json);
        Assert.Contains("#D13438", json);
        Assert.Contains("#0078D4", json);

        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions)!;
        Assert.Equal([Color.Parse("#FFD13438"), Color.Parse("#FF0078D4")], restored.BoardSettings.ResolvePaletteColors());
    }

    [Fact]
    public void BoardSettings_MissingOrDirtyPaletteFallsBackToDefaults()
    {
        // 老版本 settings.json 里没有 palette_colors 这个字段：读回来是空集合，
        // ResolvePaletteColors 必须回退到默认色板，浮窗才不会变成一条空的。
        var legacy = JsonSerializer.Deserialize<MainConfigModel>(
            """{ "board_settings": { "board_name": "作业" } }""", ConfigServiceBase.JsonOptions)!;
        Assert.Empty(legacy.BoardSettings.PaletteColors);
        Assert.Equal(BoardPalette.DefaultColors, legacy.BoardSettings.ResolvePaletteColors());

        // 存了脏数据时要能收拾干净、不抛异常：全透明色丢掉，重复色只留一个，超过上限的截断。
        // 这里故意用 6 位十六进制——磁盘上 8 位是 #RRGGBBAA（alpha 在尾），6 位则与顺序无关，不会写错。
        var dirty = JsonSerializer.Deserialize<MainConfigModel>(
            """
            { "board_settings": { "palette_colors": ["#00000000", "#000000", "#FFFFFF", "#FF0000",
                                                     "#FF0000", "#00FF00", "#0000FF", "#FFFF00"] } }
            """, ConfigServiceBase.JsonOptions)!;
        Assert.Equal(8, dirty.BoardSettings.PaletteColors.Count);

        var cleaned = dirty.BoardSettings.ResolvePaletteColors();
        Assert.Equal(BoardPalette.MaxColors, cleaned.Count);
        Assert.DoesNotContain(cleaned, color => color.A == 0);
        Assert.Equal(cleaned.Count, cleaned.Distinct().Count());
        Assert.Equal([Color.Parse("#FF000000"), Color.Parse("#FFFFFFFF"), Color.Parse("#FFFF0000"),
            Color.Parse("#FF00FF00"), Color.Parse("#FF0000FF")], cleaned);
    }
}
