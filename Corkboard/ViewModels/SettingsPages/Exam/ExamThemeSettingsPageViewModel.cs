using System.ComponentModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Exam.Theme;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;
using Corkboard.Helpers;
using Corkboard.Services.Exam;
using Corkboard.ViewModels;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.ViewModels.SettingsPages.Exam;

/// <summary>
///     「看板主题」设置页：考试看板的 13 个语义颜色从哪来（跟随应用外观 / 预设 / 自定义），
///     以及自定义时每一项颜色怎么改。
///     <para>
///         数据模型只有一份（<see cref="ExamBoardThemeConfig" />）：来源单选写 <c>Source</c>（预设再写 <c>PresetId</c>），
///         「自定义基于」写 <c>CustomBase</c>，13 项颜色是 <c>Palette</c> 里的可空覆盖（null = 沿用底）。
///         界面不再另存一份“界面主题”，所有控件都读写同一份配置，所以看板窗口与页面永远同步。
///     </para>
///     <para>
///         颜色是草稿（<see cref="ExamThemeTokenRow.DraftColor" />）：拖动光谱时只改草稿，停手 400ms 才写进配置，
///         离页时 <see cref="CommitAll" /> 把最后一次补上 —— <see cref="MainConfigHandler" /> 每变一个属性就落一次盘，
///         直接双向绑配置等于拖一路写几十遍 <c>settings.json</c>。
///         在跟随应用外观 / 预设下改任意一项会自动切到自定义，并把「底」保持成原来的来源（见 <see cref="CommitRow" />）。
///     </para>
/// </summary>
public partial class ExamThemeSettingsPageViewModel : ViewModelBase
{
    /// <summary>颜色停手后多久落盘；与其它设置页的颜色/滑杆保持同一个手感。</summary>
    private static readonly TimeSpan CommitDelay = TimeSpan.FromMilliseconds(400);

    private readonly MainConfigHandler _configHandler;
    private readonly ExamBoardThemeService _themeService;
    private readonly Dictionary<string, DebounceTimer> _timers = new(StringComparer.Ordinal);

    private ExamBoardThemeConfig? _observedSettings;
    private ExamBoardThemeOverrides? _observedPalette;
    private bool _isAttached;
    private bool _isSyncing;
    private bool _isWritingConfig;

    public ExamThemeSettingsPageViewModel(MainConfigHandler configHandler, ExamBoardThemeService themeService)
        : base(configHandler)
    {
        _configHandler = configHandler;
        _themeService = themeService;

        foreach (var token in ExamBoardThemeTokens.All)
            _timers[token.Id] = new DebounceTimer(CommitDelay);

        SourceOptions = BuildSourceOptions();
        CustomBaseOptions = BuildBaseOptions();
        TokenGroups = BuildTokenGroups();
        Rows = TokenGroups.SelectMany(group => group.Rows).ToArray();

        RefreshDrafts(force: true);
        RefreshSourceSelection();
    }

    /// <summary>当前主题配置。每次现取 <see cref="MainConfigHandler.Data" />：云端恢复会整个换掉配置对象。</summary>
    private ExamBoardThemeConfig Settings => _configHandler.Data.ExamBoardTheme;

    /// <summary>13 项覆盖色的宿主；行上的“已自定义 / 重置”状态直接读它。</summary>
    internal ExamBoardThemeOverrides Palette => Settings.Palette;

    /// <summary>来源选项：跟随应用外观、5 套预设、自定义。顺序即界面顺序。</summary>
    public IReadOnlyList<ExamThemeSourceOption> SourceOptions { get; }

    /// <summary>「自定义基于」下拉：跟随应用外观 + 5 套预设。</summary>
    public IReadOnlyList<ExamThemeBaseOption> CustomBaseOptions { get; }

    /// <summary>13 项颜色按用途分成四组，每组一张可折叠卡。</summary>
    public IReadOnlyList<ExamThemeTokenGroup> TokenGroups { get; }

    /// <summary>平铺的 13 行，供整组刷新时遍历。</summary>
    public IReadOnlyList<ExamThemeTokenRow> Rows { get; }

    /// <summary>草稿刷新等内部同步期间为真：此时行上的颜色变化不触发落盘、单选变化不写配置。</summary>
    internal bool IsSyncing => _isSyncing || _isWritingConfig;

    /// <summary>「自定义基于」下拉的选中项。读配置算，写时同时写 <c>CustomBase</c> 与（预设时）<c>PresetId</c>。</summary>
    public ExamThemeBaseOption SelectedCustomBaseOption
    {
        get
        {
            var config = Settings;
            return CustomBaseOptions.FirstOrDefault(option => option.Matches(config.CustomBase, config.PresetId))
                   ?? CustomBaseOptions[0];
        }
        set
        {
            if (value is null || IsSyncing || !_isAttached)
                return;

            var config = Settings;
            if (value.Matches(config.CustomBase, config.PresetId))
                return;

            CancelAllTimers();
            _isWritingConfig = true;
            try
            {
                config.CustomBase = value.Kind;
                if (value.Kind == ExamBoardThemeBase.Preset && value.PresetId is { } presetId)
                    config.PresetId = presetId;
            }
            finally
            {
                _isWritingConfig = false;
            }

            _themeService.Refresh();
            RefreshDrafts(force: true);
            OnPropertyChanged();
        }
    }

    /// <summary>挂上主题服务与配置的订阅（页面进入可视树时调）。重复调用不会重复挂。</summary>
    public void Attach()
    {
        // 页面是 transient、配置与服务都是单例：订阅必须成对，见 Detach。
        if (_isAttached)
            return;

        _isAttached = true;
        AttachConfig();
        _themeService.Changed += OnThemeServiceChanged;
        _configHandler.Reloaded += OnConfigReloaded;

        RefreshDrafts(force: true);
        RefreshSourceSelection();
    }

    /// <summary>断开订阅（页面离开可视树时调）。</summary>
    public void Detach()
    {
        if (!_isAttached)
            return;

        _isAttached = false;
        _themeService.Changed -= OnThemeServiceChanged;
        _configHandler.Reloaded -= OnConfigReloaded;
        DetachConfig();

        foreach (var timer in _timers.Values)
            timer.Cancel();
    }

    /// <summary>把还压在草稿里的颜色立刻写进配置（离开页面时调）。</summary>
    public void CommitAll()
    {
        foreach (var timer in _timers.Values)
            timer.Flush();
    }

    /// <summary>
    ///     「恢复默认主题」：来源与自定义底都回到跟随应用外观、预设回默认预设。
    ///     <para>
    ///         按方案稿 §3.6 只动这三项、不主动清 <c>Palette</c>（覆盖色留在盘上，只是跟随外观时解析不到）。
    ///         想连覆盖色一起清掉用下面的「恢复所有颜色」。
    ///     </para>
    /// </summary>
    [RelayCommand]
    private void ResetTheme()
    {
        CancelAllTimers();
        _isWritingConfig = true;
        try
        {
            var config = Settings;
            config.Source = ExamBoardThemeSource.FollowApp;
            config.CustomBase = ExamBoardThemeBase.FollowApp;
            config.PresetId = ExamBoardThemeConfig.DefaultPresetId;
        }
        finally
        {
            _isWritingConfig = false;
        }

        _themeService.Refresh();
        RefreshDrafts(force: true);
    }

    /// <summary>「恢复所有颜色」：清空 13 项覆盖；来源与自定义底保持不动。</summary>
    [RelayCommand]
    private void ResetAllColors()
    {
        CancelAllTimers();
        _isWritingConfig = true;
        try
        {
            var palette = Settings.Palette;
            foreach (var token in ExamBoardThemeTokens.All)
                token.SetOverride(palette, null);
        }
        finally
        {
            _isWritingConfig = false;
        }

        _themeService.Refresh();
        RefreshDrafts(force: true);
    }

    /// <summary>单选「主题来源」被选中时调（由选项自己转发；内部同步时为假动作，见 <see cref="IsSyncing" />）。</summary>
    internal void OnSourceOptionSelected(ExamThemeSourceOption option)
    {
        if (IsSyncing || !_isAttached)
            return;

        CancelAllTimers();
        _isWritingConfig = true;
        try
        {
            var config = Settings;
            switch (option.Kind)
            {
                case ExamThemeSourceKind.Preset:
                    config.Source = ExamBoardThemeSource.Preset;
                    if (option.Preset is { } preset)
                        config.PresetId = preset.Id;
                    break;
                case ExamThemeSourceKind.Custom:
                    config.Source = ExamBoardThemeSource.Custom;
                    break;
                default:
                    config.Source = ExamBoardThemeSource.FollowApp;
                    break;
            }
        }
        finally
        {
            _isWritingConfig = false;
        }

        // 立即重算：主题服务平时是 400ms 后异步刷，单选框点下去要让颜色当场变。
        _themeService.Refresh();
        RefreshDrafts(force: true);
        RefreshSourceSelection();
    }

    /// <summary>某一项颜色的草稿变了：排一次停手后落盘，并即时更新看板上的刷子（主题服务会在提交时重算）。</summary>
    internal void OnTokenDraftChanged(ExamThemeTokenRow row)
    {
        if (IsSyncing || !_isAttached)
            return;

        _timers[row.Token.Id].Schedule(() => CommitRow(row));
    }

    /// <summary>
    ///     把某一行的草稿写进配置。
    ///     <para>
    ///         如果当前来源不是自定义，先自动切到自定义，并把「底」保持成原来的来源
    ///         （原本是预设就继续用同一套预设当底）——用户只是改了其中一个颜色，不该把其它颜色一起推倒。
    ///     </para>
    /// </summary>
    private void CommitRow(ExamThemeTokenRow row)
    {
        if (!_isAttached)
            return;

        _isWritingConfig = true;
        try
        {
            var config = Settings;
            if (config.Source != ExamBoardThemeSource.Custom)
            {
                config.CustomBase = config.Source == ExamBoardThemeSource.Preset
                    ? ExamBoardThemeBase.Preset
                    : ExamBoardThemeBase.FollowApp;
                config.Source = ExamBoardThemeSource.Custom;
            }

            row.Token.SetOverride(config.Palette, row.DraftColor);
        }
        finally
        {
            _isWritingConfig = false;
        }

        row.RefreshOverrideState();
        RefreshSourceSelection();
        _themeService.Refresh();
    }

    /// <summary>某一行点「重置这一项」：清掉该 token 的覆盖，草稿回到当前底色的解析结果。</summary>
    internal void ResetToken(ExamThemeTokenRow row)
    {
        _timers[row.Token.Id].Cancel();

        _isWritingConfig = true;
        try
        {
            row.Token.SetOverride(Settings.Palette, null);
        }
        finally
        {
            _isWritingConfig = false;
        }

        row.RefreshOverrideState();
        _themeService.Refresh();

        // 这一行的拾色器可能还开着：跳过待提交的整组刷新，单独把它对齐到新的底色。
        _isSyncing = true;
        try
        {
            row.RefreshFromConfig(_themeService.Colors.Get(row.Token));
        }
        finally
        {
            _isSyncing = false;
        }
    }

    /// <summary>把草稿整体对齐到当前解析结果。正在拾色或还有待提交的行不覆盖，免得跟用户抢。</summary>
    private void RefreshDrafts(bool force)
    {
        _isSyncing = true;
        try
        {
            foreach (var row in Rows)
            {
                if (!force && (row.IsPickerOpen || _timers[row.Token.Id].IsPending))
                    continue;

                row.RefreshFromConfig(_themeService.Colors.Get(row.Token));
            }
        }
        finally
        {
            _isSyncing = false;
        }

        RefreshSourceSelection();
    }

    /// <summary>按当前配置刷新 7 个来源单选的选中状态（不写回配置）。</summary>
    private void RefreshSourceSelection()
    {
        var config = Settings;

        _isSyncing = true;
        try
        {
            foreach (var option in SourceOptions)
                option.SetSelected(option.Matches(config.Source, config.PresetId));
        }
        finally
        {
            _isSyncing = false;
        }

        OnPropertyChanged(nameof(SelectedCustomBaseOption));
    }

    private void CancelAllTimers()
    {
        foreach (var timer in _timers.Values)
            timer.Cancel();
    }

    private void OnThemeServiceChanged(object? sender, EventArgs e)
    {
        if (!_isAttached)
            return;

        RefreshDrafts(force: false);
    }

    private void OnConfigReloaded(object? sender, EventArgs e)
    {
        if (!_isAttached)
            return;

        // 云端恢复会把配置对象整个换掉：旧对象的订阅跟着旧对象一起丢掉，重挂新的。
        AttachConfig();
        _themeService.Refresh();
        RefreshDrafts(force: true);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isWritingConfig || !_isAttached)
            return;

        if (e.PropertyName is nameof(ExamBoardThemeConfig.Palette) or null)
        {
            AttachConfig();
            _themeService.Refresh();
        }
        else if (e.PropertyName is nameof(ExamBoardThemeConfig.Source)
                 or nameof(ExamBoardThemeConfig.CustomBase)
                 or nameof(ExamBoardThemeConfig.PresetId))
        {
            _themeService.Refresh();
        }

        RefreshDrafts(force: false);
    }

    private void OnPaletteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isWritingConfig || !_isAttached)
            return;

        foreach (var row in Rows)
            row.RefreshOverrideState();

        RefreshDrafts(force: false);
    }

    private void AttachConfig()
    {
        DetachConfig();

        _observedSettings = Settings;
        _observedSettings.PropertyChanged += OnSettingsChanged;

        _observedPalette = _observedSettings.Palette;
        if (_observedPalette is not null)
            _observedPalette.PropertyChanged += OnPaletteChanged;
    }

    private void DetachConfig()
    {
        if (_observedSettings is not null)
            _observedSettings.PropertyChanged -= OnSettingsChanged;

        if (_observedPalette is not null)
            _observedPalette.PropertyChanged -= OnPaletteChanged;

        _observedSettings = null;
        _observedPalette = null;
    }

    private IReadOnlyList<ExamThemeSourceOption> BuildSourceOptions()
    {
        var options = new List<ExamThemeSourceOption>
        {
            new(this, ExamThemeSourceKind.FollowApp,
                CR.Settings_Exam_Theme_Source_FollowApp,
                CR.Settings_Exam_Theme_Source_FollowAppDescription)
        };

        foreach (var preset in ExamBoardThemePresets.All)
        {
            options.Add(new ExamThemeSourceOption(this, ExamThemeSourceKind.Preset,
                Text(preset.NameKey), Text(preset.DescriptionKey), preset));
        }

        options.Add(new ExamThemeSourceOption(this, ExamThemeSourceKind.Custom,
            CR.Settings_Exam_Theme_Source_Custom,
            CR.Settings_Exam_Theme_Source_CustomDescription));

        return options;
    }

    private static IReadOnlyList<ExamThemeBaseOption> BuildBaseOptions()
    {
        var options = new List<ExamThemeBaseOption>
        {
            new(ExamBoardThemeBase.FollowApp, CR.Settings_Exam_Theme_CustomBase_FollowApp)
        };

        foreach (var preset in ExamBoardThemePresets.All)
            options.Add(new ExamThemeBaseOption(ExamBoardThemeBase.Preset, Text(preset.NameKey), preset));

        return options;
    }

    /// <summary>
    ///     13 项按用途分四组：底色与文字 / 考试状态 / 强调 / 长按退出。
    ///     顺序与 <see cref="ExamBoardThemeTokens.All" /> 一致，不在这里另排。
    /// </summary>
    private IReadOnlyList<ExamThemeTokenGroup> BuildTokenGroups()
    {
        var byId = ExamBoardThemeTokens.All.ToDictionary(token => token.Id, StringComparer.Ordinal);

        ExamThemeTokenRow[] Pick(params string[] ids) =>
            ids.Select(id => CreateRow(byId[id])).ToArray();

        return
        [
            new ExamThemeTokenGroup(CR.Settings_Exam_Theme_Group_Text,
                Pick("surface", "text_primary", "text_secondary", "text_tertiary", "subtitle", "divider")),

            new ExamThemeTokenGroup(CR.Settings_Exam_Theme_Group_Status,
                Pick("upcoming", "in_progress", "urgent")),

            new ExamThemeTokenGroup(CR.Settings_Exam_Theme_Group_Highlight,
                Pick("highlight")),

            new ExamThemeTokenGroup(CR.Settings_Exam_Theme_Group_Exit,
                Pick("exit_track", "exit_ring_start", "exit_ring_end"))
        ];
    }

    private ExamThemeTokenRow CreateRow(ExamBoardThemeToken token) =>
        new(this, token, Text(token.NameKey), _themeService.Colors.Get(token));

    /// <summary>按资源键现取文案：token 与预设的名字都只有键（Core 层不想依赖界面资源类）。</summary>
    private static string Text(string key) => CR.ResourceManager.GetString(key, CR.Culture) ?? key;
}

/// <summary>「主题来源」单选的一个选项：跟随应用外观 / 某套预设 / 自定义。</summary>
public sealed partial class ExamThemeSourceOption : ObservableObject
{
    private readonly ExamThemeSettingsPageViewModel _owner;

    public ExamThemeSourceOption(
        ExamThemeSettingsPageViewModel owner,
        ExamThemeSourceKind kind,
        string displayName,
        string description,
        ExamBoardThemePreset? preset = null)
    {
        _owner = owner;
        Kind = kind;
        DisplayName = displayName;
        Description = description;
        Preset = preset;
    }

    public ExamThemeSourceKind Kind { get; }

    /// <summary>预设选项对应的预设；其它选项为 null。</summary>
    public ExamBoardThemePreset? Preset { get; }

    public string? PresetId => Preset?.Id;

    public string DisplayName { get; }

    public string Description { get; }

    [ObservableProperty] private bool _isSelected;

    /// <summary>配置里当前来源是不是这一项。</summary>
    public bool Matches(ExamBoardThemeSource source, string presetId) => Kind switch
    {
        ExamThemeSourceKind.FollowApp => source == ExamBoardThemeSource.FollowApp,
        ExamThemeSourceKind.Custom => source == ExamBoardThemeSource.Custom,
        ExamThemeSourceKind.Preset => source == ExamBoardThemeSource.Preset
                                       && string.Equals(PresetId, presetId, StringComparison.Ordinal),
        _ => false
    };

    /// <summary>由 ViewModel 同步选中态（绕过点击回调，不会反过来写配置）。</summary>
    internal void SetSelected(bool value)
    {
        if (IsSelected != value)
            IsSelected = value;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            _owner.OnSourceOptionSelected(this);
    }
}

/// <summary>「自定义基于」下拉的一项：跟随应用外观或某套预设。</summary>
public sealed class ExamThemeBaseOption
{
    public ExamThemeBaseOption(ExamBoardThemeBase kind, string displayName, ExamBoardThemePreset? preset = null)
    {
        Kind = kind;
        DisplayName = displayName;
        Preset = preset;
    }

    public ExamBoardThemeBase Kind { get; }

    public ExamBoardThemePreset? Preset { get; }

    public string? PresetId => Preset?.Id;

    public string DisplayName { get; }

    public bool Matches(ExamBoardThemeBase kind, string presetId) => Kind switch
    {
        ExamBoardThemeBase.FollowApp => kind == ExamBoardThemeBase.FollowApp,
        ExamBoardThemeBase.Preset => kind == ExamBoardThemeBase.Preset
                                     && string.Equals(PresetId, presetId, StringComparison.Ordinal),
        _ => false
    };
}

/// <summary>13 项颜色里的一行：名称、当前草稿色、是否已有覆盖、拾色器开没开。</summary>
public sealed partial class ExamThemeTokenRow : ObservableObject
{
    private readonly ExamThemeSettingsPageViewModel _owner;

    public ExamThemeTokenRow(
        ExamThemeSettingsPageViewModel owner,
        ExamBoardThemeToken token,
        string name,
        Color initialColor)
    {
        _owner = owner;
        Token = token;
        Name = name;

        // 直接写字段：构造期还没挂订阅，别让这次初始化排出一堆“提交”。
        _draftColor = initialColor;
    }

    public ExamBoardThemeToken Token { get; }

    public string Name { get; }

    /// <summary>界面上的草稿色；拖动光谱时只改它，停手 400ms 才提交。</summary>
    [ObservableProperty] private Color _draftColor;

    /// <summary>页内展开的取色面板是否打开。</summary>
    [ObservableProperty] private bool _isPickerOpen;

    /// <summary>色块填充，跟着草稿实时变。</summary>
    public IBrush SwatchBrush => new SolidColorBrush(DraftColor);

    /// <summary>色值文本：不透明写 <c>#RRGGBB</c>，带 alpha 写磁盘同款 <c>#RRGGBBAA</c>（alpha 在后）。</summary>
    public string HexText => FormatHex(DraftColor);

    /// <summary>这一项是否已有自定义覆盖（「重置这一项」按钮的可用状态）。</summary>
    public bool HasOverride => Token.GetOverride(_owner.Palette) is not null;

    [RelayCommand]
    private void TogglePicker() => IsPickerOpen = !IsPickerOpen;

    [RelayCommand]
    private void ResetColor() => _owner.ResetToken(this);

    partial void OnDraftColorChanged(Color value)
    {
        OnPropertyChanged(nameof(SwatchBrush));
        OnPropertyChanged(nameof(HexText));

        if (!_owner.IsSyncing)
            _owner.OnTokenDraftChanged(this);
    }

    /// <summary>把草稿对齐到给定颜色（由 ViewModel 在配置/主题变化时调）。</summary>
    internal void RefreshFromConfig(Color color)
    {
        if (DraftColor != color)
            DraftColor = color;
    }

    /// <summary>覆盖状态变了（提交 / 重置 / 外部改动）时刷新「重置」按钮。</summary>
    internal void RefreshOverrideState() => OnPropertyChanged(nameof(HasOverride));

    internal static string FormatHex(Color color) =>
        color.A == byte.MaxValue
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
}

/// <summary>一组颜色（一张可折叠卡）：底色与文字 / 考试状态 / 强调 / 长按退出。</summary>
public sealed class ExamThemeTokenGroup
{
    public ExamThemeTokenGroup(string title, IReadOnlyList<ExamThemeTokenRow> rows)
    {
        Title = title;
        Rows = rows;
    }

    public string Title { get; }

    public IReadOnlyList<ExamThemeTokenRow> Rows { get; }
}

/// <summary>来源单选的三种语义。</summary>
public enum ExamThemeSourceKind
{
    FollowApp,
    Preset,
    Custom
}