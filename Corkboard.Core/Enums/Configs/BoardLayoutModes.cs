namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     排布方式的读写约定。
///     <para>
///         <see cref="BoardLayoutMode.Flow" /> 已经取消（和小区块太像），但枚举成员必须留在原地
///         ——数字落盘，见 <see cref="BoardLayoutMode" /> 的说明——所以老配置里读出来的 2
///         要在这里统一折算成 <see cref="BoardLayoutMode.Block" />。界面只认两种排布，
///         配置里那份旧值保持原样，等用户下次在设置里选一次就干净了。
///     </para>
/// </summary>
public static class BoardLayoutModes
{
    /// <summary>把配置里的值折算成界面支持的排布方式：取消的旧值和越界值都有归宿。</summary>
    public static BoardLayoutMode Normalize(BoardLayoutMode mode) => mode switch
    {
        BoardLayoutMode.SingleColumn => BoardLayoutMode.SingleColumn,
        BoardLayoutMode.Block => BoardLayoutMode.Block,
        // 旧版本的「通铺」：和小区块合并了，按区块（自适应）显示。
        BoardLayoutMode.Flow => BoardLayoutMode.Block,
        // 手改脏了配置：退回默认的第一项。
        _ => BoardLayoutMode.SingleColumn
    };
}
