using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corkboard.Core.Handwriting;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using Corkboard.ViewModels.SettingsPages.RichText;

namespace Corkboard.ViewModels.SettingsPages.Board;

/// <summary>
///     「手写」设置页：默认笔的颜色 / 粗细、橡皮粗细，以及手写工具栏上那排常用色。
///     <para>
///         控件一律绑草稿属性，不直接双向绑配置：<c>ConfigHandlerBase</c> 每次属性变化都会落一次盘，
///         而取色器拖光谱、数字框上下箭头都是「一路动一路变」，直接绑会把 <c>settings.json</c> 写爆。
///         草稿由 400ms 防抖 + 输入框失焦 + 离开页面三处提交（写法照
///         <see cref="BoardSettingsPageViewModel" />）。
///     </para>
///     <para>
///         常用色板存在配置的 <c>ObservableCollection&lt;Color&gt;</c> 里，而集合元素不会被配置句柄订阅，
///         所以增删挪与改色之后都要显式 Save 一次。
///     </para>
/// </summary>
public partial class HandwritingSettingsPageViewModel : ViewModelBase
{
    /// <summary>取色器 / 色板停手多久才把颜色写进配置。</summary>
    private static readonly TimeSpan ColorCommitDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>数字框停手多久才把笔宽 / 橡皮宽写进配置。</summary>
    private static readonly TimeSpan WidthCommitDelay = TimeSpan.FromMilliseconds(400);

    private readonly MainConfigHandler _configHandler;

    private Color? _penColorDraft;
    private DispatcherTimer? _penColorCommitTimer;

    private decimal? _penWidthDraft;
    private decimal? _eraserWidthDraft;
    private DispatcherTimer? _widthCommitTimer;

    /// <summary>正在把夹过 / 规范化过的值写回草稿：这时别再排防抖计时，否则会白提交一轮。</summary>
    private bool _syncingWidthDrafts;

    /// <summary>
    ///     取色器拖一次光谱会发上百次变更，攒一下再落盘。按色块下标分别计时，
    ///     这样连改多个色块时不会互相把对方的计时顶掉。
    /// </summary>
    private readonly Dictionary<int, DispatcherTimer> _paletteCommitTimers = [];

    public HandwritingSettingsPageViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        _configHandler = configHandler;

        // 数字框从配置起步（越界的老配置按合法区间夹一次，界面显示的就是真的会用的值）。
        _penWidthDraft = (decimal)HandwritingStyle.ClampPenWidth(Settings.HandwritingPenWidth);
        _eraserWidthDraft = (decimal)HandwritingStyle.ClampEraserWidth(Settings.HandwritingEraserWidth);

        ReloadPaletteItems();
    }

    /// <summary>作业板模块的配置（手写的几项都在里面）。</summary>
    public BoardSettingsConfig Settings => Config.BoardSettings;

    #region 笔宽与橡皮宽

    /// <summary>默认笔粗的输入草稿（DIP）。</summary>
    public decimal? PenWidthDraft
    {
        get => _penWidthDraft ??= (decimal)HandwritingStyle.ClampPenWidth(Settings.HandwritingPenWidth);
        set
        {
            if (_penWidthDraft == value)
                return;

            _penWidthDraft = value;
            OnPropertyChanged();

            if (!_syncingWidthDrafts)
                ScheduleWidthCommit();
        }
    }

    /// <summary>默认橡皮粗细的输入草稿（DIP）。</summary>
    public decimal? EraserWidthDraft
    {
        get => _eraserWidthDraft ??= (decimal)HandwritingStyle.ClampEraserWidth(Settings.HandwritingEraserWidth);
        set
        {
            if (_eraserWidthDraft == value)
                return;

            _eraserWidthDraft = value;
            OnPropertyChanged();

            if (!_syncingWidthDrafts)
                ScheduleWidthCommit();
        }
    }

    /// <summary>把两个宽度草稿写回配置。手输越界值时夹回合法区间，并把夹过的值同步回界面。</summary>
    public void CommitWidths()
    {
        CancelWidthCommit();

        _syncingWidthDrafts = true;
        try
        {
            var pen = HandwritingStyle.ClampPenWidth(
                PenWidthDraft is { } penDraft ? (double)penDraft : Settings.HandwritingPenWidth);

            if (Math.Abs(Settings.HandwritingPenWidth - pen) > double.Epsilon)
                Settings.HandwritingPenWidth = pen;

            PenWidthDraft = (decimal)pen;

            var eraser = HandwritingStyle.ClampEraserWidth(
                EraserWidthDraft is { } eraserDraft ? (double)eraserDraft : Settings.HandwritingEraserWidth);

            if (Math.Abs(Settings.HandwritingEraserWidth - eraser) > double.Epsilon)
                Settings.HandwritingEraserWidth = eraser;

            EraserWidthDraft = (decimal)eraser;
        }
        finally
        {
            _syncingWidthDrafts = false;
        }
    }

    private void ScheduleWidthCommit()
    {
        CancelWidthCommit();

        _widthCommitTimer = new DispatcherTimer { Interval = WidthCommitDelay };
        _widthCommitTimer.Tick += (_, _) => CommitWidths();
        _widthCommitTimer.Start();
    }

    private void CancelWidthCommit()
    {
        _widthCommitTimer?.Stop();
        _widthCommitTimer = null;
    }

    #endregion

    #region 默认笔色

    /// <summary>
    ///     默认笔色的取色器草稿。理由同笔宽：每帧都写配置会把 <c>settings.json</c> 写爆。
    ///     配置里为空表示「跟随主题」，这时取色器整块隐藏。
    /// </summary>
    public Color PenColorDraft
    {
        get => _penColorDraft ?? Settings.HandwritingPenColor ?? ResolveThemeForeground();
        set
        {
            if (_penColorDraft == value)
                return;

            _penColorDraft = value;
            OnPropertyChanged();
            ScheduleColorCommit(value);
        }
    }

    /// <summary>默认笔色是否跟随主题（配置里为空就是跟随），对应设置卡上的开关。</summary>
    public bool FollowThemeColor
    {
        get => Settings.HandwritingPenColor is null;
        set
        {
            if (value == FollowThemeColor)
                return;

            CancelColorCommit();

            if (value)
            {
                Settings.HandwritingPenColor = null;
            }
            else
            {
                // 从「跟随主题」切到自定义时，起点取主题当前的前景色——
                // 直接给黑色的话，深色模式下笔迹会糊在底板里看不见。
                _penColorDraft ??= ResolveThemeForeground();
                Settings.HandwritingPenColor = _penColorDraft;
                OnPropertyChanged(nameof(PenColorDraft));
            }

            // 取色器只在「自定义」时显示，换档位要通知这一条。
            OnPropertyChanged();
        }
    }

    /// <summary>主题资源里没有「前景色」这个设置项，只能按当前主题取一次。</summary>
    private static Color ResolveThemeForeground()
    {
        // 条件访问不能带 out 参数，所以先取实例再查资源。
        if (Application.Current is { } application
            && application.TryGetResource("TextFillColorPrimaryBrush", null, out var resource)
            && resource is ISolidColorBrush brush)
        {
            return brush.Color;
        }

        return HandwritingStyle.DefaultColors[0];
    }

    private void ScheduleColorCommit(Color value)
    {
        _penColorCommitTimer?.Stop();

        _penColorCommitTimer = new DispatcherTimer { Interval = ColorCommitDelay };
        _penColorCommitTimer.Tick += (_, _) =>
        {
            CancelColorCommit();

            // 已经切回「跟随主题」了：别再把手里的颜色写回配置。
            if (!FollowThemeColor)
                Settings.HandwritingPenColor = value;
        };
        _penColorCommitTimer.Start();
    }

    private void CancelColorCommit()
    {
        _penColorCommitTimer?.Stop();
        _penColorCommitTimer = null;
    }

    /// <summary>把还压在草稿里的颜色立刻写进配置，并停掉待触发的防抖计时。</summary>
    public void FlushColorDraft()
    {
        CancelColorCommit();

        if (_penColorDraft is { } draft && !FollowThemeColor && Settings.HandwritingPenColor != draft)
            Settings.HandwritingPenColor = draft;
    }

    #endregion

    #region 常用色板

    /// <summary>
    ///     手写工具栏上那排常用色，包成可绑定的行对象给设置页。<c>Color</c> 是结构体，
    ///     直接摆一个 <c>ObservableCollection&lt;Color&gt;</c> 的话取色器改不动它（见
    ///     <see cref="RichTextPaletteColorItem" />）。
    /// </summary>
    public ObservableCollection<RichTextPaletteColorItem> PaletteColors { get; } = [];

    /// <summary>色板到上限了（「新增」按钮据此禁用），上限在 <see cref="HandwritingStyle.MaxColors" /> 里。</summary>
    public bool CanAddPaletteColor => PaletteColors.Count < HandwritingStyle.MaxColors;

    /// <summary>从配置读一次色板，灌进设置页的行对象。</summary>
    private void ReloadPaletteItems()
    {
        PaletteColors.Clear();
        foreach (var color in Settings.ResolveHandwritingColors())
            PaletteColors.Add(new RichTextPaletteColorItem(color, OnPaletteColorItemChanged));

        OnPropertyChanged(nameof(CanAddPaletteColor));
    }

    /// <summary>某一个色块的取色器动了：防抖之后写回配置。</summary>
    private void OnPaletteColorItemChanged(RichTextPaletteColorItem item, Color color)
    {
        var index = PaletteColors.IndexOf(item);
        if (index < 0)
            return;

        SchedulePaletteColorCommit(index, color);
    }

    /// <summary>再加一个常用色。</summary>
    [RelayCommand]
    private void AddPaletteColor()
    {
        if (!CanAddPaletteColor)
            return;

        // 新色从默认色板里挑第一个还没用过的，免得加出来跟已有的撞成一样。
        var used = PaletteColors.Select(item => item.Color).ToList();
        var candidate = HandwritingStyle.DefaultColors.FirstOrDefault(color => !used.Contains(color));

        PaletteColors.Add(new RichTextPaletteColorItem(candidate, OnPaletteColorItemChanged));
        PersistPalette();
    }

    /// <summary>删掉一个常用色。</summary>
    public void RemovePaletteColor(RichTextPaletteColorItem item)
    {
        if (!PaletteColors.Remove(item))
            return;

        PersistPalette();
    }

    /// <summary>把某个常用色往前挪一格。</summary>
    public void MovePaletteColorUp(RichTextPaletteColorItem item) => MovePaletteColor(item, -1);

    /// <summary>把某个常用色往后挪一格。</summary>
    public void MovePaletteColorDown(RichTextPaletteColorItem item) => MovePaletteColor(item, 1);

    private void MovePaletteColor(RichTextPaletteColorItem item, int offset)
    {
        var index = PaletteColors.IndexOf(item);
        var target = index + offset;

        if (index < 0 || target < 0 || target >= PaletteColors.Count)
            return;

        PaletteColors.Move(index, target);

        // 顺序也是配置的一部分，挪完要落盘。
        PersistPalette();
    }

    /// <summary>色块颜色变更的入口：防抖之后把界面上的色板整体写回配置。</summary>
    private void SchedulePaletteColorCommit(int index, Color color)
    {
        if (_paletteCommitTimers.Remove(index, out var pending))
            pending.Stop();

        var timer = new DispatcherTimer { Interval = ColorCommitDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _paletteCommitTimers.Remove(index);

            // 期间可能已经删过色块，下标要先确认还在范围内。
            if (index < PaletteColors.Count)
                PersistPalette();
        };

        _paletteCommitTimers[index] = timer;
        timer.Start();
    }

    /// <summary>把还压在防抖里的色板改动立刻落盘（离开页面 / 保存时调）。</summary>
    public void FlushPaletteCommit()
    {
        if (_paletteCommitTimers.Count == 0)
            return;

        foreach (var timer in _paletteCommitTimers.Values)
            timer.Stop();

        _paletteCommitTimers.Clear();
        PersistPalette();
    }

    /// <summary>
    ///     把界面上的色板写回配置并落盘。<see cref="BoardSettingsConfig.ApplyHandwritingColors" />
    ///     会夹取 / 去重 / 截断，所以写完要跟界面比一次：配置真的被改过就重建行对象，别让界面显示的和存的不一样。
    /// </summary>
    private void PersistPalette()
    {
        var before = PaletteColors.Select(item => item.Color).ToList();

        Settings.ApplyHandwritingColors(before);
        OnPropertyChanged(nameof(CanAddPaletteColor));
        _configHandler.Save();

        var after = Settings.ResolveHandwritingColors();
        if (!before.SequenceEqual(after))
            ReloadPaletteItems();
    }

    #endregion

    /// <summary>设置页所有编辑的收口：草稿提交 + 防抖里的颜色落盘。输入框失焦与离开页面时都会调。</summary>
    public void SaveAll()
    {
        CommitWidths();
        FlushColorDraft();
        FlushPaletteCommit();
        CancelWidthCommit();
    }
}
