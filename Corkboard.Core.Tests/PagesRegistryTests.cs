using System.Text.Json;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Attributes;
using Corkboard.Core.Enums;
using Corkboard.Core.Extensions.Registry;
using Corkboard.Core.Models;
using Corkboard.Core.Services;
using Corkboard.Shared.Abstraction;
using Microsoft.Extensions.DependencyInjection;

namespace Corkboard.Core.Tests;

/// <summary>
///     页面注册表是「导航菜单 + 键控 DI」的双写入，测试盯住的是两处一致性：
///     没有 PageInfo 不能注册，重复 Id 必须失败，注册成功后必须能从 DI 按 Id 取到。
/// </summary>
public class PagesRegistryTests
{
    /// <summary>
    ///     注册表是静态集合（进程内只写一次，见 PagesRegistryService），
    ///     因此每个测试都要把它清干净，否则测试之间会互相污染。
    /// </summary>
    public PagesRegistryTests()
    {
        PagesRegistryService.MainItems.Clear();
        PagesRegistryService.SettingsItems.Clear();
        PagesRegistryService.GroupItems.Clear();
    }

    [PageInfo("test.registered", "\uE993")]
    private sealed class RegisteredPage : Avalonia.Controls.UserControl;

    private sealed class UnregisteredPage : Avalonia.Controls.UserControl;

    [Fact]
    public void AddMainPage_RegistersKeyedServiceAndNavigationEntry()
    {
        var services = new ServiceCollection();

        services.AddMainPage<RegisteredPage>("测试页");

        var info = PagesRegistryService.MainItems.Single(x => x.Id == "test.registered");
        Assert.Equal("测试页", info.Name);
        Assert.False(info.IsHide);

        using var provider = services.BuildServiceProvider();
        var page = provider.GetKeyedService<Avalonia.Controls.UserControl>("test.registered");
        Assert.IsType<RegisteredPage>(page);
    }

    [Fact]
    public void AddMainPage_WithoutPageInfoAttribute_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddMainPage<UnregisteredPage>("无注册信息"));
    }

    [Fact]
    public void AddMainPage_WithDuplicateId_Throws()
    {
        var services = new ServiceCollection();
        services.AddMainPage<RegisteredPage>("第一次");

        Assert.Throws<ArgumentException>(() => services.AddMainPage<RegisteredPage>("第二次"));
    }

    [Fact]
    public void AddGroup_WithDuplicateId_Throws()
    {
        var services = new ServiceCollection();
        services.AddGroup(new PageGroupInfo("通用", "test.group.dup", "\uE993"));

        Assert.Throws<ArgumentException>(
            () => services.AddGroup(new PageGroupInfo("通用2", "test.group.dup", "\uE993")));
    }
}
