using Microsoft.Extensions.DependencyInjection;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms;

public static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformServices(
        this IServiceCollection services,
        IPlatformServiceRoot root)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(root);

        services.AddSingleton<IPlatformServiceRoot>(root);
        services.AddSingleton(root.Capabilities);
        services.AddSingleton<IWindowFeatureService>(root.WindowFeatures);
        services.AddSingleton<IAutostartService>(root.Autostart);
        return services;
    }
}
