using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Controls.Converters;
using Avalonia.Media;

namespace Corkboard.Core.Converters;

/// <summary>
///     Avalonia <see cref="Color" /> 的 JSON 读写：写出去是 <c>#RRGGBBAA</c>（alpha 在后），
///     读回来同时接受十六进制字符串和 <c>{A,R,G,B}</c> 对象（旧版本写出的形态）。
///     <para>
///         注意 alpha 在**后**：<c>Color.Parse("#FF00AAFF")</c> 那种写法是按「alpha 在前」解析的，
///         和磁盘上的顺序相反，手写配置或写测试时不要混。
///     </para>
/// </summary>
public class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return ColorToHexConverter.ParseHexString(reader.GetString() ?? "", AlphaComponentPosition.Trailing) ??
                   default;

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            byte a = 0, r = 0, g = 0, b = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException();

                var propertyName = reader.GetString();
                reader.Read();

                switch (propertyName)
                {
                    case "A":
                        a = reader.GetByte();
                        break;
                    case "R":
                        r = reader.GetByte();
                        break;
                    case "G":
                        g = reader.GetByte();
                        break;
                    case "B":
                        b = reader.GetByte();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new Color(a, r, g, b);
        }

        reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(ColorToHexConverter.ToHexString(value, AlphaComponentPosition.Trailing,
            includeSymbol: true));
    }
}
