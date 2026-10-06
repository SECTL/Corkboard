using Microsoft.Extensions.Hosting;

namespace Corkboard.Core.Abstraction;

/// <summary>
///     应用主机接口。
///     <para>
///         这是全应用唯一的静态服务定位入口：视图、控件和无法注入的旧代码都通过它取服务。
///         新代码优先用构造函数注入；只有在 Avalonia 控件构造期（DI 无法介入）才用这里。
///     </para>
/// </summary>
public interface IAppHost
{
    /// <summary>应用主机；由 App.BuildHost 在启动时写入。</summary>
    public static IHost? Host;

    /// <summary>获取指定的服务，取不到直接抛异常。</summary>
    public static T GetService<T>()
    {
        var s = Host?.Services.GetService(typeof(T));
        if (s != null) return (T)s;

        throw new ArgumentException($"Service {typeof(T)} is null!");
    }

    /// <summary>尝试获取指定的服务，取不到返回 null。</summary>
    public static T? TryGetService<T>()
    {
        return (T?)Host?.Services.GetService(typeof(T));
    }
}
