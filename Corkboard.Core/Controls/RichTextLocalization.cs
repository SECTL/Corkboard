using System;
using System.Collections.Generic;
using AvaloniaRichEditor;

namespace Corkboard.Core.Controls;

/// <summary>
///     <c>AvaloniaRichEditor</c> 自带界面文案的语言表。
///     <para>
///         库只内置了英文与韩文两张表，而 <see cref="RichEditorLocalization.Language" /> 的默认值是 <c>"zh"</c>
///         ——没有这张表，于是中文界面下工具栏、右键菜单、颜色名全是英文。这里补一张简体中文表，
///         由 <see cref="Apply" /> 在界面语言切换时挂上去（注册是<b>合并</b>语义：只覆盖给出的键，
///         其余继续回退英文，所以以后升级库新增的键不会变成空字符串）。
///     </para>
///     <para>
///         我们只用库自带的工具栏（<see cref="RichEditorToolbar" />，常驻在编辑区上方，见
///         <c>BoardAssignmentForm</c>），不引它自带的整页视图；这张表覆盖库的全部键，
///         因为工具栏上的字号/字体/段落样式/对齐/颜色这些控件与它们的提示文字都走这里。
///     </para>
/// </summary>
public static class RichTextLocalization
{
    /// <summary>我们注册的简体中文语言键（库自带的 <c>"zh"</c> 是空表，不能用）。</summary>
    private const string ChineseLanguage = "zh-CN";

    /// <summary>英文的语言键：库内置，直接用它。</summary>
    private const string EnglishLanguage = "en";

    /// <summary>简体中文文案表：键取自库内置的英文表，逐条覆盖。</summary>
    private static readonly Dictionary<string, string> Chinese = new(StringComparer.Ordinal)
    {
        // 剪贴板与撤销
        ["Cut"] = "剪切",
        ["Copy"] = "复制",
        ["Paste"] = "粘贴",
        ["Delete"] = "删除",
        ["SelectAll"] = "全选",
        ["Undo"] = "撤销",
        ["Redo"] = "重做",

        // 字符格式
        ["CharacterFormat"] = "字体样式",
        ["ParagraphFormat"] = "段落",
        ["FontSizeIncrease"] = "增大字号",
        ["FontSizeDecrease"] = "减小字号",
        ["Bold"] = "加粗",
        ["Italic"] = "斜体",
        ["Underline"] = "下划线",
        ["Strikethrough"] = "删除线",
        ["FontSize"] = "字号",
        ["TextColor"] = "文字颜色",
        ["Highlight"] = "高亮",
        ["FontFamily"] = "字体",
        ["ClearFormatting"] = "清除格式",
        ["Apply"] = "应用",
        ["FormatPainter"] = "格式刷",
        ["FormatPainterTip"] = "格式刷（先选中源文字，点一下，再选目标）",

        // 预设颜色名
        ["ColorBlack"] = "黑色",
        ["ColorRed"] = "红色",
        ["ColorBlue"] = "蓝色",
        ["ColorGreen"] = "绿色",
        ["ColorGray"] = "灰色",
        ["HighlightYellow"] = "黄色",
        ["HighlightGreen"] = "浅绿",
        ["HighlightPink"] = "粉色",
        ["HighlightSkyBlue"] = "天蓝",
        ["HighlightNone"] = "无",
        ["NoHighlight"] = "不高亮",

        // 段落格式
        ["Paragraph"] = "段落",
        ["ParagraphStyle"] = "段落样式",
        ["Alignment"] = "对齐",
        ["AlignLeft"] = "左对齐",
        ["AlignCenter"] = "居中",
        ["AlignRight"] = "右对齐",
        ["AlignJustify"] = "两端对齐",
        ["List"] = "列表",
        ["BulletList"] = "无序列表",
        ["NumberedList"] = "有序列表",
        ["BulletStyle"] = "项目符号",
        ["NumberStyle"] = "编号样式",
        ["Heading"] = "标题",
        ["Heading1"] = "标题 1",
        ["Heading2"] = "标题 2",
        ["Heading3"] = "标题 3",
        ["Heading4"] = "标题 4",
        ["Heading5"] = "标题 5",
        ["Heading6"] = "标题 6",
        ["BodyText"] = "正文",
        ["Quote"] = "引用",
        ["Indent"] = "缩进",
        ["IndentIncrease"] = "增加缩进",
        ["IndentDecrease"] = "减少缩进",
        ["Margin"] = "页边距",
        ["MarginTop"] = "上边距",
        ["MarginBottom"] = "下边距",
        ["MarginLeft"] = "左边距",
        ["MarginRight"] = "右边距",
        ["LineSpacing"] = "行距",

        // 链接
        ["Hyperlink"] = "超链接",
        ["OpenLink"] = "打开链接",
        ["EditLink"] = "编辑链接",
        ["RemoveLink"] = "移除链接",
        ["InsertLink"] = "插入链接",
        ["CopyLink"] = "复制链接",

        // 插入
        ["InsertTable"] = "插入表格",
        ["InsertImage"] = "插入图片",
        ["InsertDivider"] = "插入分隔线",
        ["DragToSelectSize"] = "拖动选择大小",
        ["ImageSize"] = "大小",
        ["OriginalSize"] = "原始大小",
        ["HalfSize"] = "1/2 大小",
        ["ThirdSize"] = "1/3 大小",
        ["QuarterSize"] = "1/4 大小",
        ["ReplaceImage"] = "替换图片",
        ["SaveImageAs"] = "另存为",
        ["SelectImage"] = "选择图片",
        ["InlineWithText"] = "嵌入文字",
        ["SaveImage"] = "保存图片",

        // 表格
        ["SelectCell"] = "选择单元格",
        ["InsertRowAbove"] = "上方插入行",
        ["InsertRowBelow"] = "下方插入行",
        ["DeleteRow"] = "删除行",
        ["InsertColumnLeft"] = "左侧插入列",
        ["InsertColumnRight"] = "右侧插入列",
        ["DeleteColumn"] = "删除列",
        ["MergeCells"] = "合并单元格",
        ["UnmergeCells"] = "取消合并",
        ["DeleteTable"] = "删除表格",
        ["TableOps"] = "表格",

        // 查找替换
        ["OK"] = "确定",
        ["Cancel"] = "取消",
        ["Find"] = "查找",
        ["FindNext"] = "下一个",
        ["FindPrevious"] = "上一个",
        ["Replace"] = "替换",
        ["ReplaceAll"] = "全部替换",
        ["MatchCase"] = "区分大小写",
        ["NotFound"] = "未找到",
        ["ReplacedFormat"] = "已替换 {0} 处",

        // 状态栏与页面
        ["StatusFormat"] = "字符 {0}   词 {1}   行 {2}，列 {3}",
        ["Fit"] = "适应",
        ["ZoomTip"] = "视图缩放（Ctrl+滚轮，Ctrl+0 适应）",
        ["PaperContinuous"] = "连续",
        ["PaperTip"] = "纸张大小（连续 = 按宽度重排）",
        ["PageOutline"] = "大纲",
        ["OrientPortrait"] = "纵向",
        ["OrientLandscape"] = "横向",
        ["OrientationTip"] = "纸张方向",
        ["Export"] = "导出（JSON / .flow / HTML）",
        ["Import"] = "导入",
        ["Print"] = "打印",
        ["PageCountFormat"] = "{0} 页",
        ["ImageLimitWarning"] = "⚠ 已有 {0} 张图片，超过建议的 {1} 张（可能变慢）"
    };

    /// <summary>中文界面下挂上中文表，否则用库内置的英文表；重复调用是幂等的。</summary>
    public static void Apply(bool chinese)
    {
        RichEditorLocalization.Register(ChineseLanguage, Chinese);
        RichEditorLocalization.Language = chinese ? ChineseLanguage : EnglishLanguage;
    }
}
