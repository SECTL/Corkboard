using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Corkboard.Core.DutyRoster.Import;

namespace Corkboard.Views.Duty;

/// <summary>
///     导入区域选择器：工作表 / 表头行 / 数据行范围 + 表头附近的原始行预览。
///     <para>
///         照 SecRandom-C 的 <c>RosterImportRegionSelector</c> 移植。列范围恒为全部列，行号一律是
///         原表里的 1 基行号；控件只负责选区域，解析与列映射归 <see cref="ViewModels.Duty.DutyImportViewModel" />。
///     </para>
///     <para>
///         ⚠️ 不要在控件里嵌 <c>FASettingsExpander</c>：外层抽屉已经把它们当卡片用，卡片套卡片展开时
///         FluentAvalonia 的展开动画会拿不到 compositor 元素而抛 NullReferenceException。
///     </para>
/// </summary>
public partial class DutyImportRegionSelector : UserControl, INotifyPropertyChanged
{
    /// <summary>原始行预览窗口大小（以表头行为中心最多显示这么多行）。</summary>
    public const int RawPreviewWindowSize = 5;

    /// <summary>表头行下拉里最多列出多少行，避免几万行的表格造几万个选项。</summary>
    public const int HeaderRowOptionLimit = 200;

    private RosterImportGrid _grid = RosterImportGrid.Empty;
    private IReadOnlyList<string> _headerKeywords = [];
    private int? _headerRow;
    private int _lastContentRow;
    private int _rowCountMaximum = 1;
    private decimal? _dataFirstRow;
    private decimal? _dataLastRow;
    private string? _selectedSheet;
    private RosterImportHeaderRowOption? _selectedHeaderRowOption;
    private RosterImportRawRow? _selectedRawRow;
    private bool _hasGrid;
    private bool _hasHeaderRowWarning;
    private bool _isUpdating;
    private IReadOnlyList<string> _sheetOptions = [];
    private IReadOnlyList<RosterImportHeaderRowOption> _headerRowOptions = [];
    private IReadOnlyList<RosterImportRawRow> _rawRows = [];

    public DutyImportRegionSelector()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>表头行、数据行范围或工作表变化后触发；抽屉收到后重裁表格并刷新预览。</summary>
    public event EventHandler? RegionChanged;

    /// <summary>用户切换工作表时触发，参数是选中的工作表名（单表文件下不会触发）。</summary>
    public event EventHandler<string?>? SheetSelectionChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public bool HasGrid
    {
        get => _hasGrid;
        private set => SetField(ref _hasGrid, value);
    }

    public bool HasHeaderRowWarning
    {
        get => _hasHeaderRowWarning;
        private set => SetField(ref _hasHeaderRowWarning, value);
    }

    public bool HasSheetSelection => _sheetOptions.Count > 1;

    public IReadOnlyList<string> SheetOptions
    {
        get => _sheetOptions;
        private set => SetField(ref _sheetOptions, value);
    }

    public string? SelectedSheet
    {
        get => _selectedSheet;
        set
        {
            if (!SetField(ref _selectedSheet, value))
                return;

            if (!_isUpdating)
                SheetSelectionChanged?.Invoke(this, value);
        }
    }

    public IReadOnlyList<RosterImportHeaderRowOption> HeaderRowOptions
    {
        get => _headerRowOptions;
        private set => SetField(ref _headerRowOptions, value);
    }

    public RosterImportHeaderRowOption? SelectedHeaderRowOption
    {
        get => _selectedHeaderRowOption;
        set
        {
            if (SetField(ref _selectedHeaderRowOption, value) && !_isUpdating)
                ApplyHeaderRow(value?.Row, fromUser: true);
        }
    }

    /// <summary>行号上限（decimal 是 NumericUpDown 的绑定类型）；至少 1。</summary>
    public decimal RowCountMaximum => Math.Max(1, _rowCountMaximum);

    public decimal? DataFirstRow
    {
        get => _dataFirstRow;
        set
        {
            var normalized = NormalizeRow(value, isFirst: true);
            if (!SetField(ref _dataFirstRow, normalized) || _isUpdating)
                return;

            if (_dataLastRow is { } last && _dataFirstRow is { } first && last < first)
            {
                _dataLastRow = first;
                OnPropertyChanged(nameof(DataLastRow));
            }

            RaiseRegionChanged();
        }
    }

    public decimal? DataLastRow
    {
        get => _dataLastRow;
        set
        {
            var first = _dataFirstRow is { } value1 ? (int)value1 : 1;
            var normalized = value is null
                ? first
                : (decimal)Math.Clamp((int)value.Value, first, Math.Max(first, _rowCountMaximum));
            if (!SetField(ref _dataLastRow, normalized) || _isUpdating)
                return;

            RaiseRegionChanged();
        }
    }

    public IReadOnlyList<RosterImportRawRow> RawRows
    {
        get => _rawRows;
        private set => SetField(ref _rawRows, value);
    }

    public RosterImportRawRow? SelectedRawRow
    {
        get => _selectedRawRow;
        set
        {
            if (!SetField(ref _selectedRawRow, value))
                return;

            if (!_isUpdating && value is not null)
                ApplyHeaderRow(value.RowNumber, fromUser: true);
        }
    }

    /// <summary>当前选区；列范围恒为全部列（FirstColumn = 1、LastColumn = null）。</summary>
    public RosterImportRegion CurrentRegion => new(
        _headerRow,
        _dataFirstRow is { } first ? (int)first : 1,
        _dataLastRow is { } last ? (int)last : _rowCountMaximum,
        1,
        null);

    /// <summary>
    ///     载入新表：重置工作表、表头行与数据行范围，并按关键词自动找表头行。
    ///     重载期间不触发任何事件（避免抽屉在重入时反复重裁）。
    /// </summary>
    public void Load(
        IReadOnlyList<string>? sheetNames,
        string? sheetName,
        RosterImportGrid? grid,
        IReadOnlyList<string>? headerKeywords = null)
    {
        _isUpdating = true;
        try
        {
            _grid = grid ?? RosterImportGrid.Empty;
            _headerKeywords = headerKeywords ?? [];
            _lastContentRow = RosterImportRegionBuilder.ResolveLastContentRow(_grid);
            _rowCountMaximum = Math.Max(1, _lastContentRow);

            var sheets = sheetNames ?? [];
            _sheetOptions = sheets;
            _selectedSheet = sheetName;
            if (sheets.Count > 0 && string.IsNullOrWhiteSpace(_selectedSheet))
                _selectedSheet = sheets[0];

            var detected = RosterImportRegionBuilder.DetectHeaderRow(_grid, _headerKeywords);
            _headerRow = Math.Clamp(detected, 1, _rowCountMaximum);
            _hasHeaderRowWarning = _grid.RowCount > 1
                                   && RosterImportRegionBuilder.ScoreRow(_grid.GetRow(0), _headerKeywords) == 0;

            _dataFirstRow = Math.Clamp(_headerRow.Value + 1, 1, _rowCountMaximum);
            _dataLastRow = _rowCountMaximum;

            RebuildHeaderRowOptions();
            RebuildRawRows();
            HasGrid = _grid.RowCount > 0;
        }
        finally
        {
            _isUpdating = false;
        }

        OnPropertyChanged(string.Empty);
    }

    /// <summary>清空（没有可用表格时抽屉会调它）。</summary>
    public void Clear()
    {
        _isUpdating = true;
        try
        {
            _grid = RosterImportGrid.Empty;
            _headerKeywords = [];
            _headerRow = null;
            _lastContentRow = 0;
            _rowCountMaximum = 1;
            _dataFirstRow = null;
            _dataLastRow = null;
            _sheetOptions = [];
            _selectedSheet = null;
            _hasHeaderRowWarning = false;
            _headerRowOptions = [];
            _selectedHeaderRowOption = null;
            _rawRows = [];
            _selectedRawRow = null;
            HasGrid = false;
        }
        finally
        {
            _isUpdating = false;
        }

        OnPropertyChanged(string.Empty);
    }

    /// <summary>把某一行设为表头行，并把预览窗口与数据起始行跟着挪过去。</summary>
    public void ApplyHeaderRow(int? row, bool fromUser = false)
    {
        var clamped = row is null ? (int?)null : Math.Clamp(row.Value, 1, _rowCountMaximum);
        var changed = clamped != _headerRow || fromUser;
        _headerRow = clamped;

        _isUpdating = true;
        try
        {
            _dataFirstRow = clamped is null
                ? 1
                : Math.Min(clamped.Value + 1, _rowCountMaximum);
            _dataLastRow = _rowCountMaximum;

            RebuildRawRows();
            RebuildSelectedHeaderOption();
        }
        finally
        {
            _isUpdating = false;
        }

        OnPropertyChanged(string.Empty);
        if (changed)
            RaiseRegionChanged();
    }

    private void RebuildHeaderRowOptions()
    {
        var limit = Math.Max(
            Math.Min(Math.Max(_lastContentRow, 1), HeaderRowOptionLimit),
            _headerRow ?? 0);

        var options = new List<RosterImportHeaderRowOption>
        {
            new(null, "无表头（用 A、B、C 作为列名）")
        };
        for (var row = 1; row <= limit; row++)
            options.Add(new RosterImportHeaderRowOption(row, $"第 {row} 行"));

        _headerRowOptions = options;
        RebuildSelectedHeaderOption();
    }

    private void RebuildSelectedHeaderOption()
    {
        _selectedHeaderRowOption = _headerRowOptions.FirstOrDefault(option => option.Row == _headerRow)
                                   ?? _headerRowOptions.FirstOrDefault();
    }

    private void RebuildRawRows()
    {
        var rows = new List<RosterImportRawRow>();
        if (_grid.RowCount == 0 || _lastContentRow <= 0)
        {
            _rawRows = rows;
            _selectedRawRow = null;
            return;
        }

        var anchor = _headerRow ?? (_dataFirstRow is { } first ? (int)first : 1);
        var lastStart = Math.Max(1, _lastContentRow - RawPreviewWindowSize + 1);
        var start = Math.Clamp(anchor - (RawPreviewWindowSize - 1) / 2, 1, lastStart);
        var end = Math.Min(start + RawPreviewWindowSize - 1, _lastContentRow);

        for (var row = start; row <= end; row++)
            rows.Add(new RosterImportRawRow(row, $"第 {row} 行", BuildRowText(_grid.GetRow(row - 1))));

        _rawRows = rows;
        _selectedRawRow = _headerRow is { } header
            ? rows.FirstOrDefault(item => item.RowNumber == header)
            : null;
    }

    /// <summary>一行原始内容：右侧空白列截掉，列与列之间用「 | 」连；整行全空显示「（空行）」。</summary>
    private static string BuildRowText(IReadOnlyList<string?>? cells)
    {
        if (cells is null || cells.Count == 0)
            return "（空行）";

        var lastIndex = -1;
        for (var index = 0; index < cells.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(cells[index]))
                lastIndex = index;
        }

        if (lastIndex < 0)
            return "（空行）";

        var parts = new List<string>(lastIndex + 1);
        for (var index = 0; index <= lastIndex; index++)
            parts.Add(cells[index]?.Trim() ?? string.Empty);

        var text = string.Join(" | ", parts);
        return text.Length <= 240 ? text : string.Concat(text.AsSpan(0, 240), "…");
    }

    private decimal? NormalizeRow(decimal? value, bool isFirst)
    {
        if (value is null)
            return isFirst ? 1 : Math.Max(1, _rowCountMaximum);

        var clamped = Math.Clamp((int)value.Value, 1, Math.Max(1, _rowCountMaximum));
        return clamped;
    }

    private void RaiseRegionChanged()
    {
        if (_isUpdating)
            return;

        RegionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>表头行下拉的一行。<c>Row</c> 为 null 表示「无表头」。</summary>
public sealed record RosterImportHeaderRowOption(int? Row, string Label);

/// <summary>原始行预览的一行。</summary>
public sealed record RosterImportRawRow(int RowNumber, string RowLabel, string Text);