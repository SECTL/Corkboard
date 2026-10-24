

namespace Corkboard.Core.Board;

/// <summary>
///     排布方式的读写约定。
///     <para>
///         <see cref="BoardLayoutMode.Flow" /> 已经取消（和小区块太像），但枚举成员必须留在原地
///         ——数字落盘，见 <see cref="BoardLayoutMode" /> 的说明——所以老配置里读出来的 2
///         要在这里统一折算成 <see cref="BoardLayoutMode.Block" />。界面只认两种排布，
///         配置里那份旧值保持原样，等用户下次在设置里选一次就干净了。
///     </para>
///     <para>
///         默认排布是 <see cref="BoardLayoutMode.Block" />（用户要求「排布默认需要区块」），
///         所以手改脏了的越界值也退回区块，而不是退回多列。
///     </para>
/// </summary>
public static class BoardLayoutModes
{
    /// <summary>界面上提供的第一种（也是默认的）排布方式。</summary>
    public const BoardLayoutMode Default = BoardLayoutMode.Block;

    /// <summary>把配置里的值折算成界面支持的排布方式：取消的旧值和越界值都有归宿。</summary>
    public static BoardLayoutMode Normalize(BoardLayoutMode mode) => mode switch
    {
        BoardLayoutMode.Columns => BoardLayoutMode.Columns,
        BoardLayoutMode.Block => BoardLayoutMode.Block,
        // 旧版本的「通铺」：和小区块合并了，按区块（自适应）显示。
        BoardLayoutMode.Flow => BoardLayoutMode.Block,
        // 手改脏了配置：退回默认的区块排布。
        _ => Default
    };
}
