using System.Text.Json;
using Corkboard.Core.Models.SubConfigs.ControlCenter;

namespace Corkboard.Core.Tests;

/// <summary>
///     集控配置里「作业自动备份」的默认值与归一化：默认开（只往外传只读快照，不算远程控制），
///     <see cref="ControlCenterSettingsConfig.Normalize" /> 不该碰它。
/// </summary>
public class ControlCenterSettingsConfigTests
{
    [Fact]
    public void Defaults_HomeworkBackupOn_ControlCenterOff()
    {
        var config = new ControlCenterSettingsConfig();

        Assert.True(config.HomeworkBackupEnabled);
        Assert.False(config.Enabled);
        Assert.Equal(ControlCenterSettingsConfig.DefaultServerUrl, config.ServerUrl);
    }

    [Fact]
    public void Deserialize_MissingHomeworkBackupKey_KeepsDefaultOn()
    {
        // 老配置里没有这个键（首次升级上来的用户）：缺键时保留默认值，不是「关掉」。
        var config = JsonSerializer.Deserialize<ControlCenterSettingsConfig>("{}");

        Assert.NotNull(config);
        Assert.True(config!.HomeworkBackupEnabled);
    }

    [Fact]
    public void Deserialize_ExplicitFalse_IsPreserved()
    {
        var config = JsonSerializer.Deserialize<ControlCenterSettingsConfig>(
            """{"HomeworkBackupEnabled":false}""");

        Assert.NotNull(config);
        Assert.False(config!.HomeworkBackupEnabled);
    }

    [Fact]
    public void Normalize_DoesNotTouchHomeworkBackup()
    {
        var config = new ControlCenterSettingsConfig
        {
            HomeworkBackupEnabled = false,
            ServerUrl = "sectl.cn/",
            SiteId = "sc-6m34rq"
        };

        config.Normalize();

        Assert.False(config.HomeworkBackupEnabled);
        Assert.Equal("https://sectl.cn", config.ServerUrl);
        Assert.Equal("SC-6M34RQ", config.SiteId);
    }

    [Fact]
    public void Normalize_KeepsHomeworkBackupOn()
    {
        var config = new ControlCenterSettingsConfig { HomeworkBackupEnabled = true };

        config.Normalize();

        Assert.True(config.HomeworkBackupEnabled);
    }
}