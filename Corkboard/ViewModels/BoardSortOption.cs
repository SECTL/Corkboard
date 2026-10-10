using Corkboard.Core.Enums.Configs;

namespace Corkboard.ViewModels;

/// <summary>
///     标题栏排序菜单里的一项：落盘的枚举值 + 菜单上显示的名字。
///     <para>
///         排序原来在设置页的下拉框里（那时这个 record 在 <c>ViewModels/SettingsPages</c> 下），
///     现在入口只有主界面标题栏的排序按钮，所以搬到主界面这一层。
///     </para>
/// </summary>
/// <param name="Mode">写进配置的枚举值。</param>
/// <param name="DisplayName">显示名取自资源，界面语言变了会跟着变。</param>
public sealed record BoardSortOption(BoardSortMode Mode, string DisplayName);
