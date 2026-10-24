using System.Collections;
using System.Globalization;
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

        // 附属程序集没随测试输出时，英文这一份会静默回退成中文，比对就失去意义。
        // 挑一条中英必然不同的键确认真的加载到了 en-US，否则跳过而不是假装通过。
        if (english.TryGetValue("Menu_OpenMainWindow", out var probe) &&
            chinese.TryGetValue("Menu_OpenMainWindow", out var probeZh) &&
            string.Equals(probe, probeZh, StringComparison.Ordinal))
        {
            Assert.Skip("en-US 附属程序集没有随测试输出，无法比对英文文案");
        }

        Assert.NotEmpty(chinese);

        var missingInEnglish = chinese.Keys.Where(key => !english.ContainsKey(key)).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var missingInChinese = english.Keys.Where(key => !chinese.ContainsKey(key)).OrderBy(key => key, StringComparer.Ordinal).ToArray();

        Assert.True(missingInEnglish.Length == 0, "英文缺这些键：" + string.Join(", ", missingInEnglish));
        Assert.True(missingInChinese.Length == 0, "中文缺这些键：" + string.Join(", ", missingInChinese));
    }

    [Fact]
    public void NoResourceValueIsBlank()
    {
        foreach (var culture in new[] { CultureInfo.InvariantCulture, English })
        {
            var values = ReadResourceSet(culture);
            if (culture.Equals(English) &&
                values.TryGetValue("Menu_OpenMainWindow", out var probe) &&
                ReadResourceSet(CultureInfo.InvariantCulture).TryGetValue("Menu_OpenMainWindow", out var probeZh) &&
                string.Equals(probe, probeZh, StringComparison.Ordinal))
            {
                continue;
            }

            var blanks = values.Where(pair => string.IsNullOrWhiteSpace(pair.Value))
                               .Select(pair => pair.Key)
                               .OrderBy(key => key, StringComparer.Ordinal)
                               .ToArray();
            Assert.True(blanks.Length == 0, $"{culture.Name} 里有空文案：" + string.Join(", ", blanks));
        }
    }

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
    public void ExamResourceKeysExistInBothLanguages(string key)
    {
        var chinese = ReadResourceSet(CultureInfo.InvariantCulture);
        Assert.True(chinese.TryGetValue(key, out var zh) && !string.IsNullOrWhiteSpace(zh), $"中文缺 {key}");

        var english = ReadResourceSet(English);
        // 附属程序集缺失时退回中文，上面那条用例已经会跳过，这里只要不空白即可。
        Assert.True(english.TryGetValue(key, out var en) && !string.IsNullOrWhiteSpace(en), $"英文缺 {key}");
    }
}
