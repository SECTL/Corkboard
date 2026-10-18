using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Models.SubConfigs.Cloud;
using Corkboard.Core.Models.SubConfigs.Exam;
using Corkboard.Core.Models.SubConfigs.Export;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Models.SubConfigs.Personalized;
using Corkboard.Core.Models.SubConfigs.Update;
using Corkboard.Shared;
using Corkboard.Shared.Abstraction;

namespace Corkboard.Core.Models;

/// <summary>
///     主配置 <c>data/config/settings.json</c>。
///     <para>
///         新增配置项应挂到某个子配置下，而不是平铺到这里；
///         <see cref="Basic" /> 是历史路径 <c>general.basic</c> 的转发属性，只为兼容旧调用方。
///     </para>
/// </summary>
public partial class MainConfigModel : ConfigBase
{
    [ObservableProperty] private GeneralSettingsConfig _general = new();
    [ObservableProperty] private AppearanceSettingsConfig _appearance = new();
    [ObservableProperty] private BoardSettingsConfig _boardSettings = new();
    [ObservableProperty] private CloudSyncSettingsConfig _cloud = new();
    [ObservableProperty] private UpdateSettingsConfig _updateSettings = new();
    [ObservableProperty] private ExportSettingsConfig _exportSettings = new();

    /// <summary>考试看板的显示偏好（标题、阈值、时钟位置……）。考试安排本身在 <c>data/exam/</c>。</summary>
    [ObservableProperty] private ExamBoardSettingsConfig _examBoard = new();

    /// <summary>NTP 校时设置。看板时钟用它，其它界面不受影响。</summary>
    [ObservableProperty] private NtpSettingsConfig _ntp = new();

    [JsonIgnore] public override string ConfigFilePath => Utils.GetFilePath("config", "settings.json");

    /// <summary>
    ///     读盘后统一收拾一遍子配置：字号/宽度越界、收纳位置越界、手写笔宽的旧默认值迁移等。
    ///     由 <c>FileConfigService.LoadConfig</c> 在反序列化之后调用——**不要**挪进设置页：
    ///     作业板在设置页打开之前就会先按脏值渲染，手写表单也会继续用旧的默认笔宽。
    /// </summary>
    public void NormalizeLoaded()
    {
        BoardSettings.NormalizeBoardExtras();
        BoardSettings.NormalizeCleanup();
        BoardSettings.NormalizeHistory();
        ExportSettings.Normalize();
        UpdateSettings.Normalize();
        General.PrivacySettings.Normalize();
        ExamBoard.Normalize();
        Ntp.Normalize();
    }

    [JsonIgnore]
    public BasicSettingsConfig Basic
    {
        get => General.Basic;
        set => General.Basic = value;
    }

    /// <summary>只写属性：把旧版本平铺在根节点的 <c>basic</c> 段迁移进 <c>general.basic</c>。</summary>
    [JsonPropertyName("basic")]
    public BasicSettingsConfig LegacyBasicOnLoad
    {
        set
        {
            if (value is not null)
                General.Basic = value;
        }
    }
}
