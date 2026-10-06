using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Corkboard.Shared.Abstraction;

namespace Corkboard.Core.Abstraction;

/// <summary>
///     配置句柄基类：加载一份配置、在属性变化时自动保存，并提供重载/删除能力。
///     <para>
///         自动保存是刻意的设计——设置页只改属性，不需要记得调用 Save。
///         批量写入时用 <see cref="Save" /> 前先断开事件，或在模型里加批量方法，避免逐字段落盘。
///     </para>
/// </summary>
public abstract class ConfigHandlerBase<T> where T : ConfigBase
{
    protected ConfigHandlerBase(Func<T> fallbackFactory)
    {
        Logger = (ILogger)IAppHost.Host?.Services.GetService(typeof(ILogger<>).MakeGenericType(GetType()))!;
        ConfigService = IAppHost.GetService<ConfigServiceBase>();
        FallbackFactory = fallbackFactory;

        Logger.LogInformation("Loading config file.");
        Data = ConfigService.LoadConfig(FallbackFactory());
        Data.PropertyChanged += Data_OnPropertyChanged;
    }

    protected ConfigHandlerBase(ILogger logger, ConfigServiceBase configService, Func<T> fallbackFactory)
    {
        Logger = logger;
        ConfigService = configService;
        FallbackFactory = fallbackFactory;

        Logger.LogInformation("Loading config file.");
        Data = ConfigService.LoadConfig(FallbackFactory());
        Data.PropertyChanged += Data_OnPropertyChanged;
    }

    public T Data { get; private set; }
    public event EventHandler? Reloaded;

    /// <summary>配置文件成功写入磁盘后触发，必须在 <c>SaveConfig</c> 之后。</summary>
    public event EventHandler? Saved;

    private ILogger Logger { get; }
    private ConfigServiceBase ConfigService { get; }
    private Func<T> FallbackFactory { get; }

    public virtual void Reload()
    {
        Data.PropertyChanged -= Data_OnPropertyChanged;
        Logger.LogInformation("Reloading config file.");
        Data = ConfigService.LoadConfig(FallbackFactory());
        Data.PropertyChanged += Data_OnPropertyChanged;
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Save()
    {
        Logger.LogInformation("Saving config file.");
        ConfigService.SaveConfig(Data);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Delete()
    {
        Logger.LogInformation("Deleting config file.");
        ConfigService.DeleteConfig(Data);
    }

    protected virtual void Data_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Save();
    }
}
