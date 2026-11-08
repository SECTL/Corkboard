using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Corkboard.Core.Actions;

/// <summary>
///     执行记录的存储：<c>action/runs/yyyy-MM-dd.jsonl</c>，一行一条 <see cref="ActionRun" />。
///     <para>
///         只追加、不改写：历史是「发生过什么」的证据，补写 / 覆盖只会让它不可信。
///         跨日的记录按 <see cref="ActionRun.StartedAt" /> 归日；读取时坏行跳过（一项坏数据不该让整天的历史打不开）。
///     </para>
///     <para>
///         写失败只记日志 —— 执行本身已经发生了，日志写不进去不该反过来影响执行结果。
///     </para>
/// </summary>
public sealed class ActionRunStore
{
    /// <summary>执行记录保留天数（窗口每日首拍 / 打开时调 <see cref="Prune" />）。</summary>
    public const int ActionRunRetentionDays = 90;

    private const string FilePattern = "yyyy-MM-dd";
    private const string FileExtension = ".jsonl";

    private readonly ActionStorePaths _paths;
    private readonly ILogger<ActionRunStore>? _logger;
    private readonly object _sync = new();

    /// <summary>按路径表建存储（不读盘，读到才懒加载）。</summary>
    public ActionRunStore(ActionStorePaths paths, ILogger<ActionRunStore>? logger = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger;
    }

    /// <summary>追加一条执行记录（按开始时刻归到那天的文件）。</summary>
    public void Append(ActionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var date = DateOnly.FromDateTime(run.StartedAt);
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(_paths.RunsFolder);
                var line = JsonSerializer.Serialize(run, ActionJson.Compact) + Environment.NewLine;
                File.AppendAllText(_paths.RunFile(date), line,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "追加行动执行记录失败：{File}", _paths.RunFile(date));
            }
        }
    }

    /// <summary>读某一天的全部执行记录（追加顺序）；文件不在就是空列表。</summary>
    public IReadOnlyList<ActionRun> ReadDay(DateOnly date)
    {
        var file = _paths.RunFile(date);
        if (!File.Exists(file))
            return [];

        var runs = new List<ActionRun>();
        try
        {
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var run = JsonSerializer.Deserialize<ActionRun>(line, ActionJson.Options);
                    if (run is not null)
                        runs.Add(run);
                }
                catch (JsonException ex)
                {
                    _logger?.LogWarning(ex, "跳过 action/runs 里的坏行：{File}", file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "读取行动执行记录失败：{File}", file);
        }

        return runs;
    }

    /// <summary>读一段日期（两端都含）的全部执行记录，按开始时刻升序。</summary>
    public IReadOnlyList<ActionRun> ReadRange(DateOnly from, DateOnly to)
    {
        if (from > to)
            return [];

        var runs = new List<ActionRun>();
        for (var date = from; date <= to; date = date.AddDays(1))
            runs.AddRange(ReadDay(date));

        return runs.OrderBy(run => run.StartedAt).ToArray();
    }

    /// <summary>
    ///     清掉 <paramref name="retentionDays" /> 天之前的执行记录文件。
    ///     <para>
    ///         只删文件名严格是 <c>yyyy-MM-dd.jsonl</c> 的文件（同一目录里用户自己放的东西一律不动）。
    ///     <paramref name="retentionDays" /> 为负数按 0 处理。
    ///     </para>
    /// </summary>
    public void Prune(int retentionDays)
    {
        if (retentionDays < 0)
            retentionDays = 0;

        if (!Directory.Exists(_paths.RunsFolder))
            return;

        var cutoff = DateOnly.FromDateTime(DateTime.Today).AddDays(-retentionDays);

        try
        {
            foreach (var file in Directory.EnumerateFiles(_paths.RunsFolder, "*" + FileExtension))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Length != FilePattern.Length)
                    continue;

                if (!DateOnly.TryParseExact(name, FilePattern, CultureInfo.InvariantCulture, DateTimeStyles.None,
                        out var date))
                    continue;

                if (date >= cutoff)
                    continue;

                File.Delete(file);
                _logger?.LogInformation("清理过期的行动执行记录：{File}", file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "清理行动执行记录失败：{Folder}", _paths.RunsFolder);
        }
    }
}
