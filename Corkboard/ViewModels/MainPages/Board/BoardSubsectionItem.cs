namespace Corkboard.ViewModels.MainPages.Board;

/// <summary>
///     科目区块里的一个二级分类（小标题 + 它下面的作业）。
///     <para>
///         <paramref name="Title" /> 是显示文案；没归进任何分类的那批作业用资源里的「未分类」，
///         但 <paramref name="ShowTitle" /> 为 <c>false</c>——它们直接跟在一级标题下面，
///         不再画一条多余的「未分类」小标题。
///     </para>
///     <para>
///         <paramref name="TitleFontSize" /> 是分类标题的字号（设置里的「分类标题字号」）。
///         字号跟着列表项走而不是在模板里绑页面 ViewModel：模板的绑定上下文是列表项，
///         而列表项本来就是每次重建的，设置一改就跟着变。
///     </para>
/// </summary>
public sealed record BoardSubsectionItem(
    string Title,
    bool ShowTitle,
    IReadOnlyList<BoardNoteCardItem> Notes,
    double TitleFontSize);
