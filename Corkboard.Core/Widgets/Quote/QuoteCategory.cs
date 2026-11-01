namespace Corkboard.Core.Widgets.Quote;

/// <summary>
///     一言（Hitokoto）的类别：一位一个字母，a→l 与服务端 <c>c</c> 参数的取值一一对应。
///     <para>
///         位值会落盘（配置里存成数字），所以<b>已有位值不能改</b>，新类别只能往末尾追加；
///         官方哪天再加类别，接在 <see cref="Funny" /> 后面即可。
///     </para>
///     <para>
///         ⚠️ <see cref="NeteaseMusic" />（<c>j</c>）自 2022-11-18 起官方已停用。位留着有两个理由：
///         老配置里可能存着它、老句子的响应里也还可能出现 <c>"type":"j"</c>；
///         但请求一侧不再把它变成查询参数（见 <see cref="QuoteCategoryExtensions.ToCategoryChars" />），
///         <see cref="All" /> 也不含它 —— 带上它只会让服务端白跑一趟。
///     </para>
/// </summary>
[Flags]
public enum QuoteCategory
{
    /// <summary>不限类别（一个 <c>c</c> 参数都不带 = 服务端随便给）。</summary>
    None = 0,

    /// <summary>动画（a）。</summary>
    Animation = 1 << 0,

    /// <summary>漫画（b）。</summary>
    Comic = 1 << 1,

    /// <summary>游戏（c）。</summary>
    Game = 1 << 2,

    /// <summary>文学（d）。</summary>
    Literature = 1 << 3,

    /// <summary>原创（e）。</summary>
    Original = 1 << 4,

    /// <summary>来自网络（f）。</summary>
    Web = 1 << 5,

    /// <summary>其他（g）。</summary>
    Other = 1 << 6,

    /// <summary>影视（h）。</summary>
    Movie = 1 << 7,

    /// <summary>诗词（i）。</summary>
    Poetry = 1 << 8,

    /// <summary>网易云（j）—— <b>官方自 2022-11-18 停用</b>，只用于识别老数据，不再对外提供。</summary>
    NeteaseMusic = 1 << 9,

    /// <summary>哲学（k）。</summary>
    Philosophy = 1 << 10,

    /// <summary>抖机灵（l）。</summary>
    Funny = 1 << 11,

    /// <summary>全部<b>可用</b>的类别（不含已停用的 <see cref="NeteaseMusic" />），也是默认值。</summary>
    All = Animation | Comic | Game | Literature | Original | Web | Other | Movie | Poetry | Philosophy | Funny
}

/// <summary>
///     类别位与查询参数（单个小写字母）之间的换算，全项目只此一份 —— 纯函数，有单测。
///     <para>
///         单独放一个静态类是因为枚举本身带不了方法，而这段换算请求侧（拼 <c>c=</c>）与解析侧
///         （读响应里的 <c>type</c>）都要用；两边各写一张字母表，迟早有一天对不上。
///     </para>
/// </summary>
public static class QuoteCategoryExtensions
{
    /// <summary>
    ///     字母与位的对照表，顺序就是官方文档里 a→l 的顺序（设置页的勾选框也按这个排），
    ///     已停用的 <c>j</c> 也留在表里 —— 解析响应用得着它。
    /// </summary>
    private static readonly (char Letter, QuoteCategory Category)[] Ordered =
    [
        ('a', QuoteCategory.Animation),
        ('b', QuoteCategory.Comic),
        ('c', QuoteCategory.Game),
        ('d', QuoteCategory.Literature),
        ('e', QuoteCategory.Original),
        ('f', QuoteCategory.Web),
        ('g', QuoteCategory.Other),
        ('h', QuoteCategory.Movie),
        ('i', QuoteCategory.Poetry),
        ('j', QuoteCategory.NeteaseMusic),
        ('k', QuoteCategory.Philosophy),
        ('l', QuoteCategory.Funny)
    ];

    /// <summary>
    ///     把类别位翻成一串小写字母（拼 <c>c=a&amp;c=c</c> 用），顺序固定 a→l。
    ///     <para>
    ///         两位特殊情况：
    ///         <see cref="QuoteCategory.None" /> 与「只勾了已停用的 <see cref="QuoteCategory.NeteaseMusic" />」
    ///         都返回空表 —— 空表在请求侧的意思是「不限类别」，而不是「一类都不要」（后者做不到，也不合理）。
    ///         表里没有的脏位（老配置被手改过）直接忽略，不抛异常。
    ///     </para>
    /// </summary>
    /// <param name="categories">要翻译的类别位。</param>
    /// <returns>类别字母，按 a→l 排列；不限类别时为空表。</returns>
    public static IReadOnlyList<string> ToCategoryChars(QuoteCategory categories)
    {
        var letters = new List<string>(Ordered.Length);
        foreach (var (letter, category) in Ordered)
        {
            // 已停用的 j 不进请求：服务端不认，带上只是白跑。
            if (category == QuoteCategory.NeteaseMusic)
                continue;

            if ((categories & category) != 0)
                letters.Add(letter.ToString());
        }

        return letters;
    }

    /// <summary>
    ///     把响应里的 <c>type</c>（单个字母）翻成类别位；认不出来（大写手改过、多字符、未知字母）返回
    ///     <see cref="QuoteCategory.None" />，由调用方决定兜底成什么（解析器兜成 <see cref="QuoteCategory.Other" />）。
    ///     <para>
    ///         大小写不敏感：官方是小写，但中间隔了一层代理、或者有人手写测试数据时很可能是大写，
    ///         没必要为这个差别报错。
    ///     </para>
    /// </summary>
    /// <param name="letter">类别字母。</param>
    /// <returns>对应的类别位；认不出来时为 <see cref="QuoteCategory.None" />。</returns>
    public static QuoteCategory FromCategoryChar(char letter)
    {
        var normalized = char.ToLowerInvariant(letter);
        foreach (var (known, category) in Ordered)
        {
            if (known == normalized)
                return category;
        }

        return QuoteCategory.None;
    }
}
