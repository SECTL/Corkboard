namespace Corkboard.Core.Services.Stats;

/// <summary>
///     本机累计、还没上报成功的使用量增量。线程安全：记录来自 UI 线程，上报在后台线程。
/// </summary>
/// <remarks>
///     计数与「已上报」的界线由 <see cref="TakePending" /> 划：它把当前增量整批取走并清空，
///     上报失败时再由调用方 <see cref="Restore" /> 放回去，这样中途新记的增量不会被覆盖。
/// </remarks>
public sealed class UsageMetricCounter
{
    private readonly Dictionary<string, long> _pending = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>有没有还没上报的增量。</summary>
    public bool HasPending
    {
        get
        {
            lock (_gate)
                return _pending.Count > 0;
        }
    }

    /// <summary>累加一项指标。</summary>
    /// <exception cref="ArgumentOutOfRangeException">指标名为空或增量不是正数。</exception>
    public void Add(string metric, long delta = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metric);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(delta);

        lock (_gate)
        {
            _pending[metric] = _pending.TryGetValue(metric, out var current) ? current + delta : delta;
        }
    }

    /// <summary>取走全部待上报增量并清空本地记录；没有增量时返回空字典。</summary>
    public IReadOnlyDictionary<string, long> TakePending()
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
                return new Dictionary<string, long>(0, StringComparer.Ordinal);

            var taken = new Dictionary<string, long>(_pending, StringComparer.Ordinal);
            _pending.Clear();
            return taken;
        }
    }

    /// <summary>把上报失败的增量放回队列（与这段时间新记的增量相加）。</summary>
    public void Restore(IReadOnlyDictionary<string, long>? failed)
    {
        if (failed is null || failed.Count == 0)
            return;

        lock (_gate)
        {
            foreach (var (metric, delta) in failed)
            {
                if (string.IsNullOrWhiteSpace(metric) || delta <= 0)
                    continue;

                _pending[metric] = _pending.TryGetValue(metric, out var current) ? current + delta : delta;
            }
        }
    }

    /// <summary>当前待上报增量的只读快照（写盘用，不清空）。</summary>
    public IReadOnlyDictionary<string, long> Snapshot()
    {
        lock (_gate)
            return new Dictionary<string, long>(_pending, StringComparer.Ordinal);
    }

    /// <summary>清空本地记录（只给「已确认上报成功」的路径用）。</summary>
    public void Clear()
    {
        lock (_gate)
            _pending.Clear();
    }
}
