using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Corkboard.Core.Models.Board;
using Corkboard.Core.Services.Board;

namespace Corkboard.Core.Controls;

/// <summary>
///     承载富文本内容的控件：板子上的卡片只读显示，布置作业表单里当编辑器用。
///     <para>
///         内容是 <b>HTML 片段</b>（<see cref="Html" />），由第三方富文本控件
///         （AvaloniaRichEditor）自己解析、编辑、回写；<see cref="Html" /> 双向绑定时
///         拿到的一直是「没被我们补过默认样式」的干净 HTML，可以直接落盘。
///     </para>
///     <para>
///         默认字号 / 默认颜色（设置 → 作业板 → 内容默认样式）在加载时逐段补成行内样式
///         （见 <see cref="BoardHtmlStyling" />），编辑时补进去的那层在回写前摘掉；
///         <see cref="DefaultContentColor" /> 留空表示跟随主题前景色，跟随的那一份在
///         <see cref="OnAttachedToVisualTree" /> 之后（拿得到主题资源时）解析，
///         主题一变就重新解析并重载——所以内容里的颜色不会被烘死。
///     </para>
///     <para>
///         回写接的是库的几条通知：用户打字 / 改格式会置脏（<see cref="RichEditor.IsModified" /> 与
///         <c>TextChanged</c>），程序化装填走 <c>DocumentChanged</c>，键盘输入还会同步走
///         <see cref="OnTextInput" />；另有一条 150ms 的「打字心跳」兜底（库的通知都要等重绘，
///         真机上出现过点了编辑区之后连着敲几段一条都不发的情况）。
///         只有 <see cref="RichEditor.IsModified" /> 为真（= 真被用户改过）才回写，回写完立刻
///         <see cref="RichEditor.MarkSaved" />：装填之后顺带发出来的那一发事件不会把内容又规范化一遍，
///         同一次编辑带来的重复事件也不会重复回写。
///     </para>
///     <para>
///         控件的 <see cref="RichEditor.IsReadOnly" /> 默认为真：默认当查看器，表单里显式改成假。
///     </para>
///     <para>
///         卡片（只读）里的图片按控件宽度等比缩到放得下：落盘 HTML 里的 <c>width</c>/<c>height</c> 是插入时
///         按编辑器宽度写死的，卡片比它窄的时候库既不缩放也不裁剪（直接按 <c>ImageBlock.Width</c> 画），
///         右边会被 <see cref="Control.ClipToBounds" /> 切掉。详见 <c>FitImagesToWidth</c>。
///     </para>
/// </summary>
public class RichTextBlock : RichEditor
{
    /// <summary>内容的 HTML 片段；双向绑定时回写的是可以落盘的干净版本。</summary>
    public static readonly StyledProperty<string?> HtmlProperty =
        AvaloniaProperty.Register<RichTextBlock, string?>(nameof(Html), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>内容的默认字号（DIP），对应「设置 → 作业板 → 内容默认样式」。</summary>
    public static readonly StyledProperty<double> DefaultContentFontSizeProperty =
        AvaloniaProperty.Register<RichTextBlock, double>(
            nameof(DefaultContentFontSize), BoardContentStyle.DefaultFontSize);

    /// <summary>内容的默认颜色；为空表示跟随主题前景色。</summary>
    public static readonly StyledProperty<Color?> DefaultContentColorProperty =
        AvaloniaProperty.Register<RichTextBlock, Color?>(nameof(DefaultContentColor));

    /// <summary>这次加载补进去的两条样式声明，回写时用它认出「哪些是我们补的」。</summary>
    private string _appliedFontSizeCss = string.Empty;
    private string _appliedColorCss = string.Empty;

    /// <summary>装填 / 回写期间不把文档变化当成用户编辑，免得自己触发自己。</summary>
    private bool _isLoading;

    /// <summary>正在往 <see cref="HtmlProperty" /> 回写：这一次属性变化是我们自己写的，不能再装填一遍。</summary>
    private bool _publishing;

    /// <summary>正在补「打字默认色」：这一步会让库重绘、可能再触发事件，别自己套自己。</summary>
    private bool _armingTypingColor;

    /// <summary>
    ///     打字心跳：库的 <c>TextChanged</c> 要等下一次重绘才异步发出来，真机上还出现过「点过编辑区之后连着敲几段都不发」
    ///     的情况 —— 光靠事件，新打的字会一直黑着、回写也会停摆（「布置」按钮灰着）。可编辑时用它兜底。
    /// </summary>
    private DispatcherTimer? _typingWatch;

    /// <summary>打字心跳间隔：够快，肉眼看不到黑字停留；够慢，不白烧 UI 线程。</summary>
    private static readonly TimeSpan TypingWatchInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>
    ///     库的内容区左右各留 10 DIP（反编译 RichEditor.cs:4648 / 7298 里插入图片用的可用宽度就是
    ///     <c>ContentLayoutWidth - 20</c>），所以能放下的图片宽度 = 控件宽度减 20。
    /// </summary>
    private const double ImageContentInset = 20;

    /// <summary>上一次把图片缩到的可用宽度（DIP）；<see cref="double.NaN" /> 表示还没缩过。</summary>
    private double _fittedImageWidth = double.NaN;

    static RichTextBlock()
    {
        HtmlProperty.Changed.AddClassHandler<RichTextBlock>((control, _) => control.ReloadDocument());
        DefaultContentFontSizeProperty.Changed.AddClassHandler<RichTextBlock>((control, _) => control.ReloadDocument());
        DefaultContentColorProperty.Changed.AddClassHandler<RichTextBlock>((control, _) => control.ReloadDocument());
        IsReadOnlyProperty.Changed.AddClassHandler<RichTextBlock>((control, _) => control.OnReadOnlyChanged());
    }

    public RichTextBlock()
    {
        // 默认是查看器：卡片上不接收指针、也没有右键菜单（卡片整块要留给壳的整窗长按拖动）。
        IsReadOnly = true;
        ShowFormattingMenu = false;

        // 控件完全自绘、自己不裁剪（实测高度不够时会把整篇画到边界之外），所以必须由它自己剪掉。
        ClipToBounds = true;

        // 库的改动通知有两条，缺一不可：
        //   * 用户打字 / 改格式 → 只把内部 _textChangedPending 置真，等下一次**重绘**才异步发 TextChanged
        //     （RichEditor.cs 的 RaisePendingChangeEvents 是从 Render 里调的），同时把 IsModified 置真；
        //   * 程序化装填（LoadHtml 内部换 Document）→ 发 DocumentChanged，期间 IsModified 先真后假。
        // 早先只接了 DocumentChanged，而它对用户编辑从不发，于是敲进去的内容永远回写不出去
        // （表现就是「布置」按钮一直灰着、占位提示不消失）。
        DocumentChanged += OnDocumentChanged;
        TextChanged += OnTextChanged;

        // SetModified 是同步发的（RichEditor.cs:3094-3101），比等重绘的 TextChanged 及时；多一条触发不亏。
        IsModifiedChanged += OnIsModifiedChanged;
    }

    public string? Html
    {
        get => GetValue(HtmlProperty);
        set => SetValue(HtmlProperty, value);
    }

    public double DefaultContentFontSize
    {
        get => GetValue(DefaultContentFontSizeProperty);
        set => SetValue(DefaultContentFontSizeProperty, value);
    }

    public Color? DefaultContentColor
    {
        get => GetValue(DefaultContentColorProperty);
        set => SetValue(DefaultContentColorProperty, value);
    }

    /// <summary>
    ///     当前生效的默认文字颜色：设置里指定过就用它，否则是主题前景色。
    ///     「清除格式」要把选段恢复成这个颜色，所以得让宿主读得到。
    /// </summary>
    public Color ResolvedDefaultColor => ResolveTextColor();

    /// <summary>按当前的 HTML 与默认样式重新装填内容（外部要强行刷新时也可以调）。</summary>
    public void ReloadDocument()
    {
        // _publishing 那一支：这次 Html 变化是我们自己回写造成的，重装会把光标甩回开头，直接跳过。
        if (_isLoading || _publishing)
            return;

        _isLoading = true;
        try
        {
            var fontSize = BoardContentStyle.ClampFontSize(DefaultContentFontSize);
            _appliedFontSizeCss = BoardHtmlStyling.BuildFontSizeCss(fontSize);
            _appliedColorCss = BoardHtmlStyling.BuildColorCss(ResolveTextColor());

            // 这个属性名字骗人：它只决定段落行高，**不改字形大小**（离屏抓像素实测），字形大小只能靠
            // 上面注入的 font-size。这里同步过去只是让行距跟着默认字号走，免得大字号的行挤在一起。
            DefaultFontSize = BoardHtmlStyling.ToPoints(fontSize);
            ApplyAppFontFamily();

            // 光标也是库写死的黑色（反编译里 CaretBrush 默认 Brushes.Black），暗色主题下根本看不见：
            // 让它跟正文一个颜色，设置里指定了内容颜色时也跟着走。
            CaretBrush = new SolidColorBrush(ResolveTextColor());

            // 空内容也垫一个带默认色的零宽 run：空文档里一个 run 都没有，输入法预编辑串会退回库写死的黑色
            // （见 BoardHtmlStyling.SeedChar），垫上之后「还没按回车」的那些字也是默认色。
            // ⚠️ 判空不能只数文字：只有一张图片 / 一张表格时纯文本也是空的，那样会被种子段落顶掉整篇内容。
            var source = !IsReadOnly && !BoardHtmlStyling.HasMeaningfulContent(Html)
                ? "<p>" + BoardHtmlStyling.SeedChar + "</p>"
                : Html;

            LoadHtml(BoardHtmlStyling.Apply(source, _appliedFontSizeCss, _appliedColorCss));

            // 新文档里的图片尺寸又是 HTML 里写死的那一份，重新按当前宽度缩一遍。
            _fittedImageWidth = double.NaN;
        }
        finally
        {
            _isLoading = false;
        }

        // 换 Document 会把待用格式清掉（ResetInteractionState，RichEditor.cs:2958/3015），装完补一发：
        // 空文档里一个 run 都没有、没得继承，这样打开表单直接打字也能是第一时间的默认色（不闪一下黑）。
        if (!IsReadOnly && !BoardHtmlStyling.HasVisibleContent(GetPlainText()))
            EnsureTypingColor();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // 先缩图再交给库测量：库算内容高度时直接读 ImageBlock.Height（RichEditor.cs:6696），
        // 所以在测量前改掉尺寸，卡片高度一次就量对，不会先高一下再缩回来。
        FitImagesToWidth(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }

    /// <summary>
    ///     把文档里的块级图片等比缩到 <paramref name="availableWidth" /> 之内；本来就放得下（或宽度未知）的不动。
    ///     <para>
    ///         只处理只读展示（卡片）：编辑器里的图片由库按插入时的可用宽度管，而且在编辑态改尺寸会被
    ///      <c>PublishContent</c> 回写进落盘 HTML（把缩小的尺寸烘死，原图再也放不大）。
    ///     </para>
    /// </summary>
    private void FitImagesToWidth(double availableWidth)
    {
        if (!IsReadOnly || Document is not { } document)
            return;

        var limit = availableWidth - ImageContentInset;
        if (!double.IsFinite(limit) || limit <= 0)
            return;

        // 宽度没变就别反复扫块列表（每次测量都会走到这里）。
        if (double.IsFinite(_fittedImageWidth) && Math.Abs(_fittedImageWidth - limit) < 1)
            return;

        _fittedImageWidth = limit;

        foreach (var image in document.Blocks.OfType<ImageBlock>())
        {
            var width = image.Width;
            var height = image.Height;

            // 没写宽度时库按 200 画（ImageBlock.Width = NaN 表示自然尺寸，但绘制路径里退回 200），
            // 这里用位图自己的像素宽换算成 DIP 来判断到底放不放得下。
            if (!double.IsFinite(width) || width <= 0)
            {
                var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
                if (image.Image?.PixelSize is not { Width: > 0 } pixel)
                    continue;

                width = pixel.Width / scaling;
                height = pixel.Height / scaling;
            }

            if (width <= limit)
                continue;

            var factor = limit / width;
            image.Width = limit;
            image.Height = double.IsFinite(height) && height > 0 ? height * factor : double.NaN;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TopLevel.GetTopLevel(this) is { } topLevel)
            topLevel.ActualThemeVariantChanged += OnThemeVariantChanged;

        // 附加之后才拿得到主题资源与应用程序字体，所以这里再装填一次（覆盖先前用兜底色加载的那次）。
        ReloadDocument();

        if (!IsReadOnly)
            StartTypingWatch();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopTypingWatch();

        if (TopLevel.GetTopLevel(this) is { } topLevel)
            topLevel.ActualThemeVariantChanged -= OnThemeVariantChanged;

        base.OnDetachedFromVisualTree(e);
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e) => ReloadDocument();

    private void OnReadOnlyChanged()
    {
        if (IsReadOnly)
            StopTypingWatch();
        else if (TopLevel.GetTopLevel(this) is not null)
            StartTypingWatch();
    }

    private void StartTypingWatch()
    {
        if (_typingWatch is not null)
            return;

        _typingWatch = new DispatcherTimer { Interval = TypingWatchInterval };
        _typingWatch.Tick += OnTypingWatchTick;
        _typingWatch.Start();
    }

    private void StopTypingWatch()
    {
        if (_typingWatch is null)
            return;

        _typingWatch.Tick -= OnTypingWatchTick;
        _typingWatch.Stop();
        _typingWatch = null;
    }

    private void OnTypingWatchTick(object? sender, EventArgs e)
    {
        if (IsReadOnly || _isLoading || _publishing)
            return;

        // 用户把内容删光时种子（零宽空格）也一起被删掉了，文档又变回「一个 run 都没有」；
        // 补一次装填把种子放回去，不然下次输入法预编辑又是黑的。种子在位时光标处有颜色，不会来回装填。
        // 判空看的是整篇内容（图片 / 表格也算内容），不然一张图片会被反复重装、闪个不停。
        if (!BoardHtmlStyling.HasMeaningfulContent(Html) && GetCaretFormat().Foreground is null)
        {
            ReloadDocument();
            return;
        }

        // 光标处本来就有颜色可继承、也没有待回写的改动 → 没什么可做的。
        if (!IsModified && GetCaretFormat().Foreground is not null)
            return;

        EnsureTypingColor();
        PublishContent();
    }

    private void OnIsModifiedChanged(object? sender, EventArgs e)
    {
        if (!IsModified)
            return;

        EnsureTypingColor();

        // ⚠️ 回写必须推迟到这一轮库调用结束之后：库是先 PushUndo() 再往文档里插内容
        // （图片插入 RichEditor.cs:1331-1332「PushUndo(); InsertBlockAtCaret(imageBlock);」），
        // 而 PushUndo 里的 SetModified(true) 会**同步**把我们叫到这里 —— 那时候内容还没进文档。
        // 就地回写会把「插入前」的文档发出去（内容没变），还顺手 MarkSaved 把脏标记清掉，
        // 于是图片永远进不了 DraftHtml：「单独放一张图片存不住、布置按钮一直灰着」就是这个原因。
        Dispatcher.UIThread.Post(PublishContent, DispatcherPriority.Background);
    }

    /// <summary>
    ///     键盘输入这条路是同步的（基类在 <c>RichEditor.cs:8627-8638</c> 里直接 <c>InsertText(e.Text)</c>），
    ///     所以补色与回写都在这里就地做：库的 <c>TextChanged</c> 由渲染路径异步发出，实测「先点一下编辑区再打字」
    ///     时第一段字符等不到那一发，暗色下就一直显示黑的。
    /// </summary>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (!e.Handled)
            return;

        EnsureTypingColor();
        PublishContent();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        // 先补色再回写：补色会把「刚敲进来、还没颜色」的那段刷成默认色，回写时它已经是干净内容了。
        EnsureTypingColor();
        PublishContent();
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => PublishContent();

    /// <summary>
    ///     把「没有颜色的文字」刷成默认色 —— 暗色主题下这条决定了刚打出来的字是白的还是黑的。
    ///     <para>
    ///         库渲染时把没有显式 <c>Foreground</c> 的 run 写死成黑色（<c>RichEditor.cs:3906</c>：
    ///         <c>run.Foreground ?? Brushes.Black</c>）。装填时我们已经给每段文字注入了行内
    ///         <c>&lt;span style="color:"&gt;</c>，所以已有内容都是带色的；但**新敲进来的字符**是在空段落里
    ///         凭空造的 run，没有东西可继承 —— 实测（真机 diag）第一段字打出来时 <c>caret=&lt;null&gt;</c>、字形是黑的。
    ///     </para>
    ///     <para>
    ///         补色的手段是 <see cref="RichEditor.SetForeground" />：光标停在词里 / 词尾时它改的是**整个词**
    ///         （<c>RichEditor.cs:6287-6294</c>），光标停在不带字的位置时才记成「待用格式」（<c>:6298</c>，
    ///         由插入路径套用，<c>:3533-3543</c>）。这两种都正好是我们想要的效果，而且只在「光标处本来就没颜色」时动手，
    ///         用户自己挑过的颜色不会被覆盖。
    ///     </para>
    ///     <para>
    ///         只在打字之后补：那时光标一定是收起的，不会碰上用户拉出的选区；光标的「待用格式」会被库的按键路径
    ///         反复清掉（<c>ResetCaretBlink</c>，<c>RichEditor.cs:3083</c>），指望装填时设一次是不够的。
    ///     </para>
    /// </summary>
    private void EnsureTypingColor()
    {
        if (IsReadOnly || _isLoading || _publishing || _armingTypingColor)
            return;

        // 已经有颜色可继承（用户自己设过色，或光标就在带色的文字里）就别插手。
        if (GetCaretFormat().Foreground is not null)
            return;

        _armingTypingColor = true;
        try
        {
            SetForeground(new SolidColorBrush(ResolveTextColor()));
        }
        finally
        {
            _armingTypingColor = false;
        }
    }

    /// <summary>
    ///     把编辑器里的内容摘掉我们补的那层默认样式与颜色种子，回写给 <see cref="Html" />。
    ///     <para>
    ///         只在 <see cref="RichEditor.IsModified" /> 为真时动手：用户打字 / 改格式会把它置真，
    ///         而装填（<c>LoadHtml</c>）结束时会自己清回假，于是「装填后顺带发的那一发 TextChanged」
    ///         被跳过，内容原样留着、不会被重新序列化一遍。
    ///         回写完 <see cref="RichEditor.MarkSaved" /> 再清一次，重复事件就成了空操作。
    ///     </para>
    /// </summary>
    private void PublishContent()
    {
        if (_isLoading || _publishing || IsReadOnly || !IsModified)
            return;

        _publishing = true;
        try
        {
            // 回写的是摘掉默认样式与种子零宽空格的干净 HTML：双向绑定那一头可以直接落盘。
            // 空内容统一写成空串（编辑器会把空文档规范化成 <p data-are-empty="1"><br></p>，
            // 那既会让「有没有内容」的判断失真，也会把空段落落进文件）。
            // ⚠️ 判空用 HasMeaningfulContent，不看 GetPlainText()：只有一张图片 / 一张表格时纯文本是空的，
            // 早先那样写会把整篇内容清成空串 —— 表现就是「单独放一张图片上去存不住、按钮一直灰着」。
            var cleaned = BoardHtmlStyling.StripSeeds(
                BoardHtmlStyling.Strip(ToHtml(), _appliedFontSizeCss, _appliedColorCss));
            var html = BoardHtmlStyling.HasMeaningfulContent(cleaned) ? cleaned : string.Empty;

            if (!string.Equals(html, Html, StringComparison.Ordinal))
                SetCurrentValue(HtmlProperty, html);

            MarkSaved();
        }
        finally
        {
            _publishing = false;
        }
    }

    /// <summary>默认颜色：设置里指定的就用它，没指定就跟随主题前景色。</summary>
    private Color ResolveTextColor()
    {
        if (DefaultContentColor is { } color)
            return color;

        // ⚠️ 取主题资源必须显式带上主题变体：不带变体的 TryFindResource 走的是「默认」那一套字典，
        // 暗色主题下拿到的是亮色主题的近黑 #E4000000 —— 于是暗色底下打出来的字是黑的、看不见
        // （用户 m01501：「暗色模式下打字，字不应该默认是白色字体吗」）。
        var variant = ActualThemeVariant;
        if (variant == ThemeVariant.Default && Application.Current is { } app)
            variant = app.ActualThemeVariant;

        variant = variant == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (this.TryFindResource("TextFillColorPrimaryBrush", variant, out var resource)
            && resource is ISolidColorBrush brush)
        {
            return brush.Color;
        }

        return variant == ThemeVariant.Dark ? Colors.White : Colors.Black;
    }

    /// <summary>让库自己的默认字体跟上应用字体（用户可以在设置里换字体）。</summary>
    private void ApplyAppFontFamily()
    {
        if (this.TryFindResource("AppFontFamily", out var resource) && resource is FontFamily family)
            DefaultFontFamily = family;
    }
}
