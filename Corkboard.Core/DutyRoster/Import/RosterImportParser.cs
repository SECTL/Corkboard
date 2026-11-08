namespace Corkboard.Core.DutyRoster.Import;

/// <summary>
///     导入流程的错误码。文件读取与区域解析共用一份，界面按码取文案（不要在 Core 里拼用户可见文案）。
///     <para>
///         <see cref="None" /> 之外的所有情况都<b>不抛异常</b>，而是带着码与诊断信息返回——
///         导入向导要能在坏文件上继续显示「哪里不对、还能不能救」，异常会把它打成白屏。
///     </para>
/// </summary>
public enum RosterImportError
{
    None = 0,

    /// <summary>文件是空的，或只有空行。</summary>
    EmptyFile,

    /// <summary>扩展名/内容都不像支持的表格式（不是 csv/xlsx）。</summary>
    UnsupportedFormat,

    /// <summary>老式 .xls（BIFF/OLE2）不支持，明确报错并提示另存为 xlsx 或 csv。</summary>
    LegacyXlsUnsupported,

    /// <summary>xlsx 不是有效的 zip 包，或包内 XML 损坏。</summary>
    InvalidArchive,

    /// <summary>zip 包里找不到工作表。</summary>
    NoWorksheet,

    /// <summary>读文件失败（IO、权限、文件被占用）。</summary>
    ReadFailed,

    /// <summary>区域裁不出任何列。</summary>
    NoColumns,

    /// <summary>姓名列与序号列至少要有映射一个，否则这份数据没法认人。</summary>
    MissingRequiredColumn,

    /// <summary>区域里没有任何可用数据行。</summary>
    NoDataRows,

    /// <summary>检测到重名，且用户选择「取消导入」。</summary>
    DuplicateNamesCancelled
}

/// <summary>
///     重名处理三档：保留（可能真有两个同名同学）、自动改名（姓名 (2)）、取消整次导入。
///     默认是<b>自动改名</b>——值日表靠姓名认人，同格出现两个「张三」无法区分。
/// </summary>
public enum RosterDuplicateHandling
{
    /// <summary>原样保留重名（名单里可能真有同名）。</summary>
    Keep = 0,

    /// <summary>从第二次出现起改成「姓名 (2)」「姓名 (3)」。</summary>
    Rename = 1,

    /// <summary>发现重名就整次取消，由界面提示用户回去改表。</summary>
    Cancel = 2
}

/// <summary>
///     列映射：每个字段对应表格里的一列，<c>null</c> 表示这一列不存在（或被用户清空）。
///     <para>
///         <see cref="Id" /> 与 <see cref="Name" /> 至少要有一个：序号可以当身份，姓名可以当身份，
///         两列都没有的行没法进名单。分组列只是留给导入向导做「自动建组」，Core 这里不落盘。
///     </para>
/// </summary>
public sealed record RosterImportColumnMapping(
    string? Id,
    string? Name,
    string? Gender,
    string? Group = null,
    string? Tags = null)
{
    public static RosterImportColumnMapping Empty { get; } = new(null, null, null, null, null);

    /// <summary>姓名或序号至少映射了一列。</summary>
    public bool HasRequiredColumn => !string.IsNullOrWhiteSpace(Id) || !string.IsNullOrWhiteSpace(Name);
}

/// <summary>
///     列映射与表头识别用的关键字表。可替换（界面以后允许用户自定义），默认值覆盖中英文常见表头。
///     <para>
///         列表内顺序有意义：<see cref="RosterImportParser.ScoreColumn" /> 里越靠前的关键字
///         得分越高（完全匹配 100−i、包含 50−i），所以「学号」要排在「号」这种泛词前面。
///     </para>
/// </summary>
public sealed class RosterImportKeywords
{
    public IReadOnlyList<string> Id { get; init; } =
        ["id", "序号", "学号", "编号", "number", "no."];

    public IReadOnlyList<string> Name { get; init; } =
        ["name", "姓名", "名字", "学生", "student"];

    public IReadOnlyList<string> Gender { get; init; } =
        ["gender", "sex", "性别"];

    public IReadOnlyList<string> Group { get; init; } =
        ["group", "class", "小组", "分组", "组别", "班级"];

    public IReadOnlyList<string> Tags { get; init; } =
        ["tags", "tag", "标签", "备注", "note"];

    /// <summary>默认关键字表。</summary>
    public static RosterImportKeywords Default { get; } = new();

    private IReadOnlyList<string>? _all;

    /// <summary>
    ///     所有字段的关键字并集（去重），供表头行打分使用。
    ///     首次访问时才计算：关键字表可以被子类/init 覆盖，字段初始值里算会把默认表固定下来。
    /// </summary>
    public IReadOnlyList<string> All => _all ??= BuildAll();

    private List<string> BuildAll()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<string>();
        foreach (var keyword in Id.Concat(Name).Concat(Gender).Concat(Group).Concat(Tags))
        {
            if (string.IsNullOrWhiteSpace(keyword) || !seen.Add(keyword))
                continue;

            all.Add(keyword);
        }

        return all;
    }
}

/// <summary>
///     一次解析的结果。<see cref="Members" /> 即使失败也尽量填好（界面要拿它做预览），
///     真正决定「能不能落库」的只有 <see cref="Error" />。
/// </summary>
public sealed record RosterParseResult(
    IReadOnlyList<DutyMember> Members,
    RosterImportColumnMapping Mapping,
    IReadOnlyList<string> DuplicateNames,
    RosterImportError Error,
    string? Message)
{
    public bool Success => Error == RosterImportError.None;

    internal static RosterParseResult Failure(
        RosterImportError error,
        string message,
        RosterImportColumnMapping? mapping = null) =>
        new([], mapping ?? RosterImportColumnMapping.Empty, [], error, message);
}

/// <summary>
///     表格名单导入的纯解析逻辑：列映射打分、行 → <see cref="DutyMember" />、重名处理、RecordId 复用。
///     <para>
///         全部是静态纯函数（除了产出新对象，不改入参），读取与界面完全在外层：
///         这样移动端/单测都能直接喂内存表格。
///     </para>
/// </summary>
public static class RosterImportParser
{
    /// <summary>
    ///     按「|」拆关键字串（界面以后的自定义关键字输入框用）。
    ///     用「|」而不是逗号，是因为表头关键字本身可能带逗号/空格。
    /// </summary>
    public static string[] SplitKeywords(string? keywords)
    {
        return string.IsNullOrWhiteSpace(keywords)
            ? []
            : keywords.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    ///     单个单元格对关键字列表的匹配得分：精确命中高于包含命中，关键字越靠前得分越高。
    ///     <para>
    ///         100−i / 50−i 而不是固定分，是为了让「姓名」压过「学生姓名」这种泛写法；
    ///         表头识别与列映射共用这一套规则，两处判断才不会打架。
    ///     </para>
    /// </summary>
    public static int ScoreColumn(string? column, IReadOnlyList<string>? keywords)
    {
        if (string.IsNullOrWhiteSpace(column) || keywords is null || keywords.Count == 0)
            return 0;

        var normalizedColumn = column.Trim().ToLowerInvariant();
        var bestScore = 0;
        for (var index = 0; index < keywords.Count; index++)
        {
            var keyword = keywords[index];
            if (string.IsNullOrWhiteSpace(keyword))
                continue;

            keyword = keyword.Trim().ToLowerInvariant();
            var score = normalizedColumn == keyword ? 100 - index
                : normalizedColumn.Contains(keyword, StringComparison.Ordinal) ? 50 - index
                : 0;
            if (score > bestScore)
                bestScore = score;
        }

        return bestScore;
    }

    /// <summary>
    ///     给一个字段挑得分最高的列；<paramref name="exclude" /> 用来避开已经分配给别的字段的列，
    ///     免得「姓名」与「姓名拼音」两列都被映射成姓名列。
    /// </summary>
    public static string? FindBestColumn(
        IEnumerable<string>? columns,
        IReadOnlyList<string>? keywords,
        ISet<string>? exclude = null)
    {
        if (columns is null || keywords is null)
            return null;

        var bestScore = 0;
        string? bestColumn = null;
        foreach (var column in columns)
        {
            if (exclude is not null && exclude.Contains(column))
                continue;

            var score = ScoreColumn(column, keywords);
            if (score <= bestScore)
                continue;

            bestScore = score;
            bestColumn = column;
        }

        return bestColumn;
    }

    /// <summary>
    ///     自动列映射：先算出「字段 × 列」的所有候选分，再按分数从高到低贪心分配，
    ///     每个字段最多占一列、每列最多给一个字段。
    ///     <para>
    ///         不用「每个字段各自找最高分」是因为那样容易把同一列同时映射给两个字段
    ///         （表头「学生姓名」既命中姓名又命中学生），贪心分配能保证一对多不会发生。
    ///     </para>
    /// </summary>
    public static RosterImportColumnMapping MapColumns(
        IReadOnlyList<string>? columns,
        RosterImportKeywords? keywords = null)
    {
        if (columns is null || columns.Count == 0)
            return RosterImportColumnMapping.Empty;

        var effectiveKeywords = keywords ?? RosterImportKeywords.Default;
        var keywordsByField = new[]
        {
            effectiveKeywords.Id,
            effectiveKeywords.Name,
            effectiveKeywords.Gender,
            effectiveKeywords.Group,
            effectiveKeywords.Tags
        };

        var candidates = new List<(int Score, int Field, int Column)>();
        for (var field = 0; field < keywordsByField.Length; field++)
        {
            for (var column = 0; column < columns.Count; column++)
            {
                var score = ScoreColumn(columns[column], keywordsByField[field]);
                if (score > 0)
                    candidates.Add((score, field, column));
            }
        }

        var mapped = new string?[keywordsByField.Length];
        var usedColumns = new HashSet<int>();
        foreach (var candidate in candidates
                     .OrderByDescending(item => item.Score)
                     .ThenBy(item => item.Field)
                     .ThenBy(item => item.Column))
        {
            if (mapped[candidate.Field] is not null || usedColumns.Contains(candidate.Column))
                continue;

            mapped[candidate.Field] = columns[candidate.Column];
            usedColumns.Add(candidate.Column);
        }

        return new RosterImportColumnMapping(mapped[0], mapped[1], mapped[2], mapped[3], mapped[4]);
    }

    /// <summary>按列名取一行里的值；列没映射或行里缺这一列时返回空串。</summary>
    public static string GetValue(IReadOnlyDictionary<string, string>? row, string? column)
    {
        if (row is null || string.IsNullOrWhiteSpace(column))
            return string.Empty;

        return row.TryGetValue(column, out var value) ? value?.Trim() ?? string.Empty : string.Empty;
    }

    /// <summary>
    ///     标签串拆分：逗号、分号、斜杠、顿号、空白都当分隔符，去重后返回。
    ///     SR 的 tags 本来就是「空格连接的多个标签」，这里只做归一化，不改变语义。
    /// </summary>
    public static IReadOnlyList<string> SplitTags(string? rawTags)
    {
        if (string.IsNullOrWhiteSpace(rawTags))
            return [];

        var normalized = rawTags;
        foreach (var separator in new[] { '，', ',', '；', ';', '|', '/', '\\', '\n', '\r', '\t', '、' })
            normalized = normalized.Replace(separator, ' ');

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>一行数据是否算一条候选成员：姓名与序号至少有一个非空。</summary>
    public static bool IsCandidate(DutyMember? member) =>
        member is not null
        && (!string.IsNullOrWhiteSpace(member.Name) || !string.IsNullOrWhiteSpace(member.Id));

    /// <summary>找出重复出现的姓名（保持首次出现顺序），重名判断只看姓名、不看序号。</summary>
    public static IReadOnlyList<string> FindDuplicatedNames(IEnumerable<DutyMember>? members)
    {
        if (members is null)
            return [];

        return members
            .Select(member => member?.Name?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0)
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
    }

    /// <summary>
    ///     重名改名：从第二次出现起追加「 (2)」「 (3)」。
    ///     追加前会避开名单里本来就有的同名言——真实名单里出现「张三 (2)」时不会被改名撞成两个同名。
    /// </summary>
    public static void RenameDuplicates(IReadOnlyList<DutyMember>? members)
    {
        if (members is null)
            return;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var used = new HashSet<string>(
            members.Select(member => member?.Name ?? string.Empty).Where(name => name.Length > 0),
            StringComparer.Ordinal);

        foreach (var member in members)
        {
            if (member is null)
                continue;

            var baseName = member.Name?.Trim() ?? string.Empty;
            if (baseName.Length == 0)
                continue;

            counts.TryAdd(baseName, 0);
            counts[baseName]++;
            if (counts[baseName] == 1)
                continue;

            var candidate = $"{baseName} ({counts[baseName]})";
            while (!used.Add(candidate))
                candidate = $"{baseName} ({++counts[baseName]})";

            member.Name = candidate;
        }
    }

    /// <summary>
    ///     把导入行与本地已有名单按「序号 + 姓名」对上，复用已有的 <see cref="DutyMember.RecordId" />。
    ///     <para>
    ///         为什么需要：xlsx/csv 里没有 SR 的 <c>record_id</c>，如果每次重新导入都发新 ID，
    ///         已有的兑换、固定人员、历史记录会全部指向已经不存在的成员。按序号+姓名复用之后，
    ///         同一份名单反复导入不会断引用；姓名改过（或序号改过）的成员才会被当成新人。
    ///     </para>
    ///     <para>
    ///         同一对「序号+姓名」在两边都出现多次时，只让第一条复用同一个 ID，其余发新 ID——
    ///         否则名单里会出现两条相同 <see cref="DutyMember.RecordId" />，落盘时会被去重掉一条。
    ///     </para>
    /// </summary>
    public static IReadOnlyList<DutyMember> ReuseRecordIds(
        IReadOnlyList<DutyMember>? imported,
        IReadOnlyList<DutyMember>? existing)
    {
        if (imported is null)
            return [];

        if (existing is null || existing.Count == 0)
            return imported;

        var byKey = new Dictionary<(string Id, string Name), Guid>();
        foreach (var member in existing)
        {
            if (member is null)
                continue;

            var key = (Id: NormalizeId(member.Id), Name: member.Name?.Trim() ?? string.Empty);
            if (key.Name.Length == 0 && key.Id.Length == 0)
                continue;

            // 先出现的那条为准：本地名单里理论上不会重复，真重复时保留第一条的身份。
            byKey.TryAdd(key, member.RecordId);
        }

        var usedRecordIds = new HashSet<Guid>();
        var result = new List<DutyMember>(imported.Count);
        foreach (var member in imported)
        {
            if (member is null)
                continue;

            var importedName = member.Name ?? string.Empty;
            var key = (Id: NormalizeId(member.Id), Name: importedName.Trim());
            if (byKey.TryGetValue(key, out var recordId) && usedRecordIds.Add(recordId))
            {
                result.Add(new DutyMember
                {
                    RecordId = recordId,
                    // 已有成员可能来自手改过的 JSON（字符串字段为 null），拷进来时统一兜成空串，
                    // 免得下游的 Trim/Split 碰到 null。
                    Id = member.Id ?? string.Empty,
                    Name = member.Name ?? string.Empty,
                    Gender = member.Gender ?? string.Empty,
                    Tags = member.Tags ?? string.Empty,
                    IsActive = member.IsActive,
                    GroupId = member.GroupId
                });
                continue;
            }

            result.Add(member);
        }

        return result;
    }

    /// <summary>
    ///     一次性完成「列映射 → 解析成员 → 重名处理 → RecordId 复用」。
    ///     <para>
    ///         <paramref name="mapping" /> 为空时按 <paramref name="keywords" /> 自动映射；
    ///         <paramref name="duplicateHandling" /> 为 <see cref="RosterDuplicateHandling.Cancel" /> 时
    ///         只要有重名就返回 <see cref="RosterImportError.DuplicateNamesCancelled" />（但仍带上解析结果供预览）。
    ///     </para>
    /// </summary>
    public static RosterParseResult Parse(
        RosterImportTable? table,
        RosterImportColumnMapping? mapping = null,
        RosterDuplicateHandling duplicateHandling = RosterDuplicateHandling.Rename,
        RosterImportKeywords? keywords = null,
        IReadOnlyList<DutyMember>? existingMembers = null)
    {
        if (table is null || table.Columns.Count == 0)
            return RosterParseResult.Failure(RosterImportError.NoColumns, "表格没有可用列，请重新选择区域。");

        var resolvedMapping = mapping ?? MapColumns(table.Columns, keywords);
        if (!resolvedMapping.HasRequiredColumn)
        {
            return RosterParseResult.Failure(
                RosterImportError.MissingRequiredColumn,
                "姓名列与序号列至少要映射一个，否则这份名单没法认人。",
                resolvedMapping);
        }

        var members = new List<DutyMember>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            var member = new DutyMember
            {
                Id = GetValue(row, resolvedMapping.Id),
                Name = GetValue(row, resolvedMapping.Name),
                Gender = GetValue(row, resolvedMapping.Gender),
                Tags = string.Join(' ', SplitTags(GetValue(row, resolvedMapping.Tags)))
            };

            if (IsCandidate(member))
                members.Add(member);
        }

        if (members.Count == 0)
        {
            return RosterParseResult.Failure(
                RosterImportError.NoDataRows,
                "选中的区域里没有任何人（姓名与序号都是空的）。",
                resolvedMapping);
        }

        var duplicatedNames = FindDuplicatedNames(members);
        if (duplicateHandling == RosterDuplicateHandling.Cancel && duplicatedNames.Count > 0)
        {
            return new RosterParseResult(
                members,
                resolvedMapping,
                duplicatedNames,
                RosterImportError.DuplicateNamesCancelled,
                $"名单里有 {duplicatedNames.Count} 个重名，已按选择取消导入。");
        }

        if (duplicateHandling == RosterDuplicateHandling.Rename)
            RenameDuplicates(members);

        var finalMembers = existingMembers is { Count: > 0 }
            ? ReuseRecordIds(members, existingMembers)
            : members;

        return new RosterParseResult(finalMembers, resolvedMapping, duplicatedNames,
            RosterImportError.None, null);
    }

    /// <summary>序号比较忽略大小写与首尾空白：学号「s01」与「S01」视为同一个。</summary>
    private static string NormalizeId(string? id) => id?.Trim().ToUpperInvariant() ?? string.Empty;
}
