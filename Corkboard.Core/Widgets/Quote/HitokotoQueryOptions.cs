namespace Corkboard.Core.Widgets.Quote;

/// <summary>
///     一次一言请求的选项：要哪几类、多长、端点在国内还是海外，外加用户可以手填的附加查询串。
///     <para>
///         做成不可变 record（改一项用 <c>with</c>），是因为同一份选项要同时被设置页的草稿、
///         实际发出去的请求和「恢复默认」按钮读到；可变对象在几处之间传一圈就说不清谁改了它。
///     </para>
/// </summary>
public sealed record HitokotoQueryOptions
{
    /// <summary>要哪几类；默认全部可用类别（服务端给什么都行）。</summary>
    public QuoteCategory Categories { get; init; } = QuoteCategory.All;

    /// <summary>最短字数（含）；0 表示不限 —— 服务端默认也是 0，所以 0 时不带这个参数。</summary>
    public int MinLength { get; init; }

    /// <summary>
    ///     最长字数（含）；默认 <see cref="HitokotoQuery.DefaultMaxLength" />（与服务端默认值一致）。
    ///     0 表示<b>不带这个参数</b>，此时长度由服务端自己的默认上限决定，不是「不限」。
    /// </summary>
    public int MaxLength { get; init; } = HitokotoQuery.DefaultMaxLength;

    /// <summary>
    ///     用户在设置页手填的附加查询串（<c>a=1&amp;b=2</c>，可带前导 <c>?</c>）。
    ///     里面的键可能是内建参数名，那种一律忽略，见 <see cref="HitokotoQuery.BuildQueryPairs" />。
    /// </summary>
    public string? ExtraQuery { get; init; }

    /// <summary>走海外端点（QPS 20）还是国内端点（QPS 2）；默认国内。</summary>
    public bool UseInternationalEndpoint { get; init; }

    /// <summary>
    ///     把界面上可能出现的脏值夹成服务端能接受的范围，返回<b>新对象</b>（手上这份不动）。
    ///     <para>
    ///         顺序是有讲究的：先把负数归零、再把 <see cref="MaxLength" /> 压到
    ///         <see cref="HitokotoQuery.MaxAllowedLength" />，最后才把 <see cref="MinLength" /> 拉回
    ///         <see cref="MaxLength" />。反过来做的话，「min=400、max=500」会先通过一致性检查、
    ///         再被压成 min=400 / max=100 这种仍然反着来的组合。
    ///     </para>
    ///     <para>
    ///         <see cref="MaxLength" /> 为 0 时不做一致性修正：0 的意思是「这个参数别带」，
    ///         拿它去压 <see cref="MinLength" /> 会把用户填的下限一起抹掉。
    ///     </para>
    /// </summary>
    /// <returns>夹取之后的选项。</returns>
    public HitokotoQueryOptions Normalize()
    {
        var min = Math.Max(0, MinLength);
        var max = Math.Clamp(MaxLength, 0, HitokotoQuery.MaxAllowedLength);

        if (max > 0 && min > max)
            min = max;

        return this with { MinLength = min, MaxLength = max };
    }
}
