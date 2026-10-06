namespace Corkboard.Core.Icons;

/// <summary>
///     Fluent System Icons 字形表。
///     <para>
///         上游 SecRandom-C 用 <c>IconsMappingGenerator</c> 源生成器从
///         <c>Assets/FluentSystemIcons-Resizable.json</c> 生成这张表；Corkboard 骨架阶段先手写
///         当前用到的子集，接入生成器后这个文件应被生成产物取代（对外 API 形状保持不变）。
///     </para>
///     <para>
///         码位来自官方图标映射，格式为 <c>\uXXXX</c>，只有配合
///         <see cref="GlobalConstants.FluentIconsFontFamily" /> 才有意义。
///     </para>
/// </summary>
public static class FluentIcons
{
    public const string BoardFilled = "\uE20C";
    public const string HomeFilled = "\uE993";
    public const string SettingsFilled = "\uEF26";
    public const string InfoFilled = "\uE9E3";
    public const string AddFilled = "\uE00C";
    public const string DeleteFilled = "\uE61C";
    public const string PinFilled = "\uEDA0";
    public const string NoteFilled = "\uEBE9";
    public const string SaveFilled = "\uEEB4";
    public const string DocumentFilled = "\uE686";
    public const string ColorFilled = "\uE51D";
    public const string SyncFilled = "\uE160";
}
