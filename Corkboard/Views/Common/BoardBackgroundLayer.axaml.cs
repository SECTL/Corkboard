using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Corkboard.Core.Background;

namespace Corkboard.Views.Common;

/// <summary>
///     全屏板子的背景层（考试看板与看板模式共用）。
///     <para>
///         把 <see cref="Settings" /> 指向那份板子的配置对象即可：本控件自己订阅
///         <see cref="INotifyPropertyChanged" />，配置一改就重画，窗口 code-behind 不用管。
///         （配置对象是单例、窗口是 transient，所以必须在脱离可视树时退订，见 <see cref="OnDetachedFromVisualTree" />。）
///     </para>
///     <para>
///         图片读不出来（文件被删、路径在别的机器上）时<b>安静地退回主题底色</b>，
///         不抛异常也不弹提示：背景是装饰，不是功能。
///     </para>
/// </summary>
public partial class BoardBackgroundLayer : UserControl
{
    /// <summary>背景配置（考试看板或看板模式各自那一份）。</summary>
    public static readonly StyledProperty<BoardBackgroundSettings?> SettingsProperty =
        AvaloniaProperty.Register<BoardBackgroundLayer, BoardBackgroundSettings?>(nameof(Settings));

    /// <summary>模糊之后的图片边缘会透出下面的底色，稍微放大一点把边溢出屏幕外。</summary>
    private const double BlurOverflowScale = 1.06;

    /// <summary>已解码的图片缓存：设置页改一下暗化不该让整张图重新解码。</summary>
    private static readonly Dictionary<string, Bitmap> BitmapCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前正在加载的图片路径，用来忽略过期的加载结果（用户连点两下换图）。</summary>
    private string? _loadingPath;

    public BoardBackgroundLayer()
    {
        InitializeComponent();
        Apply();
    }

    /// <summary>背景配置。</summary>
    public BoardBackgroundSettings? Settings
    {
        get => GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SettingsProperty)
        {
            if (change.OldValue is BoardBackgroundSettings old)
                old.PropertyChanged -= OnSettingsPropertyChanged;

            if (change.NewValue is BoardBackgroundSettings current)
                current.PropertyChanged += OnSettingsPropertyChanged;

            Apply();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 控件可能在 Settings 之后才进树，这里补挂一次（重复挂不会生效两遍：-= 再 +=）。
        if (Settings is { } settings)
        {
            settings.PropertyChanged -= OnSettingsPropertyChanged;
            settings.PropertyChanged += OnSettingsPropertyChanged;
        }

        Apply();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (Settings is { } settings)
            settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    /// <summary>按当前配置重画三层。</summary>
    private void Apply()
    {
        if (BrushLayer is null || ImageLayer is null || DimLayer is null)
            return;

        var appearance = Settings?.ResolveAppearance() ?? BoardBackgroundAppearance.Theme;

        switch (appearance.Mode)
        {
            case BoardBackgroundMode.Solid:
                BrushLayer.Background = new SolidColorBrush(appearance.SolidColor ?? Colors.Black);
                BrushLayer.IsVisible = true;
                break;

            case BoardBackgroundMode.Gradient:
                BrushLayer.Background = BuildGradient(appearance);
                BrushLayer.IsVisible = true;
                break;

            default:
                // 跟随主题（以及图片模式没选到图时）：让窗口自己的底色透上来。
                BrushLayer.Background = null;
                BrushLayer.IsVisible = false;
                break;
        }

        var showImage = appearance.HasImage;
        ImageLayer.IsVisible = showImage;
        DimLayer.IsVisible = showImage && appearance.HasDim;
        DimLayer.Opacity = showImage ? appearance.Dim : 0;

        if (!showImage)
        {
            _loadingPath = null;
            ImageLayer.Source = null;
            ImageLayer.Effect = null;
            ImageLayer.RenderTransform = null;
            return;
        }

        ImageLayer.Effect = appearance.HasBlur
            ? new BlurEffect { Radius = appearance.Blur }
            : null;

        var scale = appearance.HasBlur ? BlurOverflowScale : 1;
        ImageLayer.RenderTransform = appearance.HasBlur ? new ScaleTransform(scale, scale) : null;

        LoadImage(appearance.ImagePath!);
    }

    /// <summary>
    ///     读图并塞给 <see cref="ImageLayer" />。解码放到线程池：
    ///     一张 4K 壁纸解码要几十毫秒，放在 UI 线程上就是开窗时卡一下。
    /// </summary>
    private void LoadImage(string path)
    {
        if (ImageLayer.Source is Bitmap current && _loadingPath == path &&
            BitmapCache.TryGetValue(path, out var cached) && ReferenceEquals(current, cached))
            return;

        if (BitmapCache.TryGetValue(path, out var bitmap))
        {
            _loadingPath = path;
            ImageLayer.Source = bitmap;
            return;
        }

        _loadingPath = path;
        var requested = path;

        _ = Task.Run(() =>
        {
            Bitmap? loaded = null;
            try
            {
                using var stream = File.OpenRead(requested);
                loaded = new Bitmap(stream);
            }
            catch (Exception)
            {
                // 文件没了、没权限、不是图片：一律当作「没有背景图」，界面上退回主题底色。
                loaded = null;
            }

            Dispatcher.UIThread.Post(() =>
            {
                // 加载期间用户又换了一张图：这次的结果直接丢掉。
                if (!string.Equals(_loadingPath, requested, StringComparison.OrdinalIgnoreCase))
                {
                    loaded?.Dispose();
                    return;
                }

                if (loaded is null)
                {
                    ImageLayer.Source = null;
                    ImageLayer.Effect = null;
                    ImageLayer.IsVisible = false;
                    DimLayer.IsVisible = false;
                    return;
                }

                lock (BitmapCache)
                {
                    if (!BitmapCache.ContainsKey(requested))
                        BitmapCache[requested] = loaded;
                }

                ImageLayer.Source = loaded;
            });
        });
    }

    /// <summary>
    ///     按角度拼一个线性渐变：0° 从左往右、顺时针转。
    ///     <para>
    ///         角度换成相对坐标的两个端点，画出来的方向才跟用户在设置里看到的一致
    ///         （直接用 90° 当「从上到下」，在宽屏上斜角会明显偏）。
    ///     </para>
    /// </summary>
    private static LinearGradientBrush BuildGradient(BoardBackgroundAppearance appearance)
    {
        var radians = appearance.GradientAngle * Math.PI / 180;
        var dx = Math.Cos(radians) / 2;
        var dy = Math.Sin(radians) / 2;

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5 - dx, 0.5 - dy, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5 + dx, 0.5 + dy, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(appearance.GradientStart ?? Colors.Black, 0),
                new GradientStop(appearance.GradientEnd ?? Colors.Black, 1)
            ]
        };
    }
}
