using Corkboard.Core.Exam;
using Corkboard.Core.Exam.Layout;
using Corkboard.Core.Models.SubConfigs.Exam;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.ExamBoard;

/// <summary>
///     看板字号的**唯一一份表**：16 项字号各自的名字、简介、默认值与读写委托。
///     <para>
///         布局编辑窗选中某个组件后，右侧属性面板只列这个组件用到的那几项（<see cref="IdsFor" />）。
///         原来那张「看板字号」设置页已经下架（16 个滑块塞不进合并后的页），**这是现在唯一的入口**。
///     </para>
///     <para>
///         ⚠️ 改这里不用动任何 XAML：加一项字号只要在这张表里加一行、再补三个资源键。
///     </para>
/// </summary>
public static class ExamBoardFontSizeCatalog
{
    /// <summary>
    ///     这 16 项就是看板上全部的字（外加左下角那颗退出图标），顺序即设置页顺序。
    ///     <c>Id</c> 决定资源键与读写的配置属性，**不要**随手改（改了老配置里对应那项就回默认值）。
    /// </summary>
    public static IReadOnlyList<ExamFontSizeItem> Create(ExamBoardSettingsConfig settings) =>
    [
        // 顶部：主标题与副标题。
        new("Title", CR.Settings_Exam_Font_MainTitle, CR.Settings_Exam_Font_MainTitleDescription,
            ExamBoardTextStyle.DefaultTitleFontSize, false,
            () => settings.TitleFontSize, value => settings.TitleFontSize = value),

        new("Subtitle", CR.Settings_Exam_Font_Subtitle, CR.Settings_Exam_Font_SubtitleDescription,
            ExamBoardTextStyle.DefaultSubtitleFontSize, false,
            () => settings.SubtitleFontSize, value => settings.SubtitleFontSize = value),

        // 时钟那一组：两个档位，旁边有表 vs 只剩时钟。
        new("Clock", CR.Settings_Exam_Font_Clock, CR.Settings_Exam_Font_ClockDescription,
            ExamBoardTextStyle.DefaultClockFontSize, true,
            () => settings.ClockFontSize, value => settings.ClockFontSize = value),

        new("ClockAlone", CR.Settings_Exam_Font_ClockAlone, CR.Settings_Exam_Font_ClockAloneDescription,
            ExamBoardTextStyle.DefaultClockAloneFontSize, true,
            () => settings.ClockAloneFontSize, value => settings.ClockAloneFontSize = value),

        new("ClockDate", CR.Settings_Exam_Font_ClockDate, CR.Settings_Exam_Font_ClockDateDescription,
            ExamBoardTextStyle.DefaultClockDateFontSize, false,
            () => settings.ClockDateFontSize, value => settings.ClockDateFontSize = value),

        // 时钟下面那组考试信息。
        new("InfoCaption", CR.Settings_Exam_Font_InfoCaption, CR.Settings_Exam_Font_InfoCaptionDescription,
            ExamBoardTextStyle.DefaultInfoCaptionFontSize, false,
            () => settings.InfoCaptionFontSize, value => settings.InfoCaptionFontSize = value),

        new("InfoSubject", CR.Settings_Exam_Font_InfoSubject, CR.Settings_Exam_Font_InfoSubjectDescription,
            ExamBoardTextStyle.DefaultInfoSubjectFontSize, false,
            () => settings.InfoSubjectFontSize, value => settings.InfoSubjectFontSize = value),

        new("InfoTime", CR.Settings_Exam_Font_InfoTime, CR.Settings_Exam_Font_InfoTimeDescription,
            ExamBoardTextStyle.DefaultInfoTimeFontSize, false,
            () => settings.InfoTimeFontSize, value => settings.InfoTimeFontSize = value),

        new("InfoRemaining", CR.Settings_Exam_Font_InfoRemaining, CR.Settings_Exam_Font_InfoRemainingDescription,
            ExamBoardTextStyle.DefaultInfoRemainingFontSize, false,
            () => settings.InfoRemainingFontSize, value => settings.InfoRemainingFontSize = value),

        new("InfoPaper", CR.Settings_Exam_Font_InfoPaper, CR.Settings_Exam_Font_InfoPaperDescription,
            ExamBoardTextStyle.DefaultInfoPaperFontSize, false,
            () => settings.InfoPaperFontSize, value => settings.InfoPaperFontSize = value),

        // 右侧那张考试表。
        new("TableHeader", CR.Settings_Exam_Font_TableHeader, CR.Settings_Exam_Font_TableHeaderDescription,
            ExamBoardTextStyle.DefaultTableHeaderFontSize, false,
            () => settings.TableHeaderFontSize, value => settings.TableHeaderFontSize = value),

        new("Table", CR.Settings_Exam_Font_Table, CR.Settings_Exam_Font_TableDescription,
            ExamBoardTextStyle.DefaultTableFontSize, false,
            () => settings.TableFontSize, value => settings.TableFontSize = value),

        new("TableSubject", CR.Settings_Exam_Font_TableSubject, CR.Settings_Exam_Font_TableSubjectDescription,
            ExamBoardTextStyle.DefaultTableSubjectFontSize, false,
            () => settings.TableSubjectFontSize, value => settings.TableSubjectFontSize = value),

        // 一场都没排时的空状态。
        new("Empty", CR.Settings_Exam_Font_Empty, CR.Settings_Exam_Font_EmptyDescription,
            ExamBoardTextStyle.DefaultEmptyFontSize, false,
            () => settings.EmptyFontSize, value => settings.EmptyFontSize = value),

        new("EmptyHint", CR.Settings_Exam_Font_EmptyHint, CR.Settings_Exam_Font_EmptyHintDescription,
            ExamBoardTextStyle.DefaultEmptyHintFontSize, false,
            () => settings.EmptyHintFontSize, value => settings.EmptyHintFontSize = value),

        // 左下角长按退出那颗图标：它不是字，是字形，但同属「看板上这块东西多大」。
        new("ExitIcon", CR.Settings_Exam_Font_ExitIcon, CR.Settings_Exam_Font_ExitIconDescription,
            ExamBoardTextStyle.DefaultExitIconSize, false,
            () => settings.ExitIconSize, value => settings.ExitIconSize = value)
    ];

    /// <summary>某个组件用到的那几项字号（按表里的顺序）。</summary>
    public static IReadOnlyList<string> IdsFor(ExamBoardLayoutComponentId component) => component switch
    {
        ExamBoardLayoutComponentId.Title => ["Title", "Subtitle"],
        ExamBoardLayoutComponentId.Clock => ["Clock", "ClockAlone", "ClockDate"],
        ExamBoardLayoutComponentId.ExamInfo => ["InfoCaption", "InfoSubject", "InfoTime", "InfoRemaining", "InfoPaper"],
        ExamBoardLayoutComponentId.ExamTable => ["TableHeader", "Table", "TableSubject", "Empty", "EmptyHint"],
        ExamBoardLayoutComponentId.ExitCorner => ["ExitIcon"],
        _ => []
    };
}