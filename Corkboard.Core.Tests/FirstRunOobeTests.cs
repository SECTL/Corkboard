using Corkboard.Core.Abstraction;
using Corkboard.Core.Models;
using Corkboard.Core.Services.Config;
using Corkboard.Core.Services.FirstRun;
using Microsoft.Extensions.Logging.Abstractions;

namespace Corkboard.Core.Tests;

/// <summary>
///     首次运行引导（OOBE）的判定契约：什么时候整段引导、什么时候只补协议、什么时候什么都不弹。
///     这几条一旦破了，老用户每次启动都会被拽回引导，或者新用户直接跳过隐私政策。
/// </summary>
public class FirstRunOobeTests
{
    private sealed class RecordingConfigService : ConfigServiceBase
    {
        public int SaveCount;

        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => fallback;
        public override void SaveConfig<T>(T config) => SaveCount++;
        public override void DeleteConfig<T>(T config) { }
    }

    private static (FirstRunOobeService Oobe, MainConfigHandler Handler, RecordingConfigService Service) Create()
    {
        var service = new RecordingConfigService();
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, service);
        return (new FirstRunOobeService(handler), handler, service);
    }

    [Fact]
    public void FreshConfig_RequiresFullSetup()
    {
        var (oobe, _, _) = Create();

        Assert.Equal(OobeRequirement.FullSetup, oobe.Requirement);
        Assert.True(oobe.IsRequired);
    }

    [Fact]
    public void CompletedGuide_RequiresNothing()
    {
        var (oobe, handler, _) = Create();
        oobe.Complete();

        Assert.Equal(OobeRequirement.None, oobe.Requirement);
        Assert.False(oobe.IsRequired);
        Assert.True(handler.Data.Basic.GuideCompleted);
    }

    [Fact]
    public void Complete_WritesAllThreeKeys()
    {
        var (oobe, handler, service) = Create();

        oobe.Complete();

        // 每个属性变化各落一次盘（ConfigHandlerBase 没有批量写），最后一次 Save 是兜底。
        Assert.True(service.SaveCount >= 3);
        Assert.True(handler.Data.Basic.GuideCompleted);
        Assert.Equal(FirstRunOobeService.CurrentEulaVersion, handler.Data.Basic.AcceptedEulaVersion);
        Assert.Equal(FirstRunOobeService.CurrentPrivacyPolicyVersion,
            handler.Data.Basic.AcceptedPrivacyPolicyVersion);
    }

    [Fact]
    public void Complete_SetsGuideCompletedLast()
    {
        var (oobe, handler, _) = Create();
        var order = new List<string>();
        handler.Data.Basic.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
                order.Add(e.PropertyName);
        };

        oobe.Complete();

        // GuideCompleted 必须是最后一个写的：中途崩掉时盘上仍是「没走完」，下次重新引导，
        // 而不是「算走完了但协议没确认」。
        Assert.Equal(nameof(handler.Data.Basic.GuideCompleted), order[^1]);
    }

    [Fact]
    public void GuideCompletedButStaleLegalVersions_RequiresLegalOnly()
    {
        var (oobe, handler, _) = Create();
        // 走过引导，但协议版本是旧的一版（模拟协议改版后的老配置）。
        handler.Data.Basic.GuideCompleted = true;
        handler.Data.Basic.AcceptedEulaVersion = FirstRunOobeService.CurrentEulaVersion - 1;
        handler.Data.Basic.AcceptedPrivacyPolicyVersion = FirstRunOobeService.CurrentPrivacyPolicyVersion;

        Assert.Equal(OobeRequirement.LegalOnly, oobe.Requirement);
        Assert.True(oobe.IsRequired);
    }

    [Fact]
    public void GuideCompletedButStalePrivacyVersion_RequiresLegalOnly()
    {
        var (oobe, handler, _) = Create();
        handler.Data.Basic.GuideCompleted = true;
        handler.Data.Basic.AcceptedEulaVersion = FirstRunOobeService.CurrentEulaVersion;
        handler.Data.Basic.AcceptedPrivacyPolicyVersion = FirstRunOobeService.CurrentPrivacyPolicyVersion - 1;

        Assert.Equal(OobeRequirement.LegalOnly, oobe.Requirement);
    }

    [Fact]
    public void BoardEnabled_DefaultsToTrueAndRoundTrips()
    {
        var config = new MainConfigModel();
        Assert.True(config.BoardSettings.BoardEnabled);
    }
}
