using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Corkboard.Core.Background;
using Corkboard.Shared;

namespace Corkboard.Views.Common;

/// <summary>
///     背景编辑器的代码后置：把控件上的改动写进那一份 <see cref="BoardBackgroundSettings" />。
///     <para>
///         ⚠️ <b>所有改动都走「草稿 + 防抖」</b>：<c>ConfigHandlerBase</c> 每变一个属性就落一次盘，
///         而取色器拖动、滑杆拖动会一秒产生几十次变化——直接写配置等于每秒几十次磁盘写入。
///         这里统一攒 400ms，用户停手才写。
///     </para>
///     <para>
///         与设置卡的双向绑定无关：控件在这里手动同步（<c>_syncing</c> 挡回环），
///         所以宿主页面不需要为它写任何 ViewModel。
///     </para>
/// </summary>
public partial class BoardBackgroundEditor : UserControl
{
    /// <summary>防抖窗口：与作业板「窗口与收纳」页那个取色器同一档（400ms）。</summary>
    private static readonly TimeSpan CommitDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>背景图复制到软件数据目录下的这个子目录，配置里存绝对路径。</summary>
    private const string BackgroundFolderName = "backgrounds";

    /// <summary>背景配置（考试看板或看板模式各自那一份）。</summary>
    public static readonly StyledProperty<BoardBackgroundSettings?> SettingsProperty =
        AvaloniaProperty.Register<BoardBackgroundEditor, BoardBackgroundSettings?>(nameof(Settings));

    private readonly DispatcherTimer _commitTimer;
    private bool _syncing;

    public BoardBackgroundEditor()
    {
        InitializeComponent();

        _commitTimer = new DispatcherTimer { Interval = CommitDelay };
        _commitTimer.Tick += (_, _) =>
        {
            _commitTimer.Stop();
            Commit();
        };
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

            SyncFromSettings();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Settings is { } settings)
        {
            settings.PropertyChanged -= OnSettingsPropertyChanged;
            settings.PropertyChanged += OnSettingsPropertyChanged;
        }

        SyncFromSettings();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // 离开页面时把还没落盘的改动写掉，别让用户「拖完滑杆就切页」白改。
        _commitTimer.Stop();
        Commit();

        if (Settings is { } settings)
            settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 自己写的就别再同步回来（否则拖动滑杆时值会被来回夹取、跳一格）。
        if (_syncing)
            return;

        SyncFromSettings();
    }

    /// <summary>把配置搬到控件上。</summary>
    private void SyncFromSettings()
    {
        var settings = Settings;
        if (settings is null || ModeCombo is null)
            return;

        _syncing = true;
        try
        {
            var mode = settings.BackgroundMode;
            ModeCombo.SelectedIndex = mode switch
            {
                BoardBackgroundMode.Solid => 1,
                BoardBackgroundMode.Gradient => 2,
                BoardBackgroundMode.Image => 3,
                _ => 0
            };

            SolidColorPicker.Color = BoardBackgroundSettings.ParseColor(
                settings.BackgroundColor, BoardBackgroundSettings.DefaultSolidColor);
            GradientStartPicker.Color = BoardBackgroundSettings.ParseColor(
                settings.BackgroundGradientStart, BoardBackgroundSettings.DefaultGradientStart);
            GradientEndPicker.Color = BoardBackgroundSettings.ParseColor(
                settings.BackgroundGradientEnd, BoardBackgroundSettings.DefaultGradientEnd);

            AngleSlider.Value = settings.BackgroundGradientAngle;
            DimSlider.Value = settings.BackgroundDim;
            BlurSlider.Value = settings.BackgroundBlur;
        }
        finally
        {
            _syncing = false;
        }

        UpdateValueLabels();
        UpdateCardVisibility();
    }

    /// <summary>
    ///     只有当前模式用得上的卡才显示（跟着主题时一张颜色卡都不该露出来）。
    ///     <para>
    ///         用户要求「下拉框选择之后要弹出对应的设置内容，不属于那个的设置内容就不要弹出来」：
    ///         所以这里不是「禁用」而是整张卡 <c>IsVisible=false</c>——
    ///         留一张灰着的卡在那儿，用户还是得读一遍才知道它跟当前模式无关。
    ///     </para>
    ///     <para>
    ///         纯色只要颜色、渐变要两个颜色 + 角度、图片要选图 + 压暗 + 模糊、跟随主题什么都不用配。
    ///     </para>
    /// </summary>
    private void UpdateCardVisibility()
    {
        var mode = CurrentMode();
        SolidCard.IsVisible = mode == BoardBackgroundMode.Solid;

        var isGradient = mode == BoardBackgroundMode.Gradient;
        GradientCard.IsVisible = isGradient;
        AngleCard.IsVisible = isGradient;
        AngleSlider.IsEnabled = isGradient;

        var isImage = mode == BoardBackgroundMode.Image;
        ImageCard.IsVisible = isImage;
        DimCard.IsVisible = isImage;
        BlurCard.IsVisible = isImage;
        ClearImageButton.IsEnabled = !string.IsNullOrWhiteSpace(Settings?.BackgroundImagePath);
        PickImageButton.Content = string.IsNullOrWhiteSpace(Settings?.BackgroundImagePath)
            ? Core.Langs.Common.Resources.Settings_Background_ImagePick
            : Core.Langs.Common.Resources.Settings_Background_ImageReplace;
    }

    private void UpdateValueLabels()
    {
        AngleValue.Text = string.Create(CultureInfo.InvariantCulture, $"{AngleSlider.Value:0}°");
        DimValue.Text = string.Create(CultureInfo.InvariantCulture, $"{DimSlider.Value:P0}");
        BlurValue.Text = string.Create(CultureInfo.InvariantCulture, $"{BlurSlider.Value:0}");
    }

    /// <summary>控件上现在选的是哪个模式。</summary>
    private BoardBackgroundMode CurrentMode() => ModeCombo.SelectedIndex switch
    {
        1 => BoardBackgroundMode.Solid,
        2 => BoardBackgroundMode.Gradient,
        3 => BoardBackgroundMode.Image,
        _ => BoardBackgroundMode.Theme
    };

    // ── 控件事件：只改草稿，稍后统一提交 ─────────────────────

    private void OnModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || Settings is not { } settings)
            return;

        settings.BackgroundMode = CurrentMode();
        UpdateCardVisibility();
        ScheduleCommit();
    }

    private void OnSolidColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (_syncing || Settings is not { } settings)
            return;

        settings.BackgroundColor = ToHex(e.NewColor);
        ScheduleCommit();
    }

    private void OnGradientStartChanged(object? sender, ColorChangedEventArgs e)
    {
        if (_syncing || Settings is not { } settings)
            return;

        settings.BackgroundGradientStart = ToHex(e.NewColor);
        ScheduleCommit();
    }

    private void OnGradientEndChanged(object? sender, ColorChangedEventArgs e)
    {
        if (_syncing || Settings is not { } settings)
            return;

        settings.BackgroundGradientEnd = ToHex(e.NewColor);
        ScheduleCommit();
    }

    private void OnAngleChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateValueLabels();
        if (_syncing || Settings is not { } settings)
            return;

        settings.BackgroundGradientAngle = Math.Round(e.NewValue);
        ScheduleCommit();
    }

    private void OnDimChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateValueLabels();
        if (_syncing || Settings is not { } settings)
            return;

        settings.BackgroundDim = Math.Round(e.NewValue, 2);
        ScheduleCommit();
    }

    private void OnBlurChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateValueLabels();
        if (_syncing || Settings is not { } settings)
            return;

        settings.BackgroundBlur = Math.Round(e.NewValue);
        ScheduleCommit();
    }

    /// <summary>
    ///     挑一张背景图：选完复制进软件数据目录再记路径。
    ///     <para>
    ///         不记原始路径：用户从 U 盘 / 下载目录挑的图过几天就没了，背景会突然变回纯色；
    ///         复制一份到数据目录，删了临时文件也不影响。
    ///     </para>
    /// </summary>
    private async void OnPickImageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Settings is not { } settings || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Core.Langs.Common.Resources.Settings_Background_PickTitle,
                AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll]
            });

            if (files.Count == 0)
                return;

            var source = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                return;

            var target = CopyIntoDataFolder(source);
            if (target is null)
                return;

            settings.BackgroundImagePath = target;
            // 选了图却没切到「图片」模式的话，用户会以为没生效——顺手切过去。
            if (settings.BackgroundMode != BoardBackgroundMode.Image)
                settings.BackgroundMode = BoardBackgroundMode.Image;

            SyncFromSettings();
            ScheduleCommit();
        }
        catch (Exception)
        {
            // 选择器在某些平台 / 无权限目录上会抛异常，别让它从 async void 里冒出去。
        }
    }

    private void OnClearImageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Settings is not { } settings)
            return;

        settings.BackgroundImagePath = string.Empty;
        if (settings.BackgroundMode == BoardBackgroundMode.Image)
            settings.BackgroundMode = BoardBackgroundMode.Theme;

        SyncFromSettings();
        ScheduleCommit();
    }

    /// <summary>把挑中的图片复制进 <c>data/backgrounds/</c>，名字用内容摘要（同一张图不会存两份）。</summary>
    private static string? CopyIntoDataFolder(string sourcePath)
    {
        try
        {
            var folder = Path.Combine(Utils.DataRoot, BackgroundFolderName);
            Directory.CreateDirectory(folder);

            var extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".png";

            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var target = Path.Combine(folder, $"{SanitizeName(name)}{extension}");

            // 同名同尺寸就当已经有了（重挑同一张图不该多出一份文件）。
            var info = new FileInfo(sourcePath);
            if (File.Exists(target) && new FileInfo(target).Length == info.Length)
                return target;

            var unique = target;
            var index = 1;
            while (File.Exists(unique) && new FileInfo(unique).Length != info.Length)
            {
                unique = Path.Combine(folder,
                    $"{SanitizeName(name)}-{index.ToString(CultureInfo.InvariantCulture)}{extension}");
                index++;
            }

            if (!File.Exists(unique))
                File.Copy(sourcePath, unique);

            return unique;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>文件名里剔掉 Windows 不认的字符。</summary>
    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);

        var cleaned = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "background" : cleaned;
    }

    private static string ToHex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

    // ── 防抖提交 ─────────────────────────────────────────────

    private void ScheduleCommit()
    {
        _commitTimer.Stop();
        _commitTimer.Start();
    }

    /// <summary>
    ///     把草稿写进配置。属性本身已经写在 <see cref="Settings" /> 上了，
    ///     这里只做一次显式落盘（<c>ObservableObject</c> 的 setter 已经触发过一次，
    ///     但拖动期间被防抖吞掉的那些变化要靠这一次 Save 补齐）。
    /// </summary>
    private void Commit()
    {
        if (Settings is not { } settings)
            return;

        settings.NormalizeBackground();

        // 属性 setter 已经在改的那一刻触发过一次落盘；拖动期间被防抖吞掉的那几次靠这一次补齐。
        if (Core.Abstraction.IAppHost.TryGetService<Core.Services.Config.MainConfigHandler>() is { } handler)
            handler.Save();
    }
}
