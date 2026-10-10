namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     作业板的排布方式。
///     <para>
///         枚举按数字落盘（见 <c>ConfigServiceBase.JsonOptions</c>，没有注册字符串枚举转换器），
///         所以只能在<b>末尾</b>增删成员，不要改动已有成员的数字。
///     </para>
///     <para>
///         界面上只提供「单列」和「区块」两种；<see cref="Flow" /> 已取消（和区块太像），
///         成员留着只是为了让老配置读得回来，读到时按 <see cref="BoardLayoutModes.Normalize" />
///         折算成 <see cref="Block" />。
///     </para>
/// </summary>
public enum BoardLayoutMode
{
    /// <summary>单列：从上往下排，每张便签占满可用宽度（页面会限宽居中，免得拉成一整条），便签之间用分隔线。</summary>
    SingleColumn = 0,

    /// <summary>区块：等宽区块从左排到右，满一行换行；区块宽度按可用宽度自适应。</summary>
    Block = 1,

    /// <summary>通铺（已取消）：与区块合并，见 <see cref="BoardLayoutModes.Normalize" />。</summary>
    Flow = 2
}
