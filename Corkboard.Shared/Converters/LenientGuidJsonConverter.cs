using System.Text.Json;
using System.Text.Json.Serialization;

namespace Corkboard.Shared.Converters;

/// <summary>
///     宽容的 Guid 读取器：旧配置可能把 Guid 写成空字符串、全零或带大括号，
///     直接反序列化会让整份配置回退到默认值，所以这里逐字段降级。
/// </summary>
public class LenientGuidJsonConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            return Guid.TryParse(raw, out var parsed) ? parsed : Guid.Empty;
        }

        if (reader.TokenType == JsonTokenType.Null)
            return Guid.Empty;

        reader.Skip();
        return Guid.Empty;
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("D"));
    }
}
