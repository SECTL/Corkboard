using System.Collections.ObjectModel;
using Corkboard.Core.Models.Board;

namespace Corkboard.Core.Abstraction.Services;

/// <summary>作业板领域服务：作业的增删改由方法走，类型与科目由调用方直接改集合再调 <see cref="Save" />。</summary>
public interface IBoardService
{
    /// <summary>按插入顺序（<see cref="BoardNote.Order" />）排列的作业，排序是 UI 层的事。</summary>
    ReadOnlyObservableCollection<BoardNote> Notes { get; }

    /// <summary>作业类型。设置页直接增删改，改完调 <see cref="Save" /> 落盘。</summary>
    ObservableCollection<BoardTypeDef> Types { get; }

    /// <summary>科目清单。同样是改完调 <see cref="Save" />。</summary>
    ObservableCollection<string> Subjects { get; }

    /// <summary>
    ///     手动排序下的科目区块先后。改它请走 <see cref="SetSubjectOrder" />，
    ///     这里只用于读取（分组、界面重建）。
    /// </summary>
    IReadOnlyList<string> SubjectOrder { get; }

    event EventHandler? Changed;

    BoardNote Add(BoardNote note);

    /// <summary>
    ///     按 <see cref="BoardNote.Id" /> 覆盖一条已有作业：只改类型、内容、科目与字段值，
    ///     创建时间与插入顺序保持不变（创建时间决定作业归档在哪个日期文件里），
    ///     修改时间刷成现在，然后落盘。作业不存在时返回 <c>false</c> 且不落盘。
    /// </summary>
    bool Update(BoardNote note);

    bool Remove(Guid id);

    /// <summary>按 Id 找作业类型；<paramref name="typeId" /> 为空或找不到时返回 <c>null</c>。</summary>
    BoardTypeDef? FindType(Guid? typeId);

    /// <summary>
    ///     记下手动排序的科目区块先后（调用方按界面上的先后给），顺带把已经从科目清单里
    ///     删掉的科目清出去，然后落盘并通知 <see cref="Changed" />。
    /// </summary>
    void SetSubjectOrder(IEnumerable<string> subjects);

    /// <summary>把当前内存状态整份写回 <c>board.json</c>，并通知 <see cref="Changed" />。</summary>
    void Save();

    void Reload();
}
