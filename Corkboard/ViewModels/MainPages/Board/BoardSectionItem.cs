namespace Corkboard.ViewModels.MainPages.Board;

/// <summary>
///     同一科目的作业共用一个区块；区块内部再按二级分类分成若干小节。
///     <para>
///         <paramref name="Key" /> 是落盘用的原始科目（空串表示未分类），
///         <paramref name="Subject" /> 是显示名（未分类时是资源文案）——手动排序记的是前者。
///         <paramref name="CanReorder" /> 在构建时就定下来（回放期间为 <c>false</c>）：
///         列表项是重建出来的，回放状态一变整份列表就重建，所以不需要在模板里做相对源绑定。
///     </para>
///     <para>
///         <paramref name="Sections" /> 是区块内的二级分类小节：没分类的作业在最前面、
///         不带小标题；定义过、但没有作业的分类不会出现，也不留空组。
///     </para>
///     <para>
///         <paramref name="SubjectFontSize" /> 是科目名的字号（设置里的「科目标题字号」）。
///         字号跟着列表项走而不是在模板里绑页面 ViewModel：模板的绑定上下文是列表项，
///         而列表项本来就是每次重建的，设置一改就跟着变。
///     </para>
/// </summary>
public sealed record BoardSectionItem(
    string Key,
    string Subject,
    IReadOnlyList<BoardSubsectionItem> Sections,
    bool CanReorder,
    double SubjectFontSize);
