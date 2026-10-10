namespace Corkboard.Core.Models.Board;

/// <summary>
///     作业内容（<see cref="BoardNote.Content" />）是什么格式。
///     <para>
///         枚举按数字落盘：<see cref="Markdown" /> 必须是 0——旧数据的 JSON 里没有这一项，
///         读出来就是它，于是「旧数据」与「新数据」不需要额外的版本字段或搬迁标记。
///         <see cref="LegacyBoardContentConverter" /> 在加载时把前者一次性转成后者。
///     </para>
/// </summary>
public enum BoardContentKind
{
    /// <summary>遗留格式：Markdown 原文，颜色/字号另记在按原文偏移的标注里。</summary>
    Markdown = 0,

    /// <summary>富文本文档的 HTML 片段：本地写的样式（颜色、字号）就在片段里。</summary>
    Html = 1
}
