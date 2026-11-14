using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Net.Http;

namespace Corkboard.Views.ControlCenter;

/// <summary>
///     集控推图的全屏浮窗：铺满屏幕、置顶、不进任务栏，只显示一张等比缩放的图。
///     <para>
///         下载在 <see cref="OnOpened" /> 里异步做（超时 15 秒），拿到的字节先读进
///         <see cref="MemoryStream" /> 再解码——HTTP 响应流不保证可定位，
///         直接喂 <see cref="Bitmap" /> 偶发「流不支持定位」（关于页的头像也踩过同一个坑）。
///     </para>
///     <para>
///         关闭有三条路：到时长自动关、点窗口任意处、按 Esc。全部走同一个幂等方法
///         <see cref="CloseWindow" />；关掉之后异步下载回来也不再碰控件（<see cref="_closed" /> 早退）。
///     </para>
/// </summary>
public partial class ControlImageWindow : Window
{
    /// <summary>下载超时：推图是即时指令，等太久不如直接报失败让老师重推。</summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(15);

    private readonly string _url;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DispatcherTimer _closeTimer;

    private bool _closed;

    /// <summary>建一扇全屏推图窗。<paramref name="durationSeconds" /> 到点自动关闭（这里再夹一次，防止直接构造时越界）。</summary>
    public ControlImageWindow(string url, int durationSeconds, IHttpClientFactory httpClientFactory)
    {
        _url = url;
        _httpClientFactory = httpClientFactory;

        InitializeComponent();

        var seconds = Math.Clamp(durationSeconds, 1, 3600);
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _closeTimer.Tick += OnCloseTimerTick;

        Closed += OnWindowClosed;
    }

    /// <summary>窗口打开后开始下载与计时；它们都在 UI 线程上跑。</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // 键盘焦点跟着全屏窗口，Esc 才收得到；ShowActivated 默认是开的。
        Focus();
        _closeTimer.Start();
        _ = LoadImageAsync();
    }

    /// <summary>点窗口任意处关闭（Image 上的点击会冒泡到这里）。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        CloseWindow();
        e.Handled = true;
    }

    /// <summary>按 Esc 关闭。</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseWindow();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private async Task LoadImageAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = DownloadTimeout;

            using var response = await client
                .GetAsync(_url, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(true);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(true);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(true);
            buffer.Position = 0;

            var bitmap = new Bitmap(buffer);
            if (_closed)
            {
                bitmap.Dispose();
                return;
            }

            if (this.FindControl<Image>("Picture") is { } picture)
                picture.Source = bitmap;
        }
        catch (Exception)
        {
            // 失败只显示一行提示：推图这条指令本身已经成功回执，窗口留着让用户点掉即可。
            if (_closed)
                return;

            ShowLoadFailed();
        }
    }

    private void ShowLoadFailed()
    {
        if (this.FindControl<Image>("Picture") is { } picture)
            picture.IsVisible = false;

        if (this.FindControl<TextBlock>("ErrorText") is { } errorText)
            errorText.IsVisible = true;
    }

    private void OnCloseTimerTick(object? sender, EventArgs e) => CloseWindow();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _closeTimer.Stop();
        _closeTimer.Tick -= OnCloseTimerTick;
        Closed -= OnWindowClosed;
    }

    /// <summary>幂等关闭：定时器、点击、Esc 三条路都可能走到这里。</summary>
    private void CloseWindow()
    {
        if (_closed)
            return;

        _closed = true;
        _closeTimer.Stop();
        Close();
    }
}