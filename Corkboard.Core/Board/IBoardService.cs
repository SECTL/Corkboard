using System.Collections.ObjectModel;
using Corkboard.Core.Homework;
using Corkboard.Core.Notes;

namespace Corkboard.Core.Board;

/// <summary>作业板领域服务：作业的增删改由方法走，科目由调用方直接改集合再调 <see cref="Save" />。</summary>
public interface IBoardService
{
    /// <summary>按插入顺序（<see cref="Note.Order" />）排列的作业，排序是 UI 层的事。</summary>
    ReadOnlyObservableCollection<Note> Notes { get; }

    /// <summary>科目清单。改完调 <see cref="Save" /> 落盘。</summary>
    ObservableCollection<string> Subjects { get; }

    /// <summary>
    ///     手动排序下的科目区块先后。改它请走 <see cref="SetSubjectOrder" />，
    ///     这里只用于读取（分组、界面重建）。
    /// </summary>
    IReadOnlyList<string> SubjectOrder { get; }

    /// <summary>全部二级分类定义（科目下的小标题），只用于读取。</summary>
    IReadOnlyList<HomeworkSection> Sections { get; }

    /// <summary>某个科目下的分类定义（按定义顺序）。</summary>
    IReadOnlyList<HomeworkSection> GetSections(string subject);

    /// <summary>
    ///     加一条二级分类（同科目下重名时返回已有的那条）。落盘并通知 <see cref="Changed" />；
    ///     科目或名字为空时返回 <c>null</c> 且不落盘。
    /// </summary>
    HomeworkSection? AddSection(string subject, string name);

    /// <summary>
    ///     重命名一条分类，并**把记着这个名字的作业一起改过来**（否则它们会显示成旧名字那一组）。
    ///     返回跟着改名的作业条数；分类不存在或新名字撞车时返回 0 且不落盘。
    /// </summary>
    int RenameSection(string subject, string oldName, string newName);

    /// <summary>
    ///     删掉一条分类定义，并把落在里面的作业退回科目的「未分类」（<see cref="Note.Section" /> 清空）——
    ///     删分类不该连作业一起删掉。返回被退回未分类的作业条数。
    /// </summary>
    int RemoveSection(string subject, string name);

    event EventHandler? Changed;

    Note Add(Note note);

    /// <summary>
    ///     按 <see cref="Note.Id" /> 覆盖一条已有作业：只改内容、科目与截止日期，
    ///     创建时间与插入顺序保持不变（创建时间决定作业归档在哪个日期文件里），
    ///     修改时间刷成现在，然后落盘。作业不存在时返回 <c>false</c> 且不落盘。
    /// </summary>
    bool Update(Note note);

    bool Remove(Guid id);

    /// <summary>
    ///     一次把多条标成「已清理」（自动清理走它）。比逐条改字段再 <see cref="Save" /> 好在
    ///     只落盘一次、界面也只重建一次。返回这次真正改了标记的条数（已经清过的不再算）；
    ///     一条都对不上时什么都不做，不落盘也不通知 <see cref="Changed" />。
    ///     <para>
    ///         作业**不从集合里拿掉**：清理只是让主页面不再显示它，数据仍旧写在原来那个日期文件里。
    ///     </para>
    /// </summary>
    int CleanMany(IEnumerable<Guid> ids, DateTimeOffset cleanedAt);

    /// <summary>
    ///     记下手动排序的科目区块先后（调用方按界面上的先后给），顺带把已经从科目清单里
    ///     删掉的科目清出去，然后落盘并通知 <see cref="Changed" />。
    /// </summary>
    void SetSubjectOrder(IEnumerable<string> subjects);

    /// <summary>
    ///     清空整块作业板：所有作业从集合里摘掉、落盘一次、通知一次。
    ///     <para>
    ///         科目与二级分类这些**定义**保留（用户清的是板子上的内容，不是自己的科目表）。
    ///         清之前该先备份（见 <c>BoardBackupService</c>），那是调用方的事。
    ///         </para>
    /// </summary>
    void ClearAll();

    /// <summary>把当前内存状态整份写回 <c>board.json</c>，并通知 <see cref="Changed" />。</summary>
    void Save();

    void Reload();
}
