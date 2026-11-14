using System.Text.Json.Nodes;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     一条指令的执行结果。成功时 <see cref="Data" /> 原样嵌进 <c>cmd.result</c> 的 <c>data</c>，
///     失败时带 <see cref="Code" />（缺省 <c>failed</c>）。
/// </summary>
/// <param name="Ok">是否执行成功。</param>
/// <param name="Data">要回给集控中心的数据（永远不是 <c>null</c>，失败时是空对象）。</param>
/// <param name="Code">失败码（<c>timeout</c> / <c>capability_unsupported</c> / …）。</param>
/// <param name="Message">可选的补充说明。</param>
public sealed record ControlCommandOutcome(bool Ok, JsonObject Data, string? Code = null, string? Message = null)
{
    /// <summary>成功。</summary>
    public static ControlCommandOutcome Success(JsonObject data) => new(true, data);

    /// <summary>失败（<paramref name="code" /> 就是协议里的错误码）。</summary>
    public static ControlCommandOutcome Failure(string code, string? message = null) =>
        new(false, new JsonObject(), code, message);
}

/// <summary>
///     <c>command_id</c> 幂等表：同一个 <c>command_id</c> 重投时**不重复执行**，直接把上次的结果再回一遍。
///     <para>
///         协议 §3.4 要求设备侧按 <c>command_id</c> 去重（服务端会在没收到 ack/result 时重投），
///         但服务端**不补投离线命令**——所以这里只是一个有界内存表，不需要落盘：
///         进程重启之后同一批指令本来就不会再来。
///     </para>
///     <para>
///         两个界：LRU 容量（默认 512，与参考实现一致）与 TTL（默认 10 分钟）。
///         TTL 从**首次记录**起算；被命中不会延长寿命，只把 LRU 顺序提到最新。
///     </para>
/// </summary>
public sealed class ControlCommandLedger
{
    /// <summary>默认最多记多少条（协议与参考实现都是 512）。</summary>
    public const int DefaultCapacity = 512;

    /// <summary>默认存活多久：教室机断网重连一次不会超过这个量级。</summary>
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromMinutes(10);

    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly LinkedList<Entry> _order = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeToLive;

    /// <summary>
    ///     建一张幂等表。
    /// </summary>
    /// <param name="capacity">最多记多少条。</param>
    /// <param name="timeToLive">记多久（<c>null</c> 用 <see cref="DefaultTimeToLive" />）。</param>
    /// <param name="timeProvider">时钟（测试可注入假时钟）。</param>
    public ControlCommandLedger(
        int capacity = DefaultCapacity,
        TimeSpan? timeToLive = null,
        TimeProvider? timeProvider = null)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "容量必须为正数");
        _capacity = capacity;
        _timeToLive = timeToLive ?? DefaultTimeToLive;
        if (_timeToLive <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeToLive), _timeToLive, "存活时间必须为正数");
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>当前还记着多少条（已过期的会被顺手清掉）。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                PruneExpired();
                return _entries.Count;
            }
        }
    }

    /// <summary>
    ///     取上次的结果。命中即「这条指令来过了」，调用方**不要**再执行。
    /// </summary>
    public bool TryGet(string? commandId, out ControlCommandOutcome? outcome)
    {
        outcome = null;
        if (string.IsNullOrWhiteSpace(commandId))
            return false;

        lock (_gate)
        {
            if (!_entries.TryGetValue(commandId, out var node))
                return false;

            if (IsExpired(node.Value))
            {
                RemoveNode(node);
                return false;
            }

            // 命中只刷新 LRU 顺序，不刷新时间戳（TTL 从首次记录起算，重投不能把条目续命到永远）。
            _order.Remove(node);
            _order.AddFirst(node);
            outcome = node.Value.Outcome;
            return true;
        }
    }

    /// <summary>
    ///     记下一条指令的结果。已有未过期条目时返回 <c>false</c>（表示「这条已经记过了」，
    ///     调用方此时应该用 <see cref="TryGet" /> 把旧结果回给对端）。
    /// </summary>
    public bool TryRemember(string? commandId, ControlCommandOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (string.IsNullOrWhiteSpace(commandId))
            return false;

        lock (_gate)
        {
            if (_entries.TryGetValue(commandId, out var existing))
            {
                if (!IsExpired(existing.Value))
                    return false;
                RemoveNode(existing);
            }

            var node = _order.AddFirst(new Entry(commandId, outcome, _timeProvider.GetUtcNow()));
            _entries[commandId] = node;
            TrimToCapacity();
            return true;
        }
    }

    /// <summary>清空（「取消接入」时顺手清一下，避免旧地点/旧班级的指令结果串到新设备上）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
        }
    }

    private void TrimToCapacity()
    {
        while (_entries.Count > _capacity && _order.Last is { } last)
            RemoveNode(last);
    }

    private void PruneExpired()
    {
        var node = _order.Last;
        while (node is not null)
        {
            var previous = node.Previous;
            if (IsExpired(node.Value))
                RemoveNode(node);
            node = previous;
        }
    }

    private void RemoveNode(LinkedListNode<Entry> node)
    {
        _order.Remove(node);
        _entries.Remove(node.Value.CommandId);
    }

    private bool IsExpired(Entry entry) => _timeProvider.GetUtcNow() - entry.RecordedAt >= _timeToLive;

    private sealed class Entry(string commandId, ControlCommandOutcome outcome, DateTimeOffset recordedAt)
    {
        public string CommandId { get; } = commandId;
        public ControlCommandOutcome Outcome { get; } = outcome;
        public DateTimeOffset RecordedAt { get; } = recordedAt;
    }
}
