using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Abstraction.Services;
using Corkboard.Core.Services.Board;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Services.Logging;

namespace Corkboard.Core.Services;

/// <summary>
///     Core 运行时的 DI 装配入口。<c>App.BuildHost</c> 必须调用 <see cref="AddCoreRuntimeServices" />，
///     各业务模块再各自往同一个容器里追加自己的注册。
/// </summary>
public static class CoreRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddCoreRuntimeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ConfigServiceBase, FileConfigService>();
        services.AddSingleton<MainConfigHandler>();
        services.AddSingleton<IBoardService, BoardService>();
        return services;
    }

    /// <summary>接上文件日志。控制台日志由宿主按环境决定是否追加。</summary>
    public static ILoggingBuilder AddCoreFileLogger(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddProvider(new FileLoggerProvider());
        return builder;
    }
}
