using Avalonia.Controls.Primitives;

namespace Corkboard.Core.Controls;

/// <summary>
///     开发构建装饰层：Debug 构建时贴在壳的 <c>AdornerLayer</c> 上，画左下角那行版本水印
///     （模板见 <c>Controls/DevelopmentBuildAdorner.axaml</c>）。
///     <para>
///         只是个空壳 <see cref="TemplatedControl" />：外观与文案全在模板里，所以壳侧只管
///         「<see cref="GlobalConstants.IsDevelopment" /> 为真就往 <c>AdornerLayer</c> 里塞一个」，
///         Release 构建不注册、也不会有任何额外分支。
///     </para>
/// </summary>
public class DevelopmentBuildAdorner : TemplatedControl
{
    /// <inheritdoc />
    public DevelopmentBuildAdorner()
    {
        // 装饰层铺满被装饰的壳，本身不参与布局尺寸计算，越界内容也不该被裁掉。
        ClipToBounds = false;
    }
}
