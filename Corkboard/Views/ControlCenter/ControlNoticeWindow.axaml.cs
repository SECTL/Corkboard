using Avalonia.Controls;
using Avalonia.Interactivity;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views.ControlCenter;

/// <summary>
///     集控下发的公告浮窗：无边框、置顶、不进任务栏、出现时不抢焦点。
///     <para>
///         它是一扇<b>独立顶层窗口</b>：主窗口可能收在托盘里、置底在桌面上或者被别的窗口盖住，
///         公告必须能单独浮出来。为了不抢学生正在打字的焦点，XAML 上
///         <c>ShowActivated="False"</c>，代码里也不调 <c>Activate()</c>。
///     </para>
///     <para>
///         窗口本身透明、卡片自带实体底色与圆角（同 <c>PageOverlayWindow</c>），
///         否则无边框窗口的方形底会在圆角外面露出黑角。
///     </para>
/// </summary>
public partial class ControlNoticeWindow : Window
{
    /// <summary>建一扇公告窗。标题为空时用「公告」，正文可以为空。</summary>
    public ControlNoticeWindow(string? title, string? body)
    {
        InitializeComponent();

        // 透明窗口 + 透明背景：卡片圆角外面不能露出窗口的方块底。
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        // 注意：窗口自己有个 Resources 属性（IResourceDictionary），会盖住同名的本地化静态类，
        // 所以这里用别名 CR 取 Core 的文案。
        var titleText = this.FindControl<TextBlock>("TitleText");
        if (titleText is not null)
            titleText.Text = string.IsNullOrWhiteSpace(title) ? CR.Control_Notice_DefaultTitle : title.Trim();

        var normalizedBody = body?.Trim() ?? string.Empty;
        if (this.FindControl<TextBlock>("BodyText") is { } bodyText)
            bodyText.Text = normalizedBody;

        // 没有正文时把整个滚动区收起来，窗口才不会留一块空白。
        if (this.FindControl<ScrollViewer>("BodyScroll") is { } bodyScroll)
            bodyScroll.IsVisible = normalizedBody.Length > 0;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}