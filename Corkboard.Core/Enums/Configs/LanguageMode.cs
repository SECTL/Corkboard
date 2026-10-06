namespace Corkboard.Core.Enums.Configs;

/// <summary>
///     界面语言。枚举值直接按数字落盘（见 <c>ConfigServiceBase.JsonOptions</c>），
///     只能在末尾增删成员：日语已移除，旧配置里的 2 由 <c>LanguageModeJsonConverter</c>
///     归一成 <see cref="ChineseSimplified" />，不会变成未定义值。
/// </summary>
public enum LanguageMode
{
    ChineseSimplified,
    English
}
