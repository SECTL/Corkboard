namespace Corkboard.Core.DutyRoster.Import;

/// <summary>
///     名单导入的原始表格网格：行号、列号都是 <b>1 起</b>，与用户在 Excel 里看到的坐标一致。
///     <para>
///         这里刻意保留空行与空列（不压缩），因为：
///         ① 区域选择器要让用户按「第几行」挑表头与数据范围，行号必须与文件对得上；
///         ② 表头识别要能跳过顶部的「高一(1)班名单」大标题行，压缩掉空行会让行号错位。
///         行/列里的空单元格统一读成空串（<see cref="GetCell" />），判空只走 <see cref="string.IsNullOrWhiteSpace" />。
///     </para>
///     <para>
///         读取端（<see cref="RosterFileReader" />）产出的就是它；<see cref="RosterImportRegionBuilder" /> 再按区域
///         裁成 <see cref="RosterImportTable" />。两个类型分开，是为了让「选区域」与「解析列」互不干扰。
///     </para>
/// </summary>
public sealed class RosterImportGrid
{
    private readonly IReadOnlyList<IReadOnlyList<string?>> _rows;

    /// <summary>空网格（没有读入任何内容）。</summary>
    public static RosterImportGrid Empty { get; } = new(Array.Empty<IEnumerable<string?>?>());

    public RosterImportGrid(IEnumerable<IEnumerable<string?>?>? rows)
    {
        var copied = new List<IReadOnlyList<string?>>();
        if (rows is not null)
        {
            foreach (var row in rows)
                copied.Add(row is null ? [] : row.ToArray());
        }

        _rows = copied;
    }

    /// <summary>总行数（包含尾部空行）。</summary>
    public int RowCount => _rows.Count;

    /// <summary>原始行集合（拷贝后的数组，外部改不动内部状态）。</summary>
    public IReadOnlyList<IReadOnlyList<string?>> Rows => _rows;

    /// <summary>取一整行；行号越界时返回空行，不抛异常（宽容解析）。</summary>
    public IReadOnlyList<string?> GetRow(int row) =>
        row >= 1 && row <= _rows.Count ? _rows[row - 1] : [];

    /// <summary>取一格；行/列越界或单元格为空时返回空串。</summary>
    public string GetCell(int row, int column)
    {
        var cells = GetRow(row);
        return column >= 1 && column <= cells.Count ? cells[column - 1] ?? string.Empty : string.Empty;
    }

    /// <summary>该行是否一个非空单元格都没有（尾部空行、Excel 里按过回车留下的空行）。</summary>
    public bool IsRowEmpty(int row)
    {
        foreach (var cell in GetRow(row))
        {
            if (!string.IsNullOrWhiteSpace(cell))
                return false;
        }

        return true;
    }
}

/// <summary>
///     裁好区域的名单表格：列名 + 每行的「列名 → 值」字典。
///     <para>
///         列名来自表头行；空表头回退成 Excel 列字母（A、B、C…），重复列名追加「 (2)」，
///         保证列名唯一、能直接当字典键。行是只保留「至少一格非空」的行——尾部空行不进解析。
///         值一律 Trim 过：单元格里手打的尾随空格不该变成姓名的一部分。
///     </para>
/// </summary>
public sealed record RosterImportTable(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows)
{
    /// <summary>空表格（没有可用列或没有数据行）。</summary>
    public static RosterImportTable Empty { get; } = new([], []);

    public int RowCount => Rows.Count;

    public bool IsEmpty => Columns.Count == 0 || Rows.Count == 0;
}
