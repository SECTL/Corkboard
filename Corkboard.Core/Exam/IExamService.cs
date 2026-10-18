using System.Collections.ObjectModel;

namespace Corkboard.Core.Exam;

/// <summary>
///     考试安排的读写契约。与 <c>IBoardService</c> 同一套用法：
///     调用方直接改集合以外的事情走方法，改完由服务自己落盘并通知界面。
/// </summary>
public interface IExamService
{
    /// <summary>全部考试（未排序，按录入顺序）。看板与设置页要显示时一律过 <see cref="ExamStatusPolicy" /> 排序。</summary>
    ReadOnlyObservableCollection<ExamEntry> Exams { get; }

    /// <summary>数据变了（增删改、从磁盘重读）。界面据此重建列表。</summary>
    event EventHandler? Changed;

    /// <summary>加一场考试并落盘。返回落盘后的那条（Id 已确定）。</summary>
    ExamEntry Add(ExamEntry entry);

    /// <summary>按 <see cref="ExamEntry.Id" /> 覆盖一场考试。找不到返回 <see langword="false" />。</summary>
    bool Update(ExamEntry entry);

    /// <summary>按 Id 删一场考试。找不到返回 <see langword="false" />。</summary>
    bool Remove(Guid id);

    /// <summary>把内存里的安排写回磁盘（批量改动后调一次）。</summary>
    void Save();

    /// <summary>从磁盘重读（云同步恢复之后走这里）。</summary>
    void Reload();
}
