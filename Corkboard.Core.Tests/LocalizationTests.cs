using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Corkboard.Core.Langs.Common;
using Xunit;

namespace Corkboard.Core.Tests;

/// <summary>
/// 界面文案的中英两套必须一一对齐。
/// 中文本体是 <c>Resources.resx</c>（中性资源），英文是 <c>Resources.en-US.resx</c>（附属程序集）。
/// 漏一条的表现是「切到英文后那一行显示成中文」或者直接空白，编译期不报错，
/// 所以放在测试里兜住：任何一边多出来的键都算失败。
/// </summary>
public class LocalizationTests
{
    private static readonly CultureInfo English = new("en-US");

    private static Dictionary<string, string?> ReadResourceSet(CultureInfo culture)
    {
        var set = Resources.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in set)
        {
            if (entry.Key is string key)
            {
                result[key] = entry.Value as string;
            }
        }

        return result;
    }

    [Fact]
    public void ChineseAndEnglishResourceSetsHaveTheSameKeys()
    {
        var chinese = ReadResourceSet(CultureInfo.InvariantCulture);
        var english = ReadResourceSet(English);

        EnsureEnglishSatelliteLoaded(chinese, english);

        Assert.NotEmpty(chinese);

        var missingInEnglish = chinese.Keys.Where(key => !english.ContainsKey(key)).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var missingInChinese = english.Keys.Where(key => !chinese.ContainsKey(key)).OrderBy(key => key, StringComparer.Ordinal).ToArray();

        Assert.True(missingInEnglish.Length == 0, "英文缺这些键：" + string.Join(", ", missingInEnglish));
        Assert.True(missingInChinese.Length == 0, "中文缺这些键：" + string.Join(", ", missingInChinese));
    }

    [Fact]
    public void NoResourceValueIsBlank()
    {
        var chinese = ReadResourceSet(CultureInfo.InvariantCulture);
        var english = ReadResourceSet(English);

        EnsureEnglishSatelliteLoaded(chinese, english);

        AssertNoBlankValues(CultureInfo.InvariantCulture, chinese);
        AssertNoBlankValues(English, english);
    }

    /// <summary>
    ///     确认真的加载到了 en-US 附属程序集。
    /// </summary>
    /// <remarks>
    ///     附属程序集没随测试输出时，<see cref="ResourceManager" /> 会静默回退成中文，
    ///     于是「中英比对」退化成「中文和自己比」，永远通过。
    ///     这里刻意**不**用 <c>Assert.Skip</c>：跳过留下的绿色和真通过长得一模一样，
    ///     结果是英文文案缺失长期没人发现。环境没准备好就该红，让人把附属程序集补进输出目录。
    /// </remarks>
    private static void EnsureEnglishSatelliteLoaded(
        Dictionary<string, string?> chinese,
        Dictionary<string, string?> english)
    {
        // 挑一条中英必然不同的键当探针：两边一模一样说明加载到的其实是中性资源。
        const string probeKey = "Menu_OpenMainWindow";

        var loaded = english.TryGetValue(probeKey, out var en)
                     && chinese.TryGetValue(probeKey, out var zh)
                     && !string.Equals(en, zh, StringComparison.Ordinal);

        Assert.True(loaded,
            $"en-US 附属程序集没有随测试输出加载（探针 {probeKey} 的中英文本相同）。"
            + "请确认测试输出目录里存在 en-US/Corkboard.Core.resources.dll；"
            + "在补上之前，英文文案一条都没被校验过，不能当作通过。");
    }

    private static void AssertNoBlankValues(CultureInfo culture, Dictionary<string, string?> values)
    {
        var blanks = values.Where(pair => string.IsNullOrWhiteSpace(pair.Value))
                           .Select(pair => pair.Key)
                           .OrderBy(key => key, StringComparer.Ordinal)
                           .ToArray();
        Assert.True(blanks.Length == 0, $"{culture.Name} 里有空文案：" + string.Join(", ", blanks));
    }

    /// <summary>
    ///     <c>Resources.Designer.cs</c> 是手工维护的，两个方向都要钉住：
    ///     resx 有键而 Designer 缺属性 → 代码里 <c>CR.Xxx</c> 编译不过（通常会被人发现）；
    ///     Designer 有属性而 resx 没有键 → 一声不响，留下一个永远取不到文案的属性。
    /// </summary>
    [Fact]
    public void DesignerPropertiesMatchResourceKeys()
    {
        var resourceKeys = ReadResourceSet(CultureInfo.InvariantCulture).Keys.ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(resourceKeys);

        var designerText = File.ReadAllText(DesignerFile);
        var designerKeys = Regex.Matches(designerText, @"public static string (\w+) =>")
                                .Select(match => match.Groups[1].Value)
                                .ToHashSet(StringComparer.Ordinal);

        // 统计上报那一批刻意走 ResourceManager、不要属性（见 resx 里 Update_M_* 那段的注释）。
        static bool HasNoPropertyByDesign(string key) =>
            key.StartsWith("Update_M_", StringComparison.Ordinal)
            || key.StartsWith("Update_Notification_", StringComparison.Ordinal);

        var missingProperties = resourceKeys
                                .Where(key => !HasNoPropertyByDesign(key) && !designerKeys.Contains(key))
                                .OrderBy(key => key, StringComparer.Ordinal)
                                .ToArray();
        var orphanProperties = designerKeys
                               .Where(key => !resourceKeys.Contains(key))
                               .OrderBy(key => key, StringComparer.Ordinal)
                               .ToArray();

        Assert.True(missingProperties.Length == 0,
            "Resources.Designer.cs 缺这些属性：" + string.Join(", ", missingProperties));
        Assert.True(orphanProperties.Length == 0,
            "Resources.Designer.cs 有这些 resx 里已经不存在的属性：" + string.Join(", ", orphanProperties));
    }

    /// <summary>
    ///     测试跑在 <c>bin/&lt;配置&gt;/&lt;目标框架&gt;/</c> 下，往上找到仓库根（以
    ///     <c>Directory.Build.props</c> 为界）再拼相对路径，避免把绝对路径写进测试。
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                return directory.FullName;

            directory = directory.Parent;
        }

        Assert.Fail("找不到仓库根：任何祖先目录里都没有 Directory.Build.props");
        return string.Empty;
    }

    private static string DesignerFile =>
        Path.Combine(RepositoryRoot(), "Corkboard.Core", "Langs", "Common", "Resources.Designer.cs");

    /// <summary>
    /// 考试看板这一批键是后加的，单独钉一遍：只要有人把某个键从 resx 里删掉、
    /// 或者写成了英文没写中文，编译期发现不了（XAML 里是 x:Static，删了才会红），这里会先炸。
    /// </summary>
    [Theory]
    [InlineData("Exam_Board_DefaultTitle")]
    [InlineData("Exam_Board_ExitHint")]
    [InlineData("Exam_Board_NextExam")]
    [InlineData("Exam_Status_NotStarted")]
    [InlineData("Exam_Status_Upcoming")]
    [InlineData("Exam_Status_InProgress")]
    [InlineData("Exam_Status_Finished")]
    [InlineData("Exam_Board_Table_Date")]
    [InlineData("Exam_Board_Table_Subject")]
    [InlineData("Exam_Board_Table_Time")]
    [InlineData("Exam_Board_Table_Status")]
    [InlineData("Menu_OpenExamBoard")]
    [InlineData("Settings_Exam_Group")]
    [InlineData("Settings_Exam_Schedule_Title")]
    [InlineData("Settings_Exam_Schedule_InvalidSubject")]
    [InlineData("Settings_Exam_Schedule_InvalidTime")]
    [InlineData("Settings_Exam_Schedule_Overlap")]
    [InlineData("Settings_Exam_Display_Title")]
    [InlineData("Settings_Exam_Display_Threshold")]
    [InlineData("Settings_Exam_Display_ClockSide")]
    [InlineData("Settings_Exam_Display_HideListUpcoming")]
    [InlineData("Settings_Exam_Display_ShowListInProgress")]
    [InlineData("Settings_Exam_Time_Title")]
    [InlineData("Settings_Exam_Time_Servers")]
    [InlineData("Settings_Exam_Time_SyncNow")]
    [InlineData("Settings_Exam_Time_Status_Success")]
    [InlineData("Settings_Exam_Time_Offset_Fast")]
    [InlineData("Settings_Exam_Schedule_BoardAutoShow")]
    [InlineData("Settings_Exam_AutoShow_Title")]
    [InlineData("Settings_Exam_AutoShow_Status")]
    [InlineData("Settings_Exam_AutoShow_Preview")]
    [InlineData("Settings_Exam_AutoShow_NoBoard")]
    [InlineData("Settings_Exam_AutoShow_NoExam")]
    [InlineData("Settings_Exam_AutoShow_Next")]
    [InlineData("Settings_Exam_AutoShow_NextNow")]
    [InlineData("Settings_Exam_AutoShow_BoardSummary")]
    [InlineData("Settings_Exam_AutoShow_Lead")]
    [InlineData("Settings_Exam_AutoShow_AlsoShowAfterStart")]
    [InlineData("Settings_Exam_AutoShow_CloseDelay")]
    [InlineData("Settings_Exam_AutoShow_SwitchBoard")]
    [InlineData("Settings_Exam_Schedule_Export")]
    [InlineData("Settings_Exam_Schedule_Import")]
    [InlineData("Settings_Exam_Schedule_ImportToolTip")]
    [InlineData("Exam_File_SaveTitle")]
    [InlineData("Exam_File_FileType")]
    [InlineData("Exam_File_ExportFailed")]
    [InlineData("Exam_File_ImportFailed")]
    [InlineData("Exam_File_ImportEmpty")]
    [InlineData("Exam_File_ImportDone")]
    [InlineData("Settings_Exam_Schedule_ExportAll")]
    [InlineData("Settings_Exam_Schedule_ExportAllToolTip")]
    [InlineData("Settings_Exam_Schedule_ExportBoardToolTip")]
    [InlineData("Exam_File_ExportEmpty")]
    [InlineData("Exam_File_ExportDone")]
    public void ExamResourceKeysExistInBothLanguages(string key)
    {
        var chinese = ReadResourceSet(CultureInfo.InvariantCulture);
        Assert.True(chinese.TryGetValue(key, out var zh) && !string.IsNullOrWhiteSpace(zh), $"中文缺 {key}");

        var english = ReadResourceSet(English);
        // 附属程序集缺失时退回中文，上面那条用例已经会跳过，这里只要不空白即可。
        Assert.True(english.TryGetValue(key, out var en) && !string.IsNullOrWhiteSpace(en), $"英文缺 {key}");
    }
}
