using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Exam.Theme;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Exam;

/// <summary>
///     考试看板主题的单例：解析当前配置、持有 13 个刷子实例，并在配置/应用亮暗变化时通知窗口刷新。
///     <para>
///         订阅必须挂两层：父配置管 <c>Source</c> / <c>CustomBase</c> / <c>PresetId</c> / <c>Palette</c>
///         整体替换，<c>Palette</c> 自己管 13 个颜色赋值——只订父配置会漏掉「改一个颜色」。
///         云端恢复会换掉整个 <see cref="MainConfigModel" />，<c>Reloaded</c> 后要全部重挂。
///     </para>
/// </summary>
public sealed class ExamBoardThemeService : IDisposable
{
    /// <summary>「跟随应用外观」时 13 个 token 对应的 Fluent 资源键。</summary>
    private static readonly IReadOnlyDictionary<string, string> FollowAppResourceKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["surface"] = "SolidBackgroundFillColorBaseBrush",
            ["text_primary"] = "TextFillColorPrimaryBrush",
            ["text_secondary"] = "TextFillColorSecondaryBrush",
            ["text_tertiary"] = "TextFillColorTertiaryBrush",
            ["subtitle"] = "TextFillColorSecondaryBrush",
            ["divider"] = "DividerStrokeColorDefaultBrush",
            ["upcoming"] = "SystemFillColorCautionBrush",
            ["in_progress"] = "SystemFillColorSuccessBrush",
            ["urgent"] = "SystemFillColorCriticalBrush",
            ["highlight"] = "SubtleFillColorSecondaryBrush",
            ["exit_track"] = "ControlStrokeColorDefaultBrush",
            // 退出圈两色没有对应的 Fluent 资源，走 Core 的兜底常量（与旧硬编码一致）。
            ["exit_ring_start"] = "",
            ["exit_ring_end"] = ""
        };

    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<ExamBoardThemeService> _logger;
    private ExamBoardThemeConfig? _observedConfig;
    private ExamBoardThemeOverrides? _observedPalette;
    private bool _refreshPending;

    public ExamBoardThemeService(MainConfigHandler configHandler, ILogger<ExamBoardThemeService> logger)
    {
        _configHandler = configHandler;
        _logger = logger;

        Colors = ExamBoardThemeResolver.Resolve(configHandler.Data.ExamBoardTheme, LookupFollowAppColor);
        Brushes = CreateBrushes(Colors);

        AttachConfig(configHandler.Data.ExamBoardTheme);
        _configHandler.Reloaded += OnConfigReloaded;

        if (Application.Current is { } application)
            application.ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    public ExamBoardThemeColors Colors { get; private set; }

    /// <summary>按 token id 索引的 13 个刷子；每次重算都是新实例，窗口拿到同一批。</summary>
    public IReadOnlyDictionary<string, SolidColorBrush> Brushes { get; private set; }

    /// <summary>主题解析结果变化。可能在后台线程触发；消费者在 UI 线程上用。</summary>
    public event EventHandler? Changed;

    public void Refresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }

        var colors = ExamBoardThemeResolver.Resolve(_configHandler.Data.ExamBoardTheme, LookupFollowAppColor);
        var brushes = CreateBrushes(colors);

        Colors = colors;
        Brushes = brushes;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleRefresh()
    {
        // 配置变化可能连发多次（批量写、云端恢复）；合并到下一拍只算一次。
        if (_refreshPending)
            return;

        _refreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshPending = false;
            Refresh();
        });
    }

    private static Dictionary<string, SolidColorBrush> CreateBrushes(ExamBoardThemeColors colors) =>
        ExamBoardThemeTokens.All.ToDictionary(
            token => token.Id,
            token => new SolidColorBrush(colors.Get(token)),
            StringComparer.Ordinal);

    private static Color? LookupFollowAppColor(ExamBoardThemeToken token)
    {
        if (Application.Current is not { } application)
            return null;

        if (!FollowAppResourceKeys.TryGetValue(token.Id, out var key) || string.IsNullOrEmpty(key))
            return null;

        if (!application.TryGetResource(key, application.ActualThemeVariant, out var resource))
            return null;

        // Fluent 解析出来的常是 ImmutableSolidColorBrush，不能写 is SolidColorBrush。
        return resource is ISolidColorBrush brush ? brush.Color : null;
    }

    private void AttachConfig(ExamBoardThemeConfig? config)
    {
        if (ReferenceEquals(config, _observedConfig))
            return;

        if (_observedConfig is not null)
            _observedConfig.PropertyChanged -= OnConfigPropertyChanged;

        _observedConfig = config;
        if (_observedConfig is not null)
            _observedConfig.PropertyChanged += OnConfigPropertyChanged;

        AttachPalette(config?.Palette);
    }

    private void AttachPalette(ExamBoardThemeOverrides? palette)
    {
        if (ReferenceEquals(palette, _observedPalette))
            return;

        if (_observedPalette is not null)
            _observedPalette.PropertyChanged -= OnPalettePropertyChanged;

        _observedPalette = palette;
        if (_observedPalette is not null)
            _observedPalette.PropertyChanged += OnPalettePropertyChanged;
    }

    private void OnConfigPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExamBoardThemeConfig.Palette))
            AttachPalette(_observedConfig?.Palette);

        ScheduleRefresh();
    }

    private void OnPalettePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        ScheduleRefresh();

    private void OnConfigReloaded(object? sender, EventArgs e)
    {
        if (_observedConfig is not null)
            _observedConfig.PropertyChanged -= OnConfigPropertyChanged;
        if (_observedPalette is not null)
            _observedPalette.PropertyChanged -= OnPalettePropertyChanged;

        _observedConfig = null;
        _observedPalette = null;
        AttachConfig(_configHandler.Data.ExamBoardTheme);
        ScheduleRefresh();
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        // 预设/自定义底为预设时解析不依赖 Fluent，但统一重算一次最省心。
        ScheduleRefresh();
    }

    public void Dispose()
    {
        _configHandler.Reloaded -= OnConfigReloaded;
        if (_observedConfig is not null)
            _observedConfig.PropertyChanged -= OnConfigPropertyChanged;
        if (_observedPalette is not null)
            _observedPalette.PropertyChanged -= OnPalettePropertyChanged;
        if (Application.Current is { } application)
            application.ActualThemeVariantChanged -= OnActualThemeVariantChanged;

        _logger.LogDebug("考试看板主题服务已释放");
    }
}