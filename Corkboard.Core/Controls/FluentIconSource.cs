using FluentAvalonia.UI.Controls;

namespace Corkboard.Core.Controls;

/// <summary>
///     Fluent 图标源：把 <see cref="GlobalConstants.FluentIconsFontFamily" /> 与字形绑成一个可复用的
///     <see cref="FAFontIconSource" />，导航项、按钮、菜单都从这里构建图标，避免各处硬编码字体。
/// </summary>
public class FluentIconSource : FAFontIconSource
{
    public FluentIconSource()
    {
        FontFamily = GlobalConstants.FluentIconsFontFamily;
    }

    public FluentIconSource(string glyph) : this()
    {
        Glyph = glyph;
    }

    public FluentIconSource ProvideValue()
    {
        return this;
    }
}
