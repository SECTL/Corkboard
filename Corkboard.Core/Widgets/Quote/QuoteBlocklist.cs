using System;
using System.Collections.Generic;

namespace Corkboard.Core.Widgets.Quote;

/// <summary>
///     一言的屏蔽词：一段「每行一个词」的文本，命中正文 / 出处 / 作者的句子直接不要。
///     <para>
///         为什么是客户端过滤而不是请求参数：官方 v1 没有任何「排除关键词」的参数，
///         能调的只有类别与字数。屏蔽词因此只能在拿到句子之后自己判一遍，
///         判掉了再要一条（重取次数由 <c>QuoteService</c> 限住，别在这里循环请求）。
///     </para>
///     <para>
///         纯逻辑、不碰网络也不碰配置，单测在 <c>Corkboard.Core.Tests/QuoteBlocklistTests.cs</c>。
///         匹配是<b>不区分大小写的包含</b>：中文没有大小写，英文词（比如 spoiler）这样才拦得住。
///     </para>
/// </summary>
public static class QuoteBlocklist
{
    /// <summary>屏蔽词条数上限：再多也不是「屏蔽几个词」，而是把整个词表抄进来了。</summary>
    public const int MaxWords = 200;

    /// <summary>
    ///     把配置里那段文本拆成词表：按行切、去空白、丢掉空行、忽略大小写去重，
    ///     顺序保持用户写的顺序（设置页的「现在屏蔽 N 个词」就按这个数）。
    /// </summary>
    /// <param name="raw">原始文本（可能为 <see langword="null" />，也可能用 <c>\r\n</c> 换行）。</param>
    public static IReadOnlyList<string> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        var words = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in raw.Split(['\r', '\n'], StringSplitOptions.TrimEntries))
        {
            if (line.Length == 0)
                continue;

            if (!seen.Add(line))
                continue;

            words.Add(line);
            if (words.Count >= MaxWords)
                break;
        }

        return words;
    }

    /// <summary>
    ///     把文本收拾成落盘的样子（一行一个词、去掉空行与重复），读盘后与提交前都走它。
    /// </summary>
    public static string Normalize(string? raw) => string.Join('\n', Parse(raw));

    /// <summary>
    ///     这条句子有没有踩到屏蔽词：正文、出处、作者三处任意一处包含就算命中。
    /// </summary>
    /// <param name="quote">拿到的句子。</param>
    /// <param name="words">已经拆好的词表（空表表示不屏蔽任何东西）。</param>
    public static bool IsBlocked(HitokotoQuote quote, IReadOnlyList<string> words)
    {
        if (words.Count == 0)
            return false;

        foreach (var word in words)
        {
            if (Contains(quote.Text, word))
                return true;

            if (Contains(quote.Source, word))
                return true;

            if (Contains(quote.Author, word))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     同上，但直接吃配置里那段原始文本（内部现拆词表）。
    ///     <para>界面线程以外的地方不要用它——每次都重新拆一遍，取一言那一路自己拿着词表复用。</para>
    /// </summary>
    public static bool IsBlocked(HitokotoQuote quote, string? raw) => IsBlocked(quote, Parse(raw));

    private static bool Contains(string? text, string word) =>
        !string.IsNullOrEmpty(text) && text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
