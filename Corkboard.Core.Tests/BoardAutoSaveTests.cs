using System.Text.Json;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Board.AutoSave;
using Corkboard.Core.Models.SubConfigs.Board;

namespace Corkboard.Core.Tests;

/// <summary>
///     <see cref="BoardAutoSavePolicy" /> 与 <see cref="BoardAutoSaveSettingsConfig" /> 的单测：
///     文件怎么命名、哪些文件该删、认不出来的文件一律不碰、越界配置怎么夹。
///     <para>
///         这里全是纯逻辑（<c>today</c> 从参数进来），不碰磁盘也不看本机时钟。
///     </para>
/// </summary>
public class BoardAutoSaveTests
{
    #region 命名

    /// <summary>「每天一张」只带日期：同一天反复保存写的是同一个文件，天然覆盖。</summary>
    [Fact]
    public void BuildFileName_Daily_CarriesOnlyTheDate()
    {
        var name = BoardAutoSavePolicy.BuildFileName(
            new DateOnly(2026, 10, 24),
            BoardAutoSaveMode.Daily,
            new DateTime(2026, 10, 24, 15, 30, 45));

        // 给了 now 也不带时分秒——带了就不会覆盖了。
        Assert.Equal("board-2026-10-24.png", name);
    }

    /// <summary>「每次修改都存一张」带时分秒：每次都是新文件。</summary>
    [Fact]
    public void BuildFileName_EveryChange_CarriesTheTimestamp()
    {
        var name = BoardAutoSavePolicy.BuildFileName(
            new DateOnly(2026, 10, 24),
            BoardAutoSaveMode.EveryChange,
            new DateTime(2026, 10, 24, 15, 30, 45));

        Assert.Equal("board-2026-10-24_15-30-45.png", name);
    }

    /// <summary>两种命名都能解析回那一天。</summary>
    [Theory]
    [InlineData("board-2026-10-24.png", 2026, 10, 24)]
    [InlineData("board-2026-10-24_15-30-45.png", 2026, 10, 24)]
    [InlineData("BOARD-2026-10-24.PNG", 2026, 10, 24)]
    public void TryParseDate_ReadsBothShapes(string fileName, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), BoardAutoSavePolicy.TryParseDate(fileName));
    }

    /// <summary>
    ///     不是我们命名的文件一律认不出来——用户往这个目录里丢的照片不能被当成存档删掉。
    /// </summary>
    [Theory]
    [InlineData("board.png")]
    [InlineData("board-2026-10-24.jpg")]
    [InlineData("board-2026-13-01.png")]
    [InlineData("board-2026-02-30.png")]
    [InlineData("我的作业板.png")]
    [InlineData("2026-10-24.png")]
    [InlineData("notes.json")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseDate_RejectsForeignNames(string? fileName)
    {
        Assert.Null(BoardAutoSavePolicy.TryParseDate(fileName));
    }

    /// <summary>只给文件名也能认（调用方有时拿到的是整条路径）。</summary>
    [Fact]
    public void TryParseDate_AcceptsFullPath()
    {
        var path = Path.Combine("C:", "Users", "someone", "autosave", "board-2026-10-24.png");
        Assert.Equal(new DateOnly(2026, 10, 24), BoardAutoSavePolicy.TryParseDate(path));
    }

    #endregion

    #region 目录

    /// <summary>没配目录时回退到软件自己的 <c>data/autosave</c>。</summary>
    [Fact]
    public void ResolveFolder_FallsBackToDefault()
    {
        var defaultFolder = BoardAutoSavePolicy.ResolveDefaultFolder();

        Assert.Equal(defaultFolder, BoardAutoSavePolicy.ResolveFolder(null));
        Assert.Equal(defaultFolder, BoardAutoSavePolicy.ResolveFolder("   "));
        Assert.Equal(defaultFolder, BoardAutoSavePolicy.ResolveFolder("\"\""));
    }

    /// <summary>配了目录就用配的那个，并且去空白、去引号、展开成绝对路径。</summary>
    [Fact]
    public void ResolveFolder_UsesConfiguredFolder()
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "corkboard-autosave"));

        Assert.Equal(expected, BoardAutoSavePolicy.ResolveFolder(
            $"  \"{Path.Combine(Path.GetTempPath(), "corkboard-autosave")}\"  "));
    }

    /// <summary>默认目录在软件数据根底下（用户要求「默认的文件夹在软件自己底下」）。</summary>
    [Fact]
    public void ResolveDefaultFolder_IsInsideTheAppDataRoot()
    {
        var defaultFolder = BoardAutoSavePolicy.ResolveDefaultFolder();

        Assert.StartsWith(Corkboard.Shared.Utils.DataRoot, defaultFolder, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(BoardAutoSavePolicy.DefaultFolderName, defaultFolder, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 保留天数

    /// <summary>越界值一律夹回合法区间。</summary>
    [Theory]
    [InlineData(-5, BoardAutoSavePolicy.MinRetentionDays)]
    [InlineData(0, BoardAutoSavePolicy.MinRetentionDays)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(1000, BoardAutoSavePolicy.MaxRetentionDays)]
    public void ClampRetentionDays_KeepsValueInRange(int input, int expected)
    {
        Assert.Equal(expected, BoardAutoSavePolicy.ClampRetentionDays(input));
    }

    #endregion

    #region 裁剪

    /// <summary>
    ///     保留 N 天 = 今天往前数 N 天（含今天）：N=3、今天 10-24 时，10-22 及更早的都删。
    /// </summary>
    [Fact]
    public void SelectExpired_DeletesFilesOlderThanTheWindow()
    {
        var today = new DateOnly(2026, 10, 24);
        var files = new[]
        {
            File(new DateOnly(2026, 10, 24)),
            File(new DateOnly(2026, 10, 23)),
            File(new DateOnly(2026, 10, 22)),
            File(new DateOnly(2026, 10, 21)),
            File(new DateOnly(2026, 9, 1))
        };

        var expired = BoardAutoSavePolicy.SelectExpired(files, today, 3, BoardAutoSaveMode.Daily);

        Assert.Equal(
            ["board-2026-09-01.png", "board-2026-10-21.png"],
            expired.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>保留 1 天 = 只留今天那张。</summary>
    [Fact]
    public void SelectExpired_RetentionOne_KeepsOnlyToday()
    {
        var today = new DateOnly(2026, 10, 24);
        var files = new[]
        {
            File(new DateOnly(2026, 10, 24)),
            File(new DateOnly(2026, 10, 23))
        };

        var expired = BoardAutoSavePolicy.SelectExpired(files, today, 1, BoardAutoSaveMode.Daily);

        Assert.Equal(["board-2026-10-23.png"], expired);
    }

    /// <summary>
    ///     「每天一张」模式下同一天只留最后一份：从「每次修改都存」切过来时，那天可能已经攒了一堆，
    ///     切成每天一张后必须收敛回一份，否则「每天只保留最后一份」就是句空话。
    /// </summary>
    [Fact]
    public void SelectExpired_DailyMode_CollapsesSameDayToTheNewest()
    {
        var today = new DateOnly(2026, 10, 24);
        var day = new DateOnly(2026, 10, 24);
        var files = new[]
        {
            new BoardAutoSaveFile("board-2026-10-24_09-00-00.png", day, new DateTime(2026, 10, 24, 1, 0, 0, DateTimeKind.Utc)),
            new BoardAutoSaveFile("board-2026-10-24_18-00-00.png", day, new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc)),
            new BoardAutoSaveFile("board-2026-10-24_12-00-00.png", day, new DateTime(2026, 10, 24, 4, 0, 0, DateTimeKind.Utc))
        };

        var expired = BoardAutoSavePolicy.SelectExpired(files, today, 7, BoardAutoSaveMode.Daily);

        // 留写入时间最晚的那张（18-00-00），另外两张删掉。
        Assert.Equal(2, expired.Count);
        Assert.DoesNotContain("board-2026-10-24_18-00-00.png", expired);
        Assert.Contains("board-2026-10-24_09-00-00.png", expired);
        Assert.Contains("board-2026-10-24_12-00-00.png", expired);
    }

    /// <summary>「每次修改都存」模式下同一天的多张都留着（那正是这个模式的意义）。</summary>
    [Fact]
    public void SelectExpired_EveryChangeMode_KeepsSameDayFiles()
    {
        var today = new DateOnly(2026, 10, 24);
        var day = new DateOnly(2026, 10, 24);
        var files = new[]
        {
            new BoardAutoSaveFile("board-2026-10-24_09-00-00.png", day, new DateTime(2026, 10, 24, 1, 0, 0, DateTimeKind.Utc)),
            new BoardAutoSaveFile("board-2026-10-24_18-00-00.png", day, new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc))
        };

        var expired = BoardAutoSavePolicy.SelectExpired(files, today, 7, BoardAutoSaveMode.EveryChange);

        Assert.Empty(expired);
    }

    /// <summary>
    ///     同一天的「只带日期」与「带时分秒」两份文件写入时间打平时，留「只带日期」那份——
    ///     它才是「每天一张」模式下刚写出来的正主；留错的话刚存的图会被当成旧的删掉。
    /// </summary>
    [Fact]
    public void SelectExpired_DailyMode_PrefersTheDateOnlyNameOnTies()
    {
        var day = new DateOnly(2026, 10, 24);
        var sameTime = new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
        var files = new[]
        {
            new BoardAutoSaveFile("board-2026-10-24.png", day, sameTime),
            new BoardAutoSaveFile("board-2026-10-24_18-00-00.png", day, sameTime)
        };

        var expired = BoardAutoSavePolicy.SelectExpired(files, day, 7, BoardAutoSaveMode.Daily);

        Assert.Equal(["board-2026-10-24_18-00-00.png"], expired);
    }

    /// <summary>空目录不该炸。</summary>
    [Fact]
    public void SelectExpired_HandlesEmptyInput()
    {
        var expired = BoardAutoSavePolicy.SelectExpired([], new DateOnly(2026, 10, 24), 7,
            BoardAutoSaveMode.EveryChange);

        Assert.Empty(expired);
    }

    #endregion

    #region 占用统计

    /// <summary>只算我们自己的文件，用户丢进来的图片不计入。</summary>
    [Fact]
    public void TotalBytes_CountsOnlyOurFiles()
    {
        var total = BoardAutoSavePolicy.TotalBytes(
        [
            ("board-2026-10-24.png", 1024L),
            ("board-2026-10-23_09-00-00.png", 2048L),
            ("我的截图.png", 999_999L),
            ("notes.json", 512L),
            ("board-2026-10-22.png", 0L)
        ]);

        Assert.Equal(3072, total);
    }

    #endregion

    #region 配置

    /// <summary>默认值：关着、每天一张、默认目录（空串）、保留 7 天。</summary>
    [Fact]
    public void Config_DefaultsToOffAndDaily()
    {
        var config = new BoardAutoSaveSettingsConfig();

        Assert.False(config.Enabled);
        Assert.Equal(BoardAutoSaveMode.Daily, config.Mode);
        Assert.Equal(string.Empty, config.Folder);
        Assert.Equal(BoardAutoSavePolicy.DefaultRetentionDays, config.RetentionDays);
        Assert.False(config.HasCustomFolder);
        Assert.Equal(BoardAutoSavePolicy.ResolveDefaultFolder(), config.ResolveFolder());
    }

    /// <summary>读盘后越界的枚举与保留天数都夹回来。</summary>
    [Fact]
    public void Config_Normalize_FixesOutOfRangeValues()
    {
        var config = new BoardAutoSaveSettingsConfig
        {
            Mode = (BoardAutoSaveMode)99,
            RetentionDays = 0
        };

        config.Normalize();

        Assert.Equal(BoardAutoSaveMode.Daily, config.Mode);
        Assert.Equal(BoardAutoSavePolicy.MinRetentionDays, config.RetentionDays);
    }

    /// <summary>自己指定过目录时 <c>HasCustomFolder</c> 为真（界面据此显示「恢复默认」）。</summary>
    [Fact]
    public void Config_HasCustomFolder_TracksTheFolderSetting()
    {
        var config = new BoardAutoSaveSettingsConfig { Folder = @"D:\作业板存档" };
        Assert.True(config.HasCustomFolder);

        config.Folder = "   ";
        Assert.False(config.HasCustomFolder);
    }

    /// <summary>
    ///     <c>HasCustomFolder</c> 是纯推导值，**不落盘**：写进 settings.json 只会多一个改不动、
    ///     还可能跟 <c>folder</c> 对不上的字段。
    /// </summary>
    [Fact]
    public void Config_DoesNotPersistTheDerivedFolderFlag()
    {
        var config = new BoardAutoSaveSettingsConfig { Folder = @"D:\作业板存档" };

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);

        Assert.DoesNotContain("has_custom_folder", json);
        Assert.Contains("\"folder\": \"D:\\\\作业板存档\"", json);
    }

    #endregion

    /// <summary>造一条「每天一张」的测试文件。</summary>
    private static BoardAutoSaveFile File(DateOnly date) =>
        new(BoardAutoSavePolicy.BuildFileName(date, BoardAutoSaveMode.Daily), date,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
}
