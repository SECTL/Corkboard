using System.Globalization;

namespace Corkboard.Core.Models.Board;

/// <summary>
///     字段原始值文本的反向解析：把 <see cref="BoardNote.Values" /> 里存下的文本读回来，
///     供编辑作业时预填输入框。纯逻辑，有单测。
///     <para>
///         解析一律「宽进」：读不出来就当作没填，不抛异常——老数据、手改过的配置都可能出现怪值，
///         编辑表单不该因为一条脏数据打不开。
///     </para>
/// </summary>
public static class BoardFieldValueParser
{
    /// <summary>把原始文本读成一个数字；读不出来返回 <c>null</c>。</summary>
    public static decimal? ParseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    ///     把页数区间文本（<c>12-15</c>）拆成起页与止页；只填了一边时另一边是 <c>null</c>。
    ///     没有分隔符的文本按单独的起页处理。
    /// </summary>
    public static (decimal? From, decimal? To) SplitPageRange(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);

        var text = raw.Trim();
        var separator = text.IndexOf('-', StringComparison.Ordinal);
        if (separator < 0)
            return (ParseNumber(text), null);

        return (ParseNumber(text[..separator]), ParseNumber(text[(separator + 1)..]));
    }
}
