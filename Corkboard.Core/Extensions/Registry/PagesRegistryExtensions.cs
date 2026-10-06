using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Corkboard.Core.Attributes;
using Corkboard.Core.Enums;
using Corkboard.Core.Models;
using Corkboard.Core.Services;

namespace Corkboard.Core.Extensions.Registry;

/// <summary>
///     页面注册扩展：把页面同时写进导航注册表和键控 DI（key = 页面 Id），
///     导航壳再按 Id 从 DI 取实例。两处必须成对，缺一个就会出现「菜单有项、点开空白」。
/// </summary>
public static class PagesRegistryExtensions
{
    public static IServiceCollection AddMainPage<T>(this IServiceCollection services, string name) where T : UserControl
    {
        return services.AddPageTo<T>(PagesRegistryService.MainItems, name);
    }

    public static IServiceCollection AddMainPageSeparator(this IServiceCollection services,
        PageLocation location = PageLocation.Top)
    {
        PagesRegistryService.MainItems.Add(new PageInfo(true, location));
        return services;
    }

    public static IServiceCollection AddSettingsPage<T>(this IServiceCollection services, string name)
        where T : UserControl
    {
        return services.AddPageTo<T>(PagesRegistryService.SettingsItems, name);
    }

    public static IServiceCollection AddSettingsPageSeparator(this IServiceCollection services,
        PageLocation location = PageLocation.Top, bool isHide = false)
    {
        PagesRegistryService.SettingsItems.Add(new PageInfo(true, location) { IsHide = isHide });
        return services;
    }

    public static IServiceCollection AddGroup(this IServiceCollection services, PageGroupInfo info)
    {
        if (PagesRegistryService.GroupItems.FirstOrDefault(x => x.Id == info.Id) != null)
            throw new ArgumentException($"此设置页面id {info.Id} 已经被占用。");

        PagesRegistryService.GroupItems.Add(info);
        return services;
    }

    private static IServiceCollection AddPageTo<T>(this IServiceCollection services, IList<PageInfo> list, string name)
        where T : UserControl
    {
        var type = typeof(T);
        if (type.GetCustomAttributes(false).FirstOrDefault(x => x is PageInfo) is not PageInfo info)
            throw new ArgumentException($"无法注册设置页面 {type.FullName}，因为设置页面没有注册信息。");

        if (list.FirstOrDefault(x => x.Id == info.Id) != null) throw new ArgumentException($"此设置页面id {info.Id} 已经被占用。");

        info.Name = name;
        info.SettingsPageType = typeof(T);
        services.AddKeyedTransient<UserControl, T>(info.Id);
        list.Add(info);
        return services;
    }
}
