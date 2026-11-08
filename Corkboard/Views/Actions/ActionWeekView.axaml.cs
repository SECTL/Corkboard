using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Corkboard.Core.Actions;
using Corkboard.Core.Actions.Schedule;
using Corkboard.Core.Icons;
using Corkboard.ViewModels.Actions;
using LR = Corkboard.Langs.Action.Resources;

namespace Corkboard.Views.Actions;

/// <summary>
///     周视图：左边 56px 时刻栏 + 七列 × 24 小时的时间轴。
///     <para>
///         日程块不是「一列一条」的手搓卡片：按 <c>Start</c>/<c>End</c> 换算成
///         <c>Canvas</c> 里的绝对 top/height，同一天重叠的块并排两列，第三条起折成「+N」。
///         行高与时刻栏常量都在这里，改布局只改这几个数。
///     </para>
/// </summary>
public partial class ActionWeekView : UserControl
{
    /// <summary>一小时在时间轴上的高度（DIP）。</summary>
    private const double HourHeight = 46;

    private const double GutterWidth = 56;

    private const int HourCount = 24;

    /// <summary>默认滚动到的时刻：早上 8 点。</summary>
    private const int DefaultScrollHour = 8;

    private ActionWindowViewModel? _viewModel;
    private bool _rebuildQueued;
    private bool _rebuildScroll;
    private bool _builtOnce;

    public ActionWeekView()
    {
        InitializeComponent();

        WeekBody.SizeChanged += (_, _) => QueueRebuild(false);
        Loaded += (_, _) =>
        {
            Hook();
            QueueRebuild(true);
        };
        Unloaded += (_, _) => Unhook();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Hook();
        QueueRebuild(true);
    }

    private void Hook()
    {
        if (ReferenceEquals(_viewModel, DataContext)) return;

        Unhook();
        _viewModel = DataContext as ActionWindowViewModel;
        if (_viewModel is not null)
            _viewModel.WeekColumns.CollectionChanged += OnWeekColumnsChanged;
    }

    private void Unhook()
    {
        if (_viewModel is not null)
            _viewModel.WeekColumns.CollectionChanged -= OnWeekColumnsChanged;

        _viewModel = null;
    }

    private void OnWeekColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRebuild(true);

    /// <summary>
    ///     一次刷新会把 7 列逐条加进集合（<c>Clear</c> + <c>Add</c>），合并成一帧后再重排，
    ///     否则同一个布局要算八遍。
    /// </summary>
    private void QueueRebuild(bool resetScroll)
    {
        _rebuildScroll |= resetScroll;
        if (_rebuildQueued) return;

        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            var reset = _rebuildScroll;
            _rebuildScroll = false;
            Rebuild(reset);
        }, DispatcherPriority.Loaded);
    }

    private void Rebuild(bool resetScroll)
    {
        WeekBody.Children.Clear();
        WeekBody.ColumnDefinitions.Clear();
        WeekBody.ColumnDefinitions.Add(new ColumnDefinition(GutterWidth, GridUnitType.Pixel));
        WeekBody.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        var totalHeight = HourHeight * HourCount;
        WeekBody.Height = totalHeight;

        var gutter = BuildGutter(totalHeight);
        Grid.SetColumn(gutter, 0);
        WeekBody.Children.Add(gutter);

        var columns = _viewModel?.WeekColumns;
        var contentWidth = Math.Max(0, WeekBody.Bounds.Width - GutterWidth);
        var columnWidth = contentWidth > 0 ? contentWidth / 7d : 0;

        var content = new Grid { Height = totalHeight };
        for (var i = 0; i < 7; i++)
        {
            content.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            var column = columns is not null && i < columns.Count ? columns[i] : null;
            var cell = BuildColumn(column, columnWidth, totalHeight);
            Grid.SetColumn(cell, i);
            content.Children.Add(cell);
        }

        Grid.SetColumn(content, 1);
        WeekBody.Children.Add(content);

        if (resetScroll || !_builtOnce)
        {
            _builtOnce = true;
            // 先让这一帧量出真实高度，再滚到早上 8 点。
            Dispatcher.UIThread.Post(
                () => WeekScroll.Offset = new Vector(0, Math.Max(0, DefaultScrollHour * HourHeight - 24)),
                DispatcherPriority.Loaded);
        }
    }

    private Control BuildGutter(double totalHeight)
    {
        var canvas = new Canvas { Width = GutterWidth - 8, Height = totalHeight };

        var brush = Brush("TextFillColorTertiaryBrush", Color.FromArgb(0x99, 0x88, 0x88, 0x88));
        for (var hour = 0; hour < HourCount; hour += 2)
        {
            var label = new TextBlock
            {
                Text = $"{hour:00}:00",
                FontSize = 10,
                Foreground = brush,
                TextAlignment = TextAlignment.Right,
                Width = GutterWidth - 12
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, hour * HourHeight - 7);
            canvas.Children.Add(label);
        }

        return canvas;
    }

    private Control BuildColumn(ActionWeekColumn? column, double columnWidth, double totalHeight)
    {
        var cell = new Border
        {
            ClipToBounds = true,
            Background = column?.IsToday == true
                ? Brush("SubtleFillColorSecondaryBrush", Color.FromArgb(0x12, 0x00, 0x00, 0x00))
                : Brushes.Transparent
        };

        var canvas = new Canvas { ClipToBounds = true, Height = totalHeight };
        cell.Child = canvas;

        AddHourLines(canvas, columnWidth);

        if (column is null) return cell;

        var dayStart = column.Date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        var layout = new List<BlockGeometry>();
        foreach (var item in column.Items)
        {
            var start = item.Start < dayStart ? dayStart : item.Start;
            var rawEnd = item.End ?? item.Start.AddMinutes(30);
            var end = rawEnd > dayEnd ? dayEnd : rawEnd;

            if (item.End is null && (item.Start < dayStart || item.Start >= dayEnd)) continue;
            if (end <= start && item.End is not null) continue;

            var top = (start - dayStart).TotalMinutes / 60d * HourHeight;
            var height = Math.Max(item.IsInterval ? 24 : 20, (end - start).TotalHours * HourHeight - 1);
            if (height <= 0 || top > totalHeight) continue;

            layout.Add(new BlockGeometry(item, top, height));
        }

        layout.Sort((left, right) =>
        {
            var byTop = left.Top.CompareTo(right.Top);
            return byTop != 0 ? byTop : (left.Item.IsInterval ? 1 : 0).CompareTo(right.Item.IsInterval ? 1 : 0);
        });

        AddBlocks(canvas, layout, columnWidth);
        return cell;
    }

    private void AddHourLines(Canvas canvas, double width)
    {
        if (width <= 0) return;

        var hourBrush = Brush("DividerStrokeColorDefaultBrush", Color.FromArgb(0x33, 0x80, 0x80, 0x80));
        var halfBrush = Brush("DividerStrokeColorDefaultBrush", Color.FromArgb(0x18, 0x80, 0x80, 0x80));

        for (var hour = 0; hour < HourCount; hour++)
        {
            var line = new Border { Height = 1, Width = width, Background = hourBrush };
            Canvas.SetLeft(line, 0);
            Canvas.SetTop(line, hour * HourHeight);
            canvas.Children.Add(line);

            if (hour == HourCount - 1) continue;

            var half = new Border { Height = 1, Width = width, Background = halfBrush };
            Canvas.SetLeft(half, 0);
            Canvas.SetTop(half, hour * HourHeight + HourHeight / 2);
            canvas.Children.Add(half);
        }
    }

    /// <summary>
    ///     重叠分组：一簇里最多并排两列，第三块起收进「+N」。
    ///     并排宽度按「簇里用了几条车道」动态算，单独一段仍然占满整列。
    /// </summary>
    private void AddBlocks(Canvas canvas, IReadOnlyList<BlockGeometry> layout, double columnWidth)
    {
        if (layout.Count == 0 || columnWidth <= 0) return;

        var index = 0;
        while (index < layout.Count)
        {
            var clusterEnd = layout[index].Top + layout[index].Height;
            var members = new List<BlockGeometry>();
            var next = index;
            while (next < layout.Count && layout[next].Top < clusterEnd - 0.5)
            {
                members.Add(layout[next]);
                clusterEnd = Math.Max(clusterEnd, layout[next].Top + layout[next].Height);
                next++;
            }

            var laneEnds = new List<double>(2);
            var placed = new List<(BlockGeometry Geometry, int Lane)>();
            var hidden = new List<ActionOccurrenceItem>();

            foreach (var member in members)
            {
                var lane = laneEnds.FindIndex(end => end <= member.Top + 0.5);
                if (lane < 0)
                {
                    if (laneEnds.Count >= 2)
                    {
                        hidden.Add(member.Item);
                        continue;
                    }

                    laneEnds.Add(0);
                    lane = laneEnds.Count - 1;
                }

                laneEnds[lane] = member.Top + member.Height;
                placed.Add((member, lane));
            }

            var laneCount = Math.Max(1, laneEnds.Count);
            var laneWidth = columnWidth / laneCount;

            foreach (var (geometry, lane) in placed)
            {
                var block = BuildBlock(geometry.Item, geometry.Height, laneWidth);
                Canvas.SetLeft(block, lane * laneWidth + 1);
                Canvas.SetTop(block, geometry.Top);
                canvas.Children.Add(block);
            }

            if (hidden.Count > 0)
            {
                var more = new TextBlock
                {
                    Text = string.Format(LR.Action_More, hidden.Count),
                    FontSize = 9.5,
                    Foreground = Brush("TextFillColorSecondaryBrush", Brushes.Gray),
                    Padding = new Thickness(4, 1, 4, 1)
                };
                ToolTip.SetTip(more, string.Join("\n", hidden.Select(entry => entry.Title)));

                var badge = new Border
                {
                    Background = Brush("SubtleFillColorSecondaryBrush", Color.FromArgb(0x22, 0x00, 0x00, 0x00)),
                    CornerRadius = new CornerRadius(3),
                    Child = more,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Canvas.SetLeft(badge, Math.Max(1, columnWidth - 40));
                Canvas.SetTop(badge, Math.Max(0, clusterEnd - 18));
                canvas.Children.Add(badge);
            }

            index = next;
        }
    }

    private Control BuildBlock(ActionOccurrenceItem item, double height, double laneWidth)
    {
        var accent = KindBrush(item);
        var panel = new StackPanel { Spacing = 0 };

        var timeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var time = new TextBlock
        {
            Text = item.IsInterval ? item.RangeText : item.TimeText,
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        timeRow.Children.Add(time);

        var glyphText = TargetGlyph(item);
        if (glyphText.Length > 0)
        {
            var glyph = new TextBlock
            {
                Text = glyphText,
                FontSize = 10,
                FontFamily = IconFont,
                Foreground = accent,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(glyph, 1);
            timeRow.Children.Add(glyph);
        }

        panel.Children.Add(timeRow);

        if (height >= 34)
        {
            var title = new TextBlock
            {
                Text = item.Title,
                FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Foreground = Brush("TextFillColorPrimaryBrush", Brushes.Black)
            };
            panel.Children.Add(title);

            if (height >= 56)
            {
                var summary = new TextBlock
                {
                    Text = item.SummaryText,
                    FontSize = 9.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Foreground = Brush("TextFillColorSecondaryBrush", Brushes.Gray)
                };
                panel.Children.Add(summary);
            }
        }

        var block = new Border
        {
            Width = Math.Max(12, laneWidth - 3),
            Height = height,
            CornerRadius = new CornerRadius(4),
            Background = Brush("SubtleFillColorSecondaryBrush", Color.FromArgb(0x22, 0x00, 0x00, 0x00)),
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = accent,
            Padding = new Thickness(4, 1, 2, 1),
            ClipToBounds = true,
            Child = panel,
            Opacity = item.IsPausedToday ? 0.55 : 1
        };

        ToolTip.SetTip(block, string.IsNullOrEmpty(item.EffectText) ? item.Title : item.EffectText);
        block.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _viewModel?.EditCommand.Execute(item);
            e.Handled = true;
        };

        return block;
    }

    private IBrush KindBrush(ActionOccurrenceItem item)
    {
        if (item.IsRule) return Brush("AccentFillColorDefaultBrush", Color.FromArgb(0xFF, 0x00, 0x78, 0xD4));

        return item.Kind == ScheduleItemKind.DisplayAutoShow
            ? Brush("SystemFillColorSuccessBrush", Color.FromArgb(0xFF, 0x0F, 0x7B, 0x0F))
            : Brush("SystemFillColorCautionBrush", Color.FromArgb(0xFF, 0x9D, 0x5D, 0x00));
    }

    private string TargetGlyph(ActionOccurrenceItem item)
    {
        var targetId = item.Rule?.Steps.FirstOrDefault()?.TargetId
                       ?? item.Item?.TargetIds.FirstOrDefault();

        return targetId switch
        {
            BoardTargetIds.Homework => FluentIcons.BoardFilled,
            BoardTargetIds.ExamBoard => FluentIcons.FullScreenMaximizeFilled,
            BoardTargetIds.DisplayBoard => FluentIcons.WindowFilled,
            BoardTargetIds.DutyBoard => FluentIcons.CalendarFilled,
            _ => ""
        };
    }

    private FontFamily IconFont =>
        this.TryFindResource("FluentSystemIconsResizeable", out var value) && value is FontFamily font
            ? font
            : new FontFamily("Segoe Fluent Icons");

    private IBrush Brush(string key, Color fallback) =>
        this.TryFindResource(key, out var value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, out var value) && value is IBrush brush ? brush : fallback;

    private readonly record struct BlockGeometry(ActionOccurrenceItem Item, double Top, double Height);
}
