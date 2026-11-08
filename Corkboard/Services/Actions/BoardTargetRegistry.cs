using System.ComponentModel;
using Corkboard.Core.Actions;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Services.Config;
using Microsoft.Extensions.Logging;

namespace Corkboard.Services.Actions;

/// <summary>
///     看板目标注册表：把 DI 里注册的 <see cref="IBoardTarget" /> 收集成一张只读表，
///     并在「启用作业板」变化 / 配置被云端整份替换后发 <see cref="Changed" />，
///     让编辑器与列表刷新「哪些目标现在可用」。
///     <para>
///         目标顺序就是 DI 注册顺序（作业板 → 考试看板 → 看板模式），
///         列表与下拉框直接按这个顺序显示，不需要再排一次。
///     </para>
/// </summary>
public sealed class BoardTargetRegistry : IBoardTargetRegistry, IDisposable
{
    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<BoardTargetRegistry> _logger;
    private BoardSettingsConfig? _boardSettings;
    private bool _disposed;

    /// <summary>构造注册表。目标列表在构造期定型（值是 DI 单例，运行期不会增删）。</summary>
    public BoardTargetRegistry(
        IEnumerable<IBoardTarget> targets,
        MainConfigHandler configHandler,
        ILogger<BoardTargetRegistry> logger)
    {
        Targets = targets.ToList();
        _configHandler = configHandler;
        _logger = logger;

        Subscribe();
        _configHandler.Reloaded += OnConfigReloaded;
    }

    /// <inheritdoc />
    public IReadOnlyList<IBoardTarget> Targets { get; }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IBoardTarget? Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return Targets.FirstOrDefault(target => string.Equals(target.Id, id, StringComparison.Ordinal));
    }

    /// <summary>配置被云端恢复 / 从磁盘重读：订阅换到新的子配置对象上，并通知界面重算可用性。</summary>
    private void OnConfigReloaded(object? sender, EventArgs e)
    {
        Unsubscribe();
        Subscribe();
        RaiseChanged();
    }

    private void Subscribe()
    {
        _boardSettings = _configHandler.Data.BoardSettings;
        _boardSettings.PropertyChanged += OnBoardSettingsChanged;
    }

    private void Unsubscribe()
    {
        if (_boardSettings is not null)
            _boardSettings.PropertyChanged -= OnBoardSettingsChanged;

        _boardSettings = null;
    }

    private void OnBoardSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(BoardSettingsConfig.BoardEnabled), StringComparison.Ordinal))
            return;

        _logger.LogDebug("「启用作业板」已变化，刷新看板目标可用性");
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            // 订阅方（界面）的异常不能把配置变化的通知链打断。
            _logger.LogError(exception, "通知看板目标变化失败");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _configHandler.Reloaded -= OnConfigReloaded;
        Unsubscribe();
    }
}
