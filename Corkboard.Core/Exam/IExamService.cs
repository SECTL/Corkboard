using System.Collections.ObjectModel;

namespace Corkboard.Core.Exam;

/// <summary>
///     考试看板的读写契约。与 <c>IBoardService</c> 同一套用法：
///     调用方直接改集合以外的事情走方法，改完由服务自己落盘并通知界面。
///     <para>
///         数据分两层：<see cref="Boards" /> 是全部看板文件，<see cref="ActiveBoard" /> 是当前那一块；
///         下面那几个「加 / 改 / 删考试」的方法**都作用于当前看板**，界面切看板走 <see cref="SelectBoard" />。
///     </para>
/// </summary>
public interface IExamService
{
    /// <summary>全部看板（按创建先后）。每一块自带标题、副标题与考试安排。</summary>
    ReadOnlyObservableCollection<ExamBoardDocument> Boards { get; }

    /// <summary>当前看板。任何时候都有一个（第一块，或用户选的那块）。</summary>
    ExamBoardDocument ActiveBoard { get; }

    /// <summary>当前看板的考试（未排序，按录入顺序）。显示时一律过 <see cref="ExamStatusPolicy" /> 排序。</summary>
    ReadOnlyObservableCollection<ExamEntry> Exams { get; }

    /// <summary>数据变了（考试增删改、切看板、新建 / 删除看板、改标题、从磁盘重读）。界面据此重建。</summary>
    event EventHandler? Changed;

    /// <summary>往当前看板加一场考试并落盘。返回落盘后的那条（Id 已确定）。</summary>
    ExamEntry Add(ExamEntry entry);

    /// <summary>按 <see cref="ExamEntry.Id" /> 覆盖当前看板里的一场考试。找不到返回 <see langword="false" />。</summary>
    bool Update(ExamEntry entry);

    /// <summary>按 Id 删当前看板里的一场考试。找不到返回 <see langword="false" />。</summary>
    bool Remove(Guid id);

    /// <summary>把当前看板整份写回磁盘（改标题、副标题、副标题颜色之后调它）。</summary>
    void Save();

    /// <summary>从磁盘重读全部看板（云同步恢复之后走这里）。</summary>
    void Reload();

    /// <summary>
    ///     新建一块看板并切过去。<paramref name="title" /> 是初始标题（空则用默认名）。
    ///     新看板里没有考试。
    /// </summary>
    ExamBoardDocument CreateBoard(string? title = null);

    /// <summary>
    ///     删掉一块看板连同它的考试。**最后一块删不掉**（返回 <see langword="false" />）：
    ///     界面上永远要有一块可看的看板，否则「看板」这个概念就空了。
    /// </summary>
    bool DeleteBoard(Guid id);

    /// <summary>切换当前看板。找不到返回 <see langword="false" />。</summary>
    bool SelectBoard(Guid id);
}
