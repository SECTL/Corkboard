using System.Text.Encodings.Web;
using System.Text.Json;
using Corkboard.Core.Converters;
using Corkboard.Shared.Abstraction;

namespace Corkboard.Core.Abstraction;

/// <summary>
///     配置读写契约。磁盘格式（JSON、snake_case、缩进）由这里的 <see cref="JsonOptions" /> 统一决定，
///     所有配置文件必须共用同一份选项，否则同一份配置在不同服务里会解析出不同结果。
/// </summary>
public abstract class ConfigServiceBase
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new ColorJsonConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public abstract bool IsConfigExists<T>(T fallback) where T : ConfigBase;
    public abstract T LoadConfig<T>(T fallback) where T : ConfigBase;
    public abstract void SaveConfig<T>(T config) where T : ConfigBase;
    public abstract void DeleteConfig<T>(T config) where T : ConfigBase;
}
