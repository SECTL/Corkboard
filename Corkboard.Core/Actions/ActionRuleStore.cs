using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Corkboard.Core.Actions;

/// <summary>
///     <c>rules.json</c> 处于只读状态（读到更新版本的文件、或文件损坏后不让覆盖未知内容）时，
///     变更操作抛这个异常；界面据此禁用「新建 / 编辑 / 删除 / 启用」。
/// </summary>
public sealed class ActionStoreReadOnlyException : InvalidOperationException
{
    /// <summary>用一句说明建异常。</summary>
    public ActionStoreReadOnlyException(string message) : base(message)
    {
    }
}

/// <summary>
///     行动规则的存储：<c>action/rules.json</c>。
///     <para>
///         写盘一律<b>原子写</b>（先写 <c>rules.json.tmp</c> 再整体替换），断电不会留下半份文件；
///         读坏文件时把它备份成 <c>rules.bad-&lt;时间戳&gt;.json</c>、从空列表起、<see cref="IsReadOnly" /> 置真
///         （原文件保持不动，留给用户排查）；读到 <c>version</c> 比当前新时同样只读，绝不降级覆盖。
///     </para>
///     <para>
///         时间戳（<see cref="ActionRule.CreatedAt" /> / <see cref="ActionRule.UpdatedAt" />）由编辑器维护，
///         本类只原样落盘；只有 <see cref="ActionRule.Id" /> 为空的「全新规则」才会补一个新 Guid
///         （否则多条新规则会挤成一条）。
///     </para>
/// </summary>
public sealed class ActionRuleStore
{
    /// <summary>当前能读写的 rules.json 版本。</summary>
    public const int CurrentVersion = 1;

    private readonly ActionStorePaths _paths;
    private readonly ILogger<ActionRuleStore>? _logger;
    private readonly object _sync = new();

    private ActionRule[] _rules = [];
    private bool _readOnly;
    private string? _loadError;

    /// <summary>按路径表建存储并立即读一次盘。</summary>
    /// <param name="paths">数据文件位置。</param>
    /// <param name="logger">可选日志（App 的 DI 会注入；单测可以不传）。</param>
    public ActionRuleStore(ActionStorePaths paths, ILogger<ActionRuleStore>? logger = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger;
        Reload();
    }

    /// <summary>当前规则快照（只读；每次变更后会换成新数组，调用方不要往里写）。</summary>
    public IReadOnlyList<ActionRule> Rules => _rules;

    /// <summary>当前是不是只读（读坏文件、或文件版本比当前新）。</summary>
    public bool IsReadOnly => _readOnly;

    /// <summary>只读 / 读失败的原因（正常时为空），给界面顶部提示条用。</summary>
    public string? LoadError => _loadError;

    /// <summary>规则集合变化（含 <see cref="Reload" />）后触发，可能在后台线程。</summary>
    public event EventHandler? Changed;

    /// <summary>重新读盘。读坏文件时备份并从空列表起；这里永远不抛，让窗口照样能开。</summary>
    public void Reload()
    {
        ActionRule[] rules = [];
        var readOnly = false;
        string? loadError = null;

        try
        {
            if (File.Exists(_paths.RulesFile))
            {
                var json = File.ReadAllText(_paths.RulesFile);
                var file = JsonSerializer.Deserialize<ActionRulesFile>(json, ActionJson.Options);
                var version = file?.Version ?? CurrentVersion;

                rules = Sanitize(file?.Rules);

                if (version > CurrentVersion)
                {
                    readOnly = true;
                    loadError = $"rules.json 的版本是 {version}，当前版本只支持到 {CurrentVersion}，按只读打开";
                    _logger?.LogWarning("行动规则文件版本 {Version} 高于当前支持的 {Current}，只读打开", version,
                        CurrentVersion);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException or InvalidOperationException)
        {
            var backup = TryBackupBadFile();
            rules = [];
            readOnly = true;
            loadError = backup is null
                ? "rules.json 读取失败，已按只读打开（备份失败，原文件未改动）"
                : $"rules.json 读取失败，已备份为 {Path.GetFileName(backup)} 并按只读打开";
            _logger?.LogWarning(ex, "读取行动规则失败，已备份到 {Backup}", backup ?? "(备份失败)");
        }

        lock (_sync)
        {
            _rules = rules;
            _readOnly = readOnly;
            _loadError = loadError;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>新增或替换一条规则（按 <see cref="ActionRule.Id" /> 对齐）。</summary>
    public void Upsert(ActionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_sync)
        {
            ThrowIfReadOnly();

            var stored = NormalizeForSave(rule);
            var list = _rules.ToList();
            var index = list.FindIndex(existing => existing.Id == stored.Id);
            if (index >= 0)
                list[index] = stored;
            else
                list.Add(stored);

            var next = list.ToArray();
            WriteRules(next);
            _rules = next;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>删除一条规则；没有这条规则就不写盘、不通知。</summary>
    public void Remove(Guid id)
    {
        lock (_sync)
        {
            ThrowIfReadOnly();

            var next = _rules.Where(rule => rule.Id != id).ToArray();
            if (next.Length == _rules.Length)
                return;

            WriteRules(next);
            _rules = next;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>启用 / 停用一条规则（只改这一位，不动时间戳，也不动其它字段）。</summary>
    public void SetEnabled(Guid id, bool enabled)
    {
        lock (_sync)
        {
            ThrowIfReadOnly();

            var index = Array.FindIndex(_rules, rule => rule.Id == id);
            if (index < 0 || _rules[index].Enabled == enabled)
                return;

            var next = _rules.ToArray();
            next[index] = next[index] with { Enabled = enabled };
            WriteRules(next);
            _rules = next;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfReadOnly()
    {
        if (!_readOnly)
            return;

        throw new ActionStoreReadOnlyException(
            _loadError ?? "rules.json 处于只读状态，不能修改行动规则");
    }

    /// <summary>
    ///     「全新规则」兜底：Id 为空就补一个，时间戳缺省就用当前时间填上。
    ///     已经有值的一律原样保留（编辑器自己管时间戳）。
    /// </summary>
    private static ActionRule NormalizeForSave(ActionRule rule)
    {
        var now = DateTime.Now;
        var createdAt = rule.CreatedAt == default ? now : rule.CreatedAt;
        var updatedAt = rule.UpdatedAt == default ? createdAt : rule.UpdatedAt;

        return rule with
        {
            Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    private static ActionRule[] Sanitize(IEnumerable<ActionRule>? rules)
    {
        if (rules is null)
            return [];

        var result = new List<ActionRule>();
        var seen = new HashSet<Guid>();
        foreach (var rule in rules)
        {
            if (rule is null)
                continue;

            // 同一个 Id 出现两次时保留第一条：Upsert 的「按 Id 对齐」才不会有歧义。
            if (rule.Id != Guid.Empty && !seen.Add(rule.Id))
                continue;

            result.Add(rule);
        }

        return result.ToArray();
    }

    private void WriteRules(IReadOnlyList<ActionRule> rules)
    {
        Directory.CreateDirectory(_paths.Root);

        var file = new ActionRulesFile { Version = CurrentVersion, Rules = rules.ToList() };
        var json = JsonSerializer.Serialize(file, ActionJson.Options);
        var temp = _paths.RulesFile + ".tmp";

        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, _paths.RulesFile, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(cleanupError, "清理行动规则的临时文件失败：{Temp}", temp);
            }

            throw;
        }
    }

    private string? TryBackupBadFile()
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
            var target = Path.Combine(_paths.Root, $"rules.bad-{stamp}.json");
            File.Copy(_paths.RulesFile, target, overwrite: true);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "备份损坏的 rules.json 失败：{File}", _paths.RulesFile);
            return null;
        }
    }
}

/// <summary>rules.json 的外壳（<c>{ "version": 1, "rules": [...] }</c>）。</summary>
internal sealed class ActionRulesFile
{
    /// <summary>格式版本。</summary>
    public int Version { get; set; } = ActionRuleStore.CurrentVersion;

    /// <summary>规则列表。</summary>
    public List<ActionRule>? Rules { get; set; } = [];
}
