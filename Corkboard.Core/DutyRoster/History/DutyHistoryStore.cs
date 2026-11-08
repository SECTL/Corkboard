using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Corkboard.Shared;
using Microsoft.Extensions.Logging;

namespace Corkboard.Core.DutyRoster.History;

/// <summary>
///     值日表历史存储：<c>data/history/duty/&lt;yyyy-MM-dd&gt;.jsonl</c>，一行一条事件。
///     <para>
///         和作业板历史一样**不放进 <c>data/duty/</c>**（那边会被云同步整体打包），
///         也不存位图快照——值日表事件只是文本，重放靠文档本身就能算出来。
///         坏行跳过、坏文件跳过，永远不在读取路径上抛异常。
///     </para>
/// </summary>
public sealed class DutyHistoryStore : IDutyHistoryStore
{
    private const string FileExtension = ".jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ILogger<DutyHistoryStore>? _logger;
    private readonly string _root;
    private readonly object _sync = new();

    public DutyHistoryStore(ILogger<DutyHistoryStore>? logger = null, string? root = null)
    {
        _logger = logger;
        _root = root ?? Utils.GetDirectoryPath("history", "duty");
    }

    /// <summary>历史根目录（测试里可注入临时目录）。</summary>
    public string Root => _root;

    public IReadOnlyList<DutyHistoryEvent> Read(DateOnly date) =>
        ReadFile(PathFor(date));

    public IReadOnlyList<DutyHistoryEvent> ReadRange(DateOnly from, DateOnly to)
    {
        var events = new List<DutyHistoryEvent>();
        if (from > to)
            return events;

        foreach (var date in ListDays())
        {
            if (date < from || date > to)
                continue;

            events.AddRange(ReadFile(PathFor(date)));
        }

        return events;
    }

    public IReadOnlyList<DateOnly> ListDays()
    {
        try
        {
            if (!Directory.Exists(_root))
                return [];

            var days = new List<DateOnly>();
            foreach (var path in Directory.EnumerateFiles(_root, "*" + FileExtension))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (DateOnly.TryParseExact(name, "yyyy-MM-dd", out var date))
                    days.Add(date);
            }

            days.Sort();
            return days;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "读取值日表历史目录失败：{Root}", _root);
            return [];
        }
    }

    public void Append(DutyHistoryEvent historyEvent)
    {
        if (historyEvent is null)
            return;

        var date = historyEvent.Date == default ? DateOnly.FromDateTime(historyEvent.At.LocalDateTime) : historyEvent.Date;
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(_root);
                var path = PathFor(date);
                var line = JsonSerializer.Serialize(historyEvent, JsonOptions);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, Utf8);
                writer.WriteLine(line);
            }
            catch (Exception ex)
            {
                // 追溯信息写不进去不能挡住主流程（与作业板历史的处理一致）。
                _logger?.LogWarning(ex, "写入值日表历史失败：{Kind}", historyEvent.Kind);
            }
        }
    }

    public void Prune(DateOnly keepFrom)
    {
        foreach (var date in ListDays())
        {
            if (date >= keepFrom)
                continue;

            try
            {
                File.Delete(PathFor(date));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "删除值日表历史失败：{Date}", date);
            }
        }
    }

    private IReadOnlyList<DutyHistoryEvent> ReadFile(string path)
    {
        var events = new List<DutyHistoryEvent>();
        if (!File.Exists(path))
            return events;

        try
        {
            foreach (var line in File.ReadLines(path, Utf8))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var item = JsonSerializer.Deserialize<DutyHistoryEvent>(line, JsonOptions);
                    if (item is not null)
                        events.Add(item);
                }
                catch (JsonException ex)
                {
                    _logger?.LogDebug(ex, "跳过无法解析的值日表历史行");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "读取值日表历史失败：{Path}", path);
        }

        return events;
    }

    private string PathFor(DateOnly date) =>
        Path.Combine(_root, date.ToString("yyyy-MM-dd") + FileExtension);
}
