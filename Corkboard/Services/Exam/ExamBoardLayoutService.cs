using System.ComponentModel;
using Avalonia.Threading;
using Corkboard.Core.Exam;
using Corkboard.Core.Exam.Layout;
using Corkboard.Core.Models;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Services.Config;

namespace Corkboard.Services.Exam;

/// <summary>
///     看板布局配置的变更广播。放映窗的 ViewModel 只订阅这一个事件，不直接摸配置树：
///     编辑器提交时整份 <see cref="ExamBoardLayoutConfig" /> 会被替换，设置页/恢复默认则是原地改子对象。
/// </summary>
public sealed class ExamBoardLayoutService : IDisposable
{
    private readonly MainConfigHandler _config;
    private ExamBoardLayoutConfig? _layout;
    private bool _disposed;

    public ExamBoardLayoutService(MainConfigHandler config)
    {
        _config = config;
        _config.Reloaded += OnConfigReloaded;
        _config.Data.PropertyChanged += OnConfigPropertyChanged;
        Attach(_config.Data.ExamBoardLayout);
    }

    public event EventHandler? Changed;

    private void OnConfigReloaded(object? sender, EventArgs e)
    {
        _config.Data.PropertyChanged -= OnConfigPropertyChanged;
        _config.Data.PropertyChanged += OnConfigPropertyChanged;
        Detach();
        Attach(_config.Data.ExamBoardLayout);
        RaiseChanged();
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(MainConfigModel.ExamBoardLayout))
        {
            return;
        }

        Detach();
        Attach(_config.Data.ExamBoardLayout);
        RaiseChanged();
    }

    private void Attach(ExamBoardLayoutConfig layout)
    {
        _layout = layout;
        layout.PropertyChanged += OnLayoutChanged;
        foreach (var component in Enum.GetValues<ExamStatus>())
        {
            var status = layout.For(component);
            status.PropertyChanged += OnLayoutChanged;
            status.Title.PropertyChanged += OnLayoutChanged;
            status.Clock.PropertyChanged += OnLayoutChanged;
            status.ExamInfo.PropertyChanged += OnLayoutChanged;
            status.ExamTable.PropertyChanged += OnLayoutChanged;
            status.ExitCorner.PropertyChanged += OnLayoutChanged;
        }
    }

    private void Detach()
    {
        if (_layout is null)
        {
            return;
        }

        _layout.PropertyChanged -= OnLayoutChanged;
        foreach (var component in Enum.GetValues<ExamStatus>())
        {
            var status = _layout.For(component);
            status.PropertyChanged -= OnLayoutChanged;
            status.Title.PropertyChanged -= OnLayoutChanged;
            status.Clock.PropertyChanged -= OnLayoutChanged;
            status.ExamInfo.PropertyChanged -= OnLayoutChanged;
            status.ExamTable.PropertyChanged -= OnLayoutChanged;
            status.ExitCorner.PropertyChanged -= OnLayoutChanged;
        }

        _layout = null;
    }

    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e) => RaiseChanged();

    private void RaiseChanged()
    {
        if (_disposed)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _config.Reloaded -= OnConfigReloaded;
        _config.Data.PropertyChanged -= OnConfigPropertyChanged;
        Detach();
    }
}