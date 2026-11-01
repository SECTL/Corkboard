using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Views;

namespace Corkboard.Services.Ui;

/// <summary>
///     一个能画弹层的宿主自己的那一份状态。
///     <para>
///         每个宿主（壳、全屏展示窗口……）都拿一个，XAML 直接绑它的 <see cref="IsOpen" /> 与
///         <see cref="Content" />。分开绑而不是共用服务上那两个属性，是因为弹层同一时刻只会画在
///         <b>一个</b>宿主身上：几个宿主同时活着时，只有最上面那个该显示遮罩，其余要干干净净。
///     </para>
/// </summary>
public sealed partial class PageOverlayHost : ObservableObject
{
    internal PageOverlayHost(object owner, int priority)
    {
        Owner = owner;
        Priority = priority;
    }

    /// <summary>宿主自身（通常是那个 View 或 Window）。</summary>
    public object Owner { get; }

    /// <summary>层级：数字大的赢。同一时刻只有最高的那个宿主显示弹层。</summary>
    internal int Priority { get; }

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private object? _content;

    internal void Apply(bool isOpen, object? content)
    {
        IsOpen = isOpen;
        Content = content;
    }
}

/// <summary>
///     页面级弹层宿主：页面把要显示的内容塞进来，由宿主把它画出来。
///     <para>
///         有两条路：
///         <list type="bullet">
///             <item>
///                 <b>壳内自绘</b>（默认）：<c>MainView</c> 在窗口内容最上层画遮罩与内容。
///                 放在壳这一层而不是页面里，遮罩才能盖住自绘标题栏——模态要真的是"全窗"，
///                 只盖内容区会显得标题栏还亮着、模态不成立。
///             </item>
///             <item>
///                 <b>独立顶层窗口</b>（<see cref="UseSeparateWindow" /> 为真时）：
///                 主窗口「置底到桌面」后是被嵌进 <c>Progman</c> 的子窗口，
///                 Windows 不会给它键盘焦点，表单在壳里<b>一个字都打不进去</b>
///                 （详见 <see cref="PageOverlayWindow" /> 的说明）。
///                 所以这种形态下改用独立窗口承载表单，外观仍按弹层做。
///             </item>
///         </list>
///     </para>
///     <para>
///         自绘而不是开系统窗口的理由（没有标题栏、不依赖桌面端多窗口能力、手机端同样能用）
///         在独立窗口这条路上靠 <c>SystemDecorations=None</c> 等设置维持同样的观感。
///     </para>
///     <para>
///         ⚠️ <b>宿主可以有多个</b>：全屏展示窗口打开时要抢在壳前面把弹层画出来，
///         否则用户在全屏里点「编辑作业」，表单会跑到被它挡住的主窗口去，全屏上什么都看不见。
///     </para>
/// </summary>
public partial class PageOverlayService : ObservableObject
{
    /// <summary>壳（<c>MainView</c>）的层级。</summary>
    public const int ShellHostPriority = 0;

    /// <summary>全屏展示窗口的层级：比壳高，压在主窗口之上。</summary>
    public const int FullScreenHostPriority = 10;

    private readonly List<PageOverlayHost> _hosts = [];

    private bool _useSeparateWindow;
    private PageOverlayWindow? _window;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private object? _content;

    /// <summary>当前该把弹层画到谁身上；没有宿主注册时为 <see langword="null" />。</summary>
    public PageOverlayHost? ActiveHost => ResolveActiveHost();

    /// <summary>弹层关闭时触发，两种模式（壳内自绘 / 独立窗口）都会触发。</summary>
    public event EventHandler? Closed;

    /// <summary>
    ///     是否需要把弹层摊到独立顶层窗口里。由主窗口在应用/解除「置底到桌面」时写入，
    ///     因为只有它知道该能力是不是真的生效了（平台可能报不支持）。
    /// </summary>
    public bool UseSeparateWindow
    {
        get => _useSeparateWindow;
        set
        {
            if (_useSeparateWindow == value)
                return;

            _useSeparateWindow = value;
            RefreshHosts();
        }
    }

    /// <summary>登记一个能画弹层的宿主，返回它自己那份可绑定的状态。</summary>
    public PageOverlayHost CreateHost(object owner, int priority)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var host = new PageOverlayHost(owner, priority);
        RegisterHost(host);
        return host;
    }

    /// <summary>
    ///     把之前注销过的宿主重新挂回来（窗口收起再展开、内容树重新挂载时用）。
    ///     宿主对象本身不用换，XAML 上那份绑定继续有效。
    /// </summary>
    public void RegisterHost(PageOverlayHost? host)
    {
        if (host is null || _hosts.Contains(host))
            return;

        _hosts.Add(host);
        RefreshHosts();
    }

    /// <summary>
    ///     注销宿主。它正扛着弹层的话顺手关掉——宿主没了，弹层也不能留在半空。
    /// </summary>
    public void ReleaseHost(PageOverlayHost? host)
    {
        if (host is null || !_hosts.Remove(host))
            return;

        host.Apply(false, null);
        RefreshHosts();

        if (IsOpen && !UseSeparateWindow && ResolveActiveHost() is null)
            Close();
    }

    /// <summary>显示一层弹层。内容通常是某个表单控件。</summary>
    public void Show(object content)
    {
        if (UseSeparateWindow)
        {
            ShowInSeparateWindow(content);
            return;
        }

        Content = content;
        IsOpen = true;
    }

    public void Close()
    {
        CloseSeparateWindow();

        IsOpen = false;
        Content = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsOpenChanged(bool value) => RefreshHosts();

    partial void OnContentChanged(object? value) => RefreshHosts();

    private PageOverlayHost? ResolveActiveHost()
    {
        PageOverlayHost? active = null;

        // 同层级时后注册的赢（>= 而不是 >）。
        foreach (var host in _hosts)
        {
            if (active is null || host.Priority >= active.Priority)
                active = host;
        }

        return active;
    }

    private void RefreshHosts()
    {
        var active = UseSeparateWindow ? null : ResolveActiveHost();

        foreach (var host in _hosts)
        {
            var isMine = IsOpen && ReferenceEquals(host, active);
            host.Apply(isMine, isMine ? Content : null);
        }

        OnPropertyChanged(nameof(ActiveHost));
    }

    /// <summary>
    ///     独立窗口这条路：置底形态下主窗口拿不到键盘焦点，必须由这个普通顶层窗口来承载输入。
    ///     窗口置顶显示，保证贴在桌面上的挂件不会把表单挡在后面。
    /// </summary>
    private void ShowInSeparateWindow(object content)
    {
        // 壳内那条路要收干净，否则两条路会同时画一份内容。
        IsOpen = false;
        Content = null;

        if (_window is null)
        {
            _window = new PageOverlayWindow { Topmost = true };
            _window.Closed += OnSeparateWindowClosed;
        }

        _window.OverlayContent = content;

        if (!_window.IsVisible)
            _window.Show();

        // 置底的主窗口拿不到前台，表单要自己争取一下，否则用户点进输入框才生效。
        _window.Activate();
    }

    private void CloseSeparateWindow()
    {
        if (_window is not { } window)
            return;

        _window = null;
        window.Closed -= OnSeparateWindowClosed;
        window.Close();
    }

    private void OnSeparateWindowClosed(object? sender, EventArgs e)
    {
        if (_window is not { } window)
            return;

        _window = null;
        window.Closed -= OnSeparateWindowClosed;
        window.OverlayContent = null;

        // 用户直接关了独立窗口（点 X），也通知一下，与调用 Close() 走同一条事件。
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
