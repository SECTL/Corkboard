using Corkboard.Core.Abstraction;
using Corkboard.Core.Enums.Configs;
using Corkboard.Core.Models;
using Corkboard.Core.Models.SubConfigs.General;
using Microsoft.Extensions.Logging.Abstractions;

namespace Corkboard.Core.Tests;

/// <summary>
///     配置自动保存契约：根属性与任意深度的子配置属性变化都必须落盘，且一次变化只写一次。
///     破了这条，设置页改的东西（语言、开关、窗口尺寸记忆）重启就丢——见 AGENTS.md NOTES。
/// </summary>
public class ConfigAutoSaveTests
{
    private sealed class RecordingConfigService : ConfigServiceBase
    {
        public int SaveCount;

        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => fallback;
        public override void SaveConfig<T>(T config) => SaveCount++;
        public override void DeleteConfig<T>(T config) { }
    }

    private sealed class TestHandler(ConfigServiceBase service)
        : ConfigHandlerBase<MainConfigModel>(NullLogger<TestHandler>.Instance, service, () => new MainConfigModel());

    [Fact]
    public void RootPropertyChange_SavesOnce()
    {
        var service = new RecordingConfigService();
        var handler = new TestHandler(service);

        handler.Data.General = new GeneralSettingsConfig();

        Assert.Equal(1, service.SaveCount);
    }

    [Fact]
    public void NestedSubConfigChange_SavesOnce()
    {
        var service = new RecordingConfigService();
        var handler = new TestHandler(service);

        // General.Basic 是第二层子配置：设置页的语言、各开关都挂在它下面。
        handler.Data.Basic.Language = LanguageMode.English;

        Assert.Equal(1, service.SaveCount);
        Assert.Equal(LanguageMode.English, handler.Data.Basic.Language);
    }

    [Fact]
    public void SubConfigChangeAtAnotherBranch_SavesOnce()
    {
        var service = new RecordingConfigService();
        var handler = new TestHandler(service);

        handler.Data.BoardSettings.ConfirmBeforeDelete = false;
        handler.Data.Appearance.Theme = ThemeMode.Dark;

        Assert.Equal(2, service.SaveCount);
    }

    [Fact]
    public void ReplacedSubConfig_IsObservedToo()
    {
        var service = new RecordingConfigService();
        var handler = new TestHandler(service);

        // 先整体换掉通用设置分组，再改新分组里的子配置：新对象也必须被订阅。
        handler.Data.General = new GeneralSettingsConfig();
        var afterReplace = service.SaveCount;
        handler.Data.Basic.Language = LanguageMode.English;

        Assert.Equal(afterReplace + 1, service.SaveCount);
    }

    [Fact]
    public void Reload_DropsSubscriptionsOfOldModel()
    {
        var service = new RecordingConfigService();
        var handler = new TestHandler(service);
        var staleBasic = handler.Data.Basic;

        handler.Reload();
        var afterReload = service.SaveCount;

        staleBasic.Language = LanguageMode.English;
        Assert.Equal(afterReload, service.SaveCount);

        handler.Data.Basic.Language = LanguageMode.English;
        Assert.Equal(afterReload + 1, service.SaveCount);
    }
}
