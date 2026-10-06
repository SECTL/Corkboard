using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Corkboard.Core.Models.SubConfigs.Board;
using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Models.SubConfigs.Personalized;
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

    [JsonIgnore] public override string ConfigFilePath => Utils.GetFilePath("config", "settings.json");

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
