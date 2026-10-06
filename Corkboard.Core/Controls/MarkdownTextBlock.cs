using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Services.Markdown;

namespace Corkboard.Core.Controls;

/// <summary>
///     作业内容的显示控件：把 Markdown 原文连同颜色/字号标注画成富文本。
///     <para>
///         作业板列表与布置作业表单里的预览用的是同一个控件，所以「预览里看到的样子」
///         和「板子上显示的样子」必然一致——两边要是各画一套，迟早会出现预览好看、
///         落到板子上变样的问题。
///     </para>
///     <para>
///         内容通过 <see cref="Document" /> 传入。它是一条不可变记录，换内容就是换实例，
///         控件只比较引用、整份重建 <c>Inlines</c>（见 <see cref="RebuildInlines" />）。
///     </para>
/// </summary>
public class MarkdownTextBlock : TextBlock
{
    /// <summary>要显示的内容。<c>null</c> 表示什么都不画。</summary>
    public static readonly StyledProperty<BoardRichText?> DocumentProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, BoardRichText?>(nameof(Document));

    static MarkdownTextBlock()
    {
        DocumentProperty.Changed.AddClassHandler<MarkdownTextBlock>((block, _) => block.RebuildInlines());
    }

    /// <summary>
    ///     控件主题按 <see cref="TextBlock" /> 找：前景色、字体系列这些都由 TextBlock 的控件主题给，
    ///     派生类型自己没注册过主题，不指回去就一个样式都拿不到。
    /// </summary>
    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <summary>要显示的内容。</summary>
    public BoardRichText? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>
    ///     按 <see cref="Document" /> 重建整份 <c>Inlines</c>。
    ///     <para>
    ///         不做增量：一份作业内容顶多几十个 Run，整份换掉比维护差量简单，
    ///         也不会出现「标注改了但某一截没跟上」的错位。
    ///     </para>
    /// </summary>
    public void RebuildInlines()
    {
        var rendered = MarkdownInlineRenderer.Render(Document);

        var collection = Inlines;
        if (collection is null)
        {
            collection = new InlineCollection();
            Inlines = collection;
        }

        collection.Clear();
        foreach (var inline in rendered)
            collection.Add(inline);
    }
}
