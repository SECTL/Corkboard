using System.Text;
using System.Text.Json;
using Avalonia.Media;

namespace Corkboard.Core.Handwriting;

/// <summary>
///     笔迹 JSON 的编解码。<see cref="Encode" /> 把一页手写压成存进 <c>Note.Handwriting</c> 的字符串，
///     <see cref="TryDecode" /> 反过来读。
///     <para>
///         格式（version 目前只有 1）：
///         <code>
///         {"version":1,"aspect":1.6,"strokes":[{"color":"#FF1A1A1A","width":4,"erase":false,"points":[0.1,0.2,0.3,0.4]}]}
///         </code>
///         点按 x, y 成对平铺（不是对象数组，省一大半体积）；坐标已归一化到 0..1；
///         aspect 是画布 高 / 宽，缺了时预览按默认 1.6 画。
///     </para>
///     <para>
///         <b>解码绝不抛异常</b>：空串、乱码、类型不对、坐标越界……一律收拾成能用的数据或返回
///         <see langword="false" />。手写是用户随手画的东西，坏掉一段 JSON 不该把作业板点崩。
///     </para>
/// </summary>
public static class HandwritingCodec
{
    /// <summary>坐标保留几位小数：0.0001 在 1600 宽的画面里只有 0.16 像素，肉眼看不出。</summary>
    private const int CoordinateDigits = 4;

    /// <summary>笔宽保留几位小数。</summary>
    private const int WidthDigits = 2;

    /// <summary>一页手写 → JSON 文本。</summary>
    public static string Encode(HandwritingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", HandwritingDocument.CurrentVersion);
            writer.WriteNumber("aspect", RoundAspect(document));
            writer.WriteStartArray("strokes");

            foreach (var stroke in document.Strokes)
            {
                // 没有点的笔画画不出来也占地方，直接不写。
                if (stroke.Points.Count == 0)
                    continue;

                writer.WriteStartObject();
                writer.WriteString("color", FormatColor(stroke.Color));
                writer.WriteNumber("width", RoundWidth(stroke.Width));
                writer.WriteBoolean("erase", stroke.Erase);
                writer.WriteStartArray("points");
                foreach (var point in stroke.Points)
                {
                    writer.WriteNumberValue(RoundCoordinate(point.X));
                    writer.WriteNumberValue(RoundCoordinate(point.Y));
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    ///     JSON 文本 → 一页手写。空数据、坏 JSON、格式不认的一律返回 <see langword="false" />，
    ///     并把 <paramref name="document" /> 置成空文档（<b>绝不抛异常</b>）。
    /// </summary>
    public static bool TryDecode(string? json, out HandwritingDocument document)
    {
        document = new HandwritingDocument();

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            // 版本比自己新：那多半是以后加了字段的格式，宁可不画也不猜着读。
            // 版本字段的类型都不对（写成字符串之类）同样按坏数据算，不当作「没写版本」。
            if (root.TryGetProperty("version", out var versionElement))
            {
                if (versionElement.ValueKind != JsonValueKind.Number
                    || !versionElement.TryGetInt32(out var version))
                {
                    return false;
                }

                if (version > HandwritingDocument.CurrentVersion)
                    return false;
            }

            var result = new HandwritingDocument();

            // aspect / strokes 写了但类型不对，是坏数据（不是「没写」）：空文档走的是「根本没这个字段」。
            if (root.TryGetProperty("aspect", out var aspectElement))
            {
                if (aspectElement.ValueKind != JsonValueKind.Number
                    || !aspectElement.TryGetDouble(out var aspect))
                {
                    return false;
                }

                result.Aspect = HandwritingDocument.ClampAspect(aspect);
                result.HasAspect = true;
            }

            if (root.TryGetProperty("strokes", out var strokesElement))
            {
                if (strokesElement.ValueKind != JsonValueKind.Array)
                    return false;

                foreach (var strokeElement in strokesElement.EnumerateArray())
                {
                    if (DecodeStroke(strokeElement) is { } stroke)
                        result.Strokes.Add(stroke);
                }
            }

            document = result;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (Exception)
        {
            // 极端情况（比如字符串长度超出解析器上限）也按坏数据处理，绝不往上抛。
            return false;
        }
    }

    /// <summary>解一笔；数据不够（没有点、类型不对）就返回 <see langword="null" />。</summary>
    private static HandwritingStroke? DecodeStroke(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.TryGetProperty("points", out var pointsElement) || pointsElement.ValueKind != JsonValueKind.Array)
            return null;

        var points = new List<HandwritingPoint>();
        double? pendingX = null;

        foreach (var coordinate in pointsElement.EnumerateArray())
        {
            if (coordinate.ValueKind != JsonValueKind.Number
                || !coordinate.TryGetDouble(out var value)
                || !double.IsFinite(value))
            {
                continue;
            }

            if (pendingX is not { } x)
            {
                pendingX = value;
                continue;
            }

            // 归一化在这里夹一次：手改过或别的版本写歪的坐标也不会画到画布外。
            points.Add(HandwritingPoint.Create(x, value));
            pendingX = null;
        }

        // 一个点都解不出来，或者只有半个坐标，就不当一笔。
        if (points.Count == 0)
            return null;

        return new HandwritingStroke
        {
            Color = DecodeColor(element),
            Width = DecodeWidth(element),
            Erase = DecodeErase(element),
            Points = points
        };
    }

    private static Color DecodeColor(JsonElement strokeElement)
    {
        if (strokeElement.TryGetProperty("color", out var colorElement)
            && colorElement.ValueKind == JsonValueKind.String
            && colorElement.GetString() is { } text
            && Color.TryParse(text, out var color))
        {
            return color;
        }

        // 颜色坏了用默认色：总比画成透明、什么都看不见强。
        return HandwritingStyle.DefaultColors[0];
    }

    private static double DecodeWidth(JsonElement strokeElement)
    {
        if (strokeElement.TryGetProperty("width", out var widthElement)
            && widthElement.ValueKind == JsonValueKind.Number
            && widthElement.TryGetDouble(out var width))
        {
            return HandwritingStyle.ClampPenWidth(width);
        }

        return HandwritingStyle.DefaultPenWidth;
    }

    private static bool DecodeErase(JsonElement strokeElement)
    {
        return strokeElement.TryGetProperty("erase", out var eraseElement)
               && eraseElement.ValueKind == JsonValueKind.True;
    }

    /// <summary>aspect 写盘前夹一次，坏值不会流到磁盘上。</summary>
    private static double RoundAspect(HandwritingDocument document)
    {
        return Math.Round(HandwritingDocument.ClampAspect(document.Aspect), CoordinateDigits, MidpointRounding.AwayFromZero);
    }

    private static double RoundCoordinate(double value)
    {
        return Math.Round(HandwritingPoint.Clamp01(value), CoordinateDigits, MidpointRounding.AwayFromZero);
    }

    private static double RoundWidth(double value)
    {
        return Math.Round(HandwritingStyle.ClampPenWidth(value), WidthDigits, MidpointRounding.AwayFromZero);
    }

    private static string FormatColor(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
