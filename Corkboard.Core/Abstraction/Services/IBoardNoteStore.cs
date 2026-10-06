using Corkboard.Core.Models.Board;

namespace Corkboard.Core.Abstraction.Services;

/// <summary>
///     作业的持久化契约。作业按**创建日期**分文件存放，服务层只管内存集合，
///     落盘位置与目录结构由实现决定。
/// </summary>
public interface IBoardNoteStore
{
    /// <summary>读出全部作业。归档目录里没有任何作业文件时返回空集合。</summary>
    IReadOnlyList<BoardNote> LoadAll();

    /// <summary>把内存里的作业整份写回磁盘，并清掉已经不该存在的旧文件。</summary>
    void SaveAll(IReadOnlyCollection<BoardNote> notes);

    /// <summary>
    ///     读旧版单文件（<c>data/config/board.json</c>，作业与类型、科目混在一起），供一次性搬迁。
    ///     没有旧文件或读不出来时返回 <c>null</c>。
    /// </summary>
    LegacyBoardFile? TryLoadLegacy();

    /// <summary>搬迁完成后删掉旧文件。删不掉不算失败：新数据已经写好，只是下次启动会再走一遍搬迁。</summary>
    void TryDeleteLegacy();
}
