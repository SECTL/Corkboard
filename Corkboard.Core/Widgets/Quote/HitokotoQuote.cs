namespace Corkboard.Core.Widgets.Quote;

/// <summary>
///     一句话（一言）：正文、出处、作者、类别，外加可以拿来去重的 <c>uuid</c> 与字数。
///     <para>
///         只带界面上真要显示的东西：<c>creator</c> / <c>reviewer</c> / <c>commit_from</c> / <c>created_at</c>
///         那些字段是运营信息，小组件上贴不出来，留在 JSON 里就好（<see cref="HitokotoQuoteParser" />
///         不把它们搬进来，也就不会有人误以为改它们有用）。
///     </para>
///     <para>
///         <see cref="Source" /> 与 <see cref="Author" /> 都可能没有（服务端给 <c>null</c>），
///         界面据此决定那一行显示不显示 —— 用 <see langword="null" /> 而不是空串表达「没有」，
///         空串会让「有出处但是空的」与「没有出处」两件事混在一起。
///     </para>
/// </summary>
/// <param name="Text">正文。</param>
/// <param name="Source">出处（响应里的 <c>from</c>），可能没有。</param>
/// <param name="Author">作者（响应里的 <c>from_who</c>），可能没有。</param>
/// <param name="Category">类别；认不出来的按 <see cref="QuoteCategory.Other" /> 记。</param>
/// <param name="Uuid">服务端的句子标识，用来判断「又抽到同一句」。</param>
/// <param name="Length">字数（服务端给的是含标点的字符数）。</param>
public sealed record HitokotoQuote(
    string Text,
    string? Source,
    string? Author,
    QuoteCategory Category,
    string? Uuid,
    int Length)
{
    /// <summary>
    ///     「还没拿到」的那一条：界面先绑它，比到处判 <see langword="null" /> 省事，
    ///     也不用给每个绑定都写 <c>?</c> 兜底。
    /// </summary>
    public static HitokotoQuote Empty { get; } = new(string.Empty, null, null, QuoteCategory.None, null, 0);
}
