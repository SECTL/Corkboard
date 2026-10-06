using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Corkboard.Core.Controls;

/// <summary>
///     「这块地方的手势归控件自己」的标记，挂在控件上，供壳层的整窗长按拖动查询。
///     <para>
///         整窗长按拖动的规则是「任意位置按住不动就拖窗口」，而作业板科目区块的标题行
///         是「按住就抓起来拖动排序」——只要按住不动，两者都会成立：
///         窗口跟着指针走、区块也在换位置。
///     </para>
///     <para>
///         所以这里给控件一个显式的退让开关：标了 <see cref="SuppressProperty" /> 的子树，
///         壳一律不参与长按拖动。这是一张**极短的例外表**（当前是作业板科目区块的标题行
///         与弹层卡片两个使用者），不是通用的控件排除表——普通按钮、输入框仍然靠
///         「移动超过阈值即让出」自然共存。
///     </para>
///     <para>
///         刻意做成 <see cref="AvaloniaObject" /> 的子类而不是静态类：C# 不允许静态类作泛型实参，
///         Avalonia 的附加属性注册要求宿主类型是可作泛型实参的类型（对照
///         <c>Avalonia.Controls.Chrome.WindowDecorationProperties</c>）。
///     </para>
/// </summary>
public class WindowDragGesture : AvaloniaObject
{
    /// <summary>置为 <c>true</c> 后，这棵子树里的按下不再触发壳的长按拖动。</summary>
    public static readonly AttachedProperty<bool> SuppressProperty =
        AvaloniaProperty.RegisterAttached<WindowDragGesture, Control, bool>("Suppress");

    public static void SetSuppress(Control element, bool value)
    {
        element.SetValue(SuppressProperty, value);
    }

    public static bool GetSuppress(Control element)
    {
        return element.GetValue(SuppressProperty);
    }

    /// <summary>
    ///     从命中元素往上走，看这棵树里有没有人声明「长按归我」。
    ///     壳的拖动处理器拿 <c>e.Source</c> 直接调它。
    /// </summary>
    public static bool IsSuppressed(object? source)
    {
        if (source is not Visual visual)
            return false;

        for (Visual? current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Control control && GetSuppress(control))
                return true;
        }

        return false;
    }
}
