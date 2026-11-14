using Avalonia.Threading;
using Corkboard.Core.Langs.Common;
using Corkboard.Views.ControlCenter;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.ControlCenter;

/// <summary>
///     集控浮层的窗口调度：把指令处理器（后台线程）的要求切回 UI 线程，开 / 换 / 关两扇独立顶层窗口。
///     <para>
///         为什么需要这一层：Core 的命令处理器在后台线程上跑，而 Avalonia 的窗口只能在 UI 线程创建；
///         同时「同一时刻只留一扇公告窗 / 一扇图片窗」这条规则也要有个地方收口——
///         新的公告覆盖旧的，旧的先关掉再开新的，免得集控连续下发时叠出一摞窗口。
///     </para>
///     <para>
///         窗口引用一直攥在字段里，<c>Closed</c> 时置空：窗口关掉后还留着引用会让它回收不掉。
///         这里的方法<b>不向调用方抛异常</b>（开窗失败只记日志），避免一条指令把连接搞崩。
///     </para>
/// </summary>
public sealed class ControlCenterPresenter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ControlCenterPresenter> _logger;

    private ControlNoticeWindow? _noticeWindow;
    private ControlImageWindow? _imageWindow;

    public ControlCenterPresenter(IHttpClientFactory httpClientFactory, ILogger<ControlCenterPresenter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    ///     弹一条集控公告。同一时刻只有一扇公告窗：新的会替换旧的（先关旧的再开新的）。
    ///     可以在任意线程调用。
    /// </summary>
    public void ShowNotice(string? title, string? body)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? Resources.Control_Notice_DefaultTitle : title;
        RunOnUiThread(() => ShowNoticeCore(normalizedTitle, body));
    }

    /// <summary>
    ///     全屏推一张图。同一时刻只有一扇图片窗：新的会替换旧的。
    ///     可以在任意线程调用；下载与自动关闭都由窗口自己负责。
    /// </summary>
    public void ShowImage(string url, int durationSeconds) =>
        RunOnUiThread(() => ShowImageCore(url, durationSeconds));

    /// <summary>ui 线程调度；Dispatcher 已经停掉时只记日志，绝不让异常冒到命令处理器外面。</summary>
    private void RunOnUiThread(Action action)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return;
            }

            Dispatcher.UIThread.Post(action);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "集控浮窗调度失败");
        }
    }

    private void ShowNoticeCore(string title, string? body)
    {
        try
        {
            CloseNotice();

            var window = new ControlNoticeWindow(title, body);
            window.Closed += OnNoticeWindowClosed;
            _noticeWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "集控公告窗口显示失败");
            _noticeWindow = null;
        }
    }

    private void ShowImageCore(string url, int durationSeconds)
    {
        try
        {
            CloseImage();

            var window = new ControlImageWindow(url, durationSeconds, _httpClientFactory);
            window.Closed += OnImageWindowClosed;
            _imageWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "集控图片窗口显示失败");
            _imageWindow = null;
        }
    }

    /// <summary>关掉当前的公告窗（幂等，并先摘掉字段再关，避免 Closed 处理器把新窗口引用抹掉）。</summary>
    private void CloseNotice()
    {
        var window = _noticeWindow;
        _noticeWindow = null;

        if (window is null)
            return;

        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "关闭集控公告窗口时出错");
        }
    }

    /// <summary>关掉当前的图片窗（幂等）。</summary>
    private void CloseImage()
    {
        var window = _imageWindow;
        _imageWindow = null;

        if (window is null)
            return;

        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "关闭集控图片窗口时出错");
        }
    }

    private void OnNoticeWindowClosed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _noticeWindow))
            return;

        _noticeWindow = null;
        if (sender is ControlNoticeWindow window)
            window.Closed -= OnNoticeWindowClosed;
    }

    private void OnImageWindowClosed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _imageWindow))
            return;

        _imageWindow = null;
        if (sender is ControlImageWindow window)
            window.Closed -= OnImageWindowClosed;
    }
}