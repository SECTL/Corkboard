namespace Corkboard.Core.DutyRoster.Import;

/// <summary>
///     表格导入区域。行号、列号都是 <b>1 起</b>的原始表格坐标，与用户在 Excel 里看到的一致。
///     <para>
///         <see cref="HeaderRow" /> 为 <c>null</c> 表示这张表没有表头行，列名退化成 A、B、C…；
///         <see cref="LastDataRow" /> / <see cref="LastColumn" /> 为 <c>null</c> 表示「到有内容为止」，
///         由 <see cref="RosterImportRegionBuilder" /> 现算——用户只改表头行时不用同时改三个数字。
///     </para>
/// </summary>
public sealed record RosterImportRegion(
    int? HeaderRow,
    int FirstDataRow,
    int? LastDataRow,
    int FirstColumn,
    int? LastColumn)
{
    /// <summary>
    ///     默认区域：指定表头行，其后取全部数据行与全部列。
    ///     等价于「第一行即表头」的老行为，也是界面上一键重置用的值。
    /// </summary>
    public static RosterImportRegion Default(int headerRow) => new(headerRow, headerRow + 1, null, 1, null);
}

/// <summary>
///     导入区域的纯解析逻辑：表头行识别、尾部空行/右侧空列的裁剪、区域 → 表格。
///     <para>
///         这里不碰 IO、不碰界面：读取由 <see cref="RosterFileReader" /> 负责，交互由导入向导负责，
///         所以整段可以在单测里直接用内存网格驱动。
///     </para>
/// </summary>
public static class RosterImportRegionBuilder
{
    /// <summary>
    ///     自动识别表头行时最多向下扫描的行数。
    ///     真实班级名单第一行常是「高一(1)班名单」这种大标题，只扫前 10 行既够用又不会把数据行误判成表头。
    /// </summary>
    public const int HeaderScanRowLimit = 10;

    /// <summary>
    ///     按关键字给一行的内容打分，用于自动识别表头行；与列映射共用 <see cref="RosterImportParser.ScoreColumn" />
    ///     的规则（完全匹配 100−i、包含 50−i），所以「序号 / 姓名 / 性别」三列的表头行分数远高于标题行。
    /// </summary>
    public static int ScoreRow(IEnumerable<string?>? cells, IReadOnlyList<string>? keywords)
    {
        if (cells is null || keywords is null || keywords.Count == 0)
            return 0;

        var score = 0;
        foreach (var cell in cells)
        {
            if (string.IsNullOrWhiteSpace(cell))
                continue;

            score += RosterImportParser.ScoreColumn(cell, keywords);
        }

        return score;
    }

    /// <summary>
    ///     在前 <see cref="HeaderScanRowLimit" /> 行里挑最像表头的一行（1 起）。
    ///     一行都识别不出来时返回 1，保持「第一行即表头」的老行为，用户仍可在界面上手动改。
    /// </summary>
    public static int DetectHeaderRow(RosterImportGrid? grid, IReadOnlyList<string>? keywords)
    {
        if (grid is null || grid.RowCount == 0 || keywords is null || keywords.Count == 0)
            return 1;

        var bestRow = 1;
        var bestScore = 0;
        var limit = Math.Min(HeaderScanRowLimit, grid.RowCount);
        for (var row = 1; row <= limit; row++)
        {
            var score = ScoreRow(grid.GetRow(row), keywords);
            // 严格大于：分数相同时保留更靠上的那行（标题行与真表头撞分时优先标题，用户改一行就能修）。
            if (score <= bestScore)
                continue;

            bestScore = score;
            bestRow = row;
        }

        return bestRow;
    }

    /// <summary>
    ///     最后一个出现过非空单元格的行号（1 起），整张表都没有内容时返回 0。
    ///     <para>
    ///         表格尾部被按过回车、或被格式刷过的空行会让行数虚高，所以默认数据区末尾与预览范围
    ///         都按「真实内容」算，而不是按网格长度——否则导入进度条与预览里会凭空多出一大截空行。
    ///     </para>
    /// </summary>
    public static int ResolveLastContentRow(RosterImportGrid? grid)
    {
        if (grid is null)
            return 0;

        for (var row = grid.RowCount; row >= 1; row--)
        {
            if (!grid.IsRowEmpty(row))
                return row;
        }

        return 0;
    }

    /// <summary>
    ///     从 <paramref name="firstRow" /> 起最后一个出现过非空单元格的列号，用来裁掉右侧整列空白的列，
    ///     避免列映射下拉里出现一大堆无意义的 A、B、C 占位项。
    /// </summary>
    public static int ResolveColumnCount(RosterImportGrid? grid, int firstRow)
    {
        if (grid is null)
            return 0;

        var columnCount = 0;
        var start = Math.Max(1, firstRow);
        for (var row = start; row <= grid.RowCount; row++)
        {
            var cells = grid.GetRow(row);
            for (var column = cells.Count; column > columnCount; column--)
            {
                if (!string.IsNullOrWhiteSpace(cells[column - 1]))
                {
                    columnCount = column;
                    break;
                }
            }
        }

        return columnCount;
    }

    /// <summary>
    ///     自动识别整块区域：表头行取前 10 行里分数最高的一行，数据行到最后一个有内容的行，
    ///     列从第 1 列到最后一个有内容的列。用户仍然可以在这之上手动改任何一个数字。
    /// </summary>
    public static RosterImportRegion DetectRegion(RosterImportGrid? grid, RosterImportKeywords? keywords = null)
    {
        var lastContentRow = ResolveLastContentRow(grid);
        if (grid is null || lastContentRow == 0)
            return new RosterImportRegion(null, 1, null, 1, null);

        var effectiveKeywords = keywords ?? RosterImportKeywords.Default;
        var headerRow = DetectHeaderRow(grid, effectiveKeywords.All);
        var lastColumn = ResolveColumnCount(grid, Math.Min(headerRow, lastContentRow));
        return new RosterImportRegion(
            headerRow,
            headerRow + 1,
            lastContentRow,
            FirstColumn: 1,
            lastColumn == 0 ? null : lastColumn);
    }

    /// <summary>
    ///     区域 → 列名 + 行字典。区域里的行/列越界值一律夹到网格范围内，不抛异常。
    ///     <para>
    ///         数据区永远不含表头行本身：即使用户把最后一行选成表头、又把 FirstDataRow 填得比它小，
    ///         也会从表头下一行开始取——否则表头会被当成一条数据导进去。
    ///     </para>
    /// </summary>
    public static RosterImportTable Build(RosterImportGrid? grid, RosterImportRegion? region)
    {
        if (grid is null || region is null || grid.RowCount == 0)
            return RosterImportTable.Empty;

        int? headerRow = null;
        if (region.HeaderRow is { } candidateHeaderRow && candidateHeaderRow >= 1 && candidateHeaderRow <= grid.RowCount)
            headerRow = candidateHeaderRow;

        var lastDataRow = Math.Min(region.LastDataRow ?? ResolveLastContentRow(grid), grid.RowCount);
        var firstDataRow = Math.Max(1, region.FirstDataRow);
        if (headerRow is { } headerRowNumber && firstDataRow <= headerRowNumber)
            firstDataRow = headerRowNumber + 1;

        var columnCount = ResolveColumnCount(grid,
            headerRow is null ? firstDataRow : Math.Min(headerRow.Value, firstDataRow));
        var firstColumn = Math.Max(1, region.FirstColumn);
        var lastColumn = Math.Min(region.LastColumn ?? columnCount, columnCount);
        if (columnCount == 0 || lastColumn < firstColumn || lastDataRow < firstDataRow)
            return RosterImportTable.Empty;

        var columns = BuildColumnNames(grid, headerRow, firstColumn, lastColumn);

        var rows = new List<IReadOnlyDictionary<string, string>>();
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            var cells = grid.GetRow(row);
            var values = new Dictionary<string, string>(columns.Count, StringComparer.Ordinal);
            var hasValue = false;
            for (var index = 0; index < columns.Count; index++)
            {
                var column = firstColumn + index;
                var value = column <= cells.Count ? cells[column - 1]?.Trim() ?? string.Empty : string.Empty;
                values[columns[index]] = value;
                hasValue |= value.Length > 0;
            }

            if (hasValue)
                rows.Add(values);
        }

        return new RosterImportTable(columns, rows);
    }

    /// <summary>列号 → Excel 列字母（1 → A，27 → AA）。</summary>
    public static string ColumnName(int column)
    {
        var value = Math.Max(1, column);
        var builder = new System.Text.StringBuilder(3);
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char)('A' + value % 26));
            value /= 26;
        }

        return builder.ToString();
    }

    /// <summary>Excel 列字母 → 列号（A → 1，AA → 27）；只接受纯字母，其余返回 0。</summary>
    public static int ColumnIndex(string? column)
    {
        if (string.IsNullOrEmpty(column) || column.Length > 3)
            return 0;

        var index = 0;
        foreach (var character in column)
        {
            if (character is < 'A' or > 'Z')
                return 0;

            index = index * 26 + (character - 'A' + 1);
        }

        return index;
    }

    /// <summary>
    ///     表头行 → 列名：空表头回退成列字母；重名追加「 (2)」「 (3)」。
    ///     列名唯一是硬要求——<see cref="RosterImportTable.Rows" /> 用它当字典键。
    /// </summary>
    private static List<string> BuildColumnNames(RosterImportGrid grid, int? headerRow, int firstColumn, int lastColumn)
    {
        var columns = new List<string>(lastColumn - firstColumn + 1);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            var header = headerRow is null ? string.Empty : grid.GetCell(headerRow.Value, column).Trim();
            var name = header.Length > 0 ? header : ColumnName(column);
            var candidate = name;
            var suffix = 2;
            while (!used.Add(candidate))
                candidate = $"{name} ({suffix++})";

            columns.Add(candidate);
        }

        return columns;
    }
}
