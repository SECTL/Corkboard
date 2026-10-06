using System.Text.Json;
using System.Text.Json.Serialization;
using Corkboard.Core.Enums.Configs;

namespace Corkboard.Core.Converters;

/// <summary>
///     界面语言的 JSON 读写：写出去仍是数字（与上游「枚举按数字落盘」的约定一致），
///     读回来时把已移除的日语（2）以及任何越界值折算成 <see cref="LanguageMode.ChineseSimplified" />，
///     免得旧配置在内存里留下一个枚举未定义值。名字形态的字符串也照收，方便手改配置。
/// </summary>
public class LanguageModeJsonConverter : JsonConverter<LanguageMode>
{
    public override LanguageMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var mode = reader.TokenType switch
        {
            JsonTokenType.Number when reader.TryGetInt32(out var number) => (LanguageMode)number,
            JsonTokenType.String when Enum.TryParse<LanguageMode>(reader.GetString(), true, out var named) => named,
            _ => LanguageMode.ChineseSimplified
        };

        return Enum.IsDefined(mode) ? mode : LanguageMode.ChineseSimplified;
    }

    public override void Write(Utf8JsonWriter writer, LanguageMode value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((int)value);
    }
}
