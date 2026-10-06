namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     作业板的排布方式。
///     <para>
///         枚举按数字落盘（见 <c>ConfigServiceBase.JsonOptions</c>，没有注册字符串枚举转换器），
///         所以只能在<b>末尾</b>增删成员，不要改动已有成员的数字。
///     </para>
/// </summary>
public enum BoardLayoutMode
{
    /// <summary>单列：从左往下排，每张便签占满可用宽度，便签之间用分隔线。</summary>
    SingleColumn = 0,

    /// <summary>小区块：等宽等高的小格子，从左排到右，满一行换行。</summary>
    Block = 1,

    /// <summary>通铺：宽度由内容决定，从左排到右，满一行换行，单张有最大宽度上限。</summary>
    Flow = 2
}
