using Avalonia.Controls;
using Avalonia.Media;
using Corkboard.Core.Exam.Theme;

namespace Corkboard.Views.Common;

/// <summary>
///     把主题解析出来的 13 个刷子写进窗口/预览自己的资源字典。
///     <para>
///         键名 = <c>ExamBoard + token 的 Pascal 名 + Brush</c>（如 <c>ExamBoardTextPrimaryBrush</c>）。
///         必须用<b>索引器替换条目</b>：Avalonia 12.1.1 实测这样才会让 <c>{DynamicResource}</c>
///         （包括 <c>Window.Styles</c> 里的 Setter）重新解析；原地改 <c>SolidColorBrush.Color</c> 不会。
///     </para>
/// </summary>
public static class ExamBoardThemeResources
{
    /// <summary>建一本只有 13 个占位键的字典；挂进 <c>Window.Resources.MergedDictionaries</c> 后再 Apply。</summary>
    public static ResourceDictionary Create()
    {
        var dictionary = new ResourceDictionary();
        foreach (var token in ExamBoardThemeTokens.All)
            dictionary[token.BrushKey] = Brushes.Transparent;
        return dictionary;
    }

    public static void Apply(ResourceDictionary dictionary, IReadOnlyDictionary<string, SolidColorBrush> brushes)
    {
        foreach (var token in ExamBoardThemeTokens.All)
        {
            if (brushes.TryGetValue(token.Id, out var brush))
                dictionary[token.BrushKey] = brush;
        }
    }
}