using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Corkboard.Shared.Abstraction;

namespace Corkboard.Core.Abstraction;

/// <summary>
///     配置句柄基类：加载一份配置、在属性变化时自动保存，并提供重载/删除能力。
///     <para>
///         自动保存是刻意的设计——设置页只改属性，不需要记得调用 Save。
///         订阅是**递归**的：根配置和任意深度的子配置（以及集合对象）都挂在同一个处理器上，
///         所以「新配置项挂到子配置下」也不会漏掉落盘，模型侧不需要额外接线。
///     </para>
///     <para>
///         批量写入时用 <see cref="Save" /> 前先断开事件，或在模型里加批量方法，避免逐字段落盘。
///     </para>
/// </summary>
public abstract class ConfigHandlerBase<T> where T : ConfigBase
{
    /// <summary>已订阅的对象（按引用比较）：同一实例只订阅一次，避免一次变更写多遍。</summary>
    private readonly HashSet<INotifyPropertyChanged> _observed = new(ReferenceEqualityComparer.Instance);

    protected ConfigHandlerBase(Func<T> fallbackFactory)
    {
        Logger = (ILogger)IAppHost.Host?.Services.GetService(typeof(ILogger<>).MakeGenericType(GetType()))!;
        ConfigService = IAppHost.GetService<ConfigServiceBase>();
        FallbackFactory = fallbackFactory;

        Logger.LogInformation("Loading config file.");
        Data = ConfigService.LoadConfig(FallbackFactory());
        Attach(Data);
    }

    protected ConfigHandlerBase(ILogger logger, ConfigServiceBase configService, Func<T> fallbackFactory)
    {
        Logger = logger;
        ConfigService = configService;
        FallbackFactory = fallbackFactory;

        Logger.LogInformation("Loading config file.");
        Data = ConfigService.LoadConfig(FallbackFactory());
        Attach(Data);
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
        Detach();
        Logger.LogInformation("Reloading config file.");
        Data = ConfigService.LoadConfig(FallbackFactory());
        Attach(Data);
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
        // 子配置的变更也订阅在这里；如果这次变更整体换掉了某个子对象（Reload、模型迁移、直接赋值），
        // 把新对象一起纳入订阅，否则新对象之后的改动又会静默丢失。
        if (sender is not null && !string.IsNullOrEmpty(e.PropertyName)
                                && sender.GetType().GetProperty(e.PropertyName) is { CanRead: true } property
                                && typeof(INotifyPropertyChanged).IsAssignableFrom(property.PropertyType))
            Attach(property.GetValue(sender));

        Save();
    }

    /// <summary>
    ///     递归订阅配置对象图：根配置与任意深度的子配置都接到 <see cref="Data_OnPropertyChanged" />。
    ///     <c>ObservableObject</c> 不会把子对象的变更冒泡给父对象，做不到这一步就只有根属性会落盘。
    /// </summary>
    private void Attach(object? model)
    {
        if (model is not INotifyPropertyChanged observable || !_observed.Add(observable))
            return;

        observable.PropertyChanged += Data_OnPropertyChanged;

        foreach (var property in ObservableChildProperties(model.GetType()))
            Attach(property.GetValue(model));
    }

    private void Detach()
    {
        foreach (var observable in _observed)
            observable.PropertyChanged -= Data_OnPropertyChanged;

        _observed.Clear();
    }

    /// <summary>只往下走「可能挂子配置/集合」的属性，跳过字符串、路径这类无关注解。</summary>
    private static IEnumerable<PropertyInfo> ObservableChildProperties(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            if (typeof(INotifyPropertyChanged).IsAssignableFrom(property.PropertyType))
                yield return property;
        }
    }
}
