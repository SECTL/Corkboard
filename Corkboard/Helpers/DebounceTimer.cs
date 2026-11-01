using Avalonia.Threading;

namespace Corkboard.Helpers;

/// <summary>
///     一次性防抖计时器：每次 <see cref="Schedule" /> 都把上一次顶掉，停手之后才真提交。
///     <para>
///         设置页里凡是「一路动一路变」的控件（滑杆、取色器、输入框）都要用它：
///         <c>ConfigHandlerBase</c> 每变一个属性就落一次盘（没有防抖），直接双向绑配置
///         等于拖一次滑杆写几十遍 <c>settings.json</c>。
///     </para>
///     <para>
///         <see cref="Flush" /> 与 <see cref="Cancel" /> 的区别只有一点：前者把压在计时里的
///     那次提交当场跑掉（页面离开可视树时用，别把用户最后一次选择丢了），后者直接扔掉
///     （别处已经提交过，再跑一次只会白写一遍盘）。
///     </para>
/// </summary>
public sealed class DebounceTimer
{
    private readonly DispatcherTimer _timer;
    private Action? _commit;

    public DebounceTimer(TimeSpan interval)
    {
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += OnTick;
    }

    /// <summary>还有一次提交压在计时里没跑。</summary>
    public bool IsPending => _commit is not null;

    /// <summary>排一次「停手后提交」：把上一次还没跑的顶掉，再起一个新的。</summary>
    public void Schedule(Action commit)
    {
        _commit = commit;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>把压在计时里的提交立刻跑掉。</summary>
    public void Flush() => TakePending()?.Invoke();

    /// <summary>扔掉还没跑的提交。</summary>
    public void Cancel() => TakePending();

    private void OnTick(object? sender, EventArgs e) => TakePending()?.Invoke();

    /// <summary>停表并把待提交的那一次取走（取走即清空，重复调用拿到 null）。</summary>
    private Action? TakePending()
    {
        _timer.Stop();

        var commit = _commit;
        _commit = null;

        return commit;
    }
}
