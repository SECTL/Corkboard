using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Corkboard.Core.Converters;

/// <summary>
///     把 <see cref="Color" /> 转成画刷，供 XAML 里当 <c>Background</c> / <c>Foreground</c> 用。
///     <para>
///         Avalonia 不会自动把 <c>Color</c> 转成 <c>IBrush</c>：把 <c>Color</c> 直接绑到
///         <c>Background</c> 上不会报错，但结果是空刷子（色块是透明的），很难查。
///         所以凡是「集合里存 Color、界面上画色块」的地方都要显式走这个转换器。
///     </para>
/// </summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    /// <summary>共享实例，XAML 里直接 <c>{x:Static}</c> 引用，不用每处 new 一个。</summary>
    public static ColorToBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Color color ? new SolidColorBrush(color) : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ISolidColorBrush brush ? brush.Color : null;
    }
}
