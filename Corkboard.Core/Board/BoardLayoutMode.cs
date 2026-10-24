

namespace Corkboard.Core.Board;

/// <summary>
///     作业板的排布方式。
///     <para>
///         枚举按数字落盘（见 <c>ConfigServiceBase.JsonOptions</c>，没有注册字符串枚举转换器），
///         所以只能在<b>末尾</b>增删成员，不要改动已有成员的数字。
///     </para>
///     <para>
///         界面上只提供「区块」和「多列」两种；<see cref="Flow" /> 已取消（和区块太像），
///         成员留着只是为了让老配置读得回来，读到时按 <see cref="BoardLayoutModes.Normalize" />
///         折算成 <see cref="Block" />。
///     </para>
/// </summary>
public enum BoardLayoutMode
{
    /// <summary>
    ///     多列：按窗口宽度并排几条窄阅读栏（每栏最宽见 <see cref="BoardColumnStyle.MaxColumnWidth" />），
    ///     窗口够宽就两栏、三栏，窄了自己收回一栏。
    ///     <para>
    ///         ⚠️ 数值 0 是老配置里「单列」那个值，**不能改**：老配置读出来就该是这个排布，
    ///         只是它现在会按窗口宽度自己分栏，不再是死板的一列。
    ///     </para>
    /// </summary>
    Columns = 0,

    /// <summary>区块：等宽区块从左排到右，满一行换行；区块宽度按可用宽度自适应。</summary>
    Block = 1,

    /// <summary>通铺（已取消）：与区块合并，见 <see cref="BoardLayoutModes.Normalize" />。</summary>
    Flow = 2
}
