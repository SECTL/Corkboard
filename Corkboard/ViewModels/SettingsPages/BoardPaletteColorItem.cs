using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Corkboard.ViewModels.SettingsPages;

/// <summary>
///     设置页色板里的一行：一个预设色。
///     <para>
///         为什么要有这一层：色板在配置里是 <c>ObservableCollection&lt;Color&gt;</c>，
///         而 <c>Color</c> 是结构体——取色器改它不会发出任何通知，双向绑也写不回集合元素。
///         包一层之后取色器绑的是这个对象的 <see cref="Color" />，
///         改完由它把值转交给页面 ViewModel 按位替换（见
///         <see cref="BoardSettingsPageViewModel.SchedulePaletteColorCommit" />）。
///     </para>
///     <para>
///         <see cref="CanMoveUp" /> / <see cref="CanMoveDown" /> / <see cref="CanRemove" /> 由
///         ViewModel 在每次增删挪之后统一刷新（见 <c>RefreshPaletteItemStates</c>）：
///         第一个不能往前挪、最后一个不能往后挪、只剩一个时不能删。
///         宁可把按钮禁掉，也不要留一个按下去没反应的按钮。
///     </para>
/// </summary>
public partial class BoardPaletteColorItem : ObservableObject
{
    private readonly Action<BoardPaletteColorItem, Color> _onColorChanged;

    public BoardPaletteColorItem(Color color, Action<BoardPaletteColorItem, Color> onColorChanged)
    {
        _color = color;
        _onColorChanged = onColorChanged;
    }

    /// <summary>这个色块当前的颜色。取色器拖一下就变一次，所以提交要防抖。</summary>
    [ObservableProperty] private Color _color;

    /// <summary>还能往前挪（不是第一个）。</summary>
    [ObservableProperty] private bool _canMoveUp;

    /// <summary>还能往后挪（不是最后一个）。</summary>
    [ObservableProperty] private bool _canMoveDown;

    /// <summary>
    ///     能不能删。只剩一个时不能删：色板清空会触发「回退默认色板」，
    ///     用户会看到自己刚删掉的颜色又全冒出来。
    /// </summary>
    [ObservableProperty] private bool _canRemove;

    partial void OnColorChanged(Color value)
    {
        _onColorChanged(this, value);
    }
}
