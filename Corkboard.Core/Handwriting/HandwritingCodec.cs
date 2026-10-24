using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Media;

namespace Corkboard.Core.Handwriting;

/// <summary>
///     手写笔迹的专有格式编解码。<see cref="Encode" /> 把一页手写压成存进 <c>Note.Handwriting</c> 的字符串，
///     <see cref="TryDecode" /> 反过来读。
///     <para>
///         这是<strong>我们自己的格式</strong>（不是 JSON）：一行魔数 + 一行元数据 + 一笔一行。
///         带魔数是为了两件事：一眼认得出这是手写数据；以后换存储位置（比如单独成文件）时不用改调用方
///         ——拿到的字符串自描述、自带版本。
///         <code>
///         CORKBOARD-INK/2
///         aspect=1.6000
///         ref=540.00
///         #FF1A1A1A|4.00|0|0.1000,0.2000,1.0000;0.3000,0.4000,1.2000
///         </code>
///         每一笔：<c>颜色|笔宽|橡皮标记|点列表</c>，点用 <c>x,y,笔锋倍率</c> 三元组、分号分隔。
///         倍率就是这一点的粗细（按书写速度算的「速度笔锋」），解析时缺省按 1（等宽）。
///         以前某一版在行尾多写过第 5 段「起笔锋利度」，那个字段已经废弃：编码不再写，
///         解码见到多出来的一段直接忽略，老文件照样能画。
///         坐标相对 <c>ref</c>（参考宽度 DIP）归一化，所以拖动内容框改大小时笔迹不会被拉动。
///     </para>
///     <para>
///         兼容旧数据：以前版本把笔迹存成了 JSON（<c>{"version":1,"aspect":…}</c>），
///         <see cref="TryDecode" /> 见到 <c>{</c> 开头就按旧 JSON 读，读出来照样能用。
///     </para>
///     <para>
///         <b>解码绝不抛异常</b>：空串、乱码、类型不对、坐标越界……一律收拾成能用的数据或返回
///         <see langword="false" />。手写是用户随手画的东西，坏掉一段数据不该把作业板点崩。
///     </para>
/// </summary>
public static class HandwritingCodec
{
    /// <summary>专有格式的魔数；文件里第一行就是它加版本号。</summary>
    public const string FormatMagic = "CORKBOARD-INK";

    /// <summary>当前专有格式版本。</summary>
    public const int FormatVersion = 2;

    private const string MagicLine = FormatMagic + "/2";

    /// <summary>坐标保留几位小数：0.0001 在 1600 宽的画面里只有 0.16 像素，肉眼看不出。</summary>
    private const int CoordinateDigits = 4;

    /// <summary>笔宽保留几位小数。</summary>
    private const int WidthDigits = 2;

    /// <summary>笔锋倍率保留几位小数。</summary>
    private const int ScaleDigits = 3;

    private const char StrokePartSeparator = '|';
    private const char PointSeparator = ';';
    private const char CoordinateSeparator = ',';
    private const char MetaSeparator = '=';

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>一页手写 → 专有格式文本。</summary>
    public static string Encode(HandwritingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();
        builder.Append(MagicLine).Append('\n');
        builder.Append("aspect=").Append(Format(RoundAspect(document.Aspect), CoordinateDigits)).Append('\n');
        builder.Append("ref=").Append(Format(RoundReferenceWidth(document.ReferenceWidth), WidthDigits)).Append('\n');

        foreach (var stroke in document.Strokes)
        {
            // 没有点的笔画画不出来也占地方，直接不写。
            if (stroke.Points.Count == 0)
                continue;

            builder.Append(FormatColor(stroke.Color))
                .Append(StrokePartSeparator)
                .Append(Format(RoundWidth(stroke.Width), WidthDigits))
                .Append(StrokePartSeparator)
                .Append(stroke.Erase ? '1' : '0')
                .Append(StrokePartSeparator);

            for (var index = 0; index < stroke.Points.Count; index++)
            {
                var point = stroke.Points[index];
                if (index > 0)
                    builder.Append(PointSeparator);

                builder.Append(Format(RoundCoordinate(point.X), CoordinateDigits))
                    .Append(CoordinateSeparator)
                    .Append(Format(RoundCoordinate(point.Y), CoordinateDigits))
                    .Append(CoordinateSeparator)
                    .Append(Format(RoundWidthScale(point.WidthScale), ScaleDigits));
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    ///     专有格式文本（或旧 JSON）→ 一页手写。空数据、坏数据、格式不认的一律返回 <see langword="false" />，
    ///     并把 <paramref name="document" /> 置成空文档（<b>绝不抛异常</b>）。
    /// </summary>
    public static bool TryDecode(string? data, out HandwritingDocument document)
    {
        document = new HandwritingDocument();

        if (string.IsNullOrWhiteSpace(data))
            return false;

        try
        {
            var text = data.Trim();

            // 旧版本存的是 JSON：见到 { 就按旧格式读，老作业里的笔迹照样能画出来。
            if (text.StartsWith('{'))
                return TryDecodeLegacyJson(text, out document);

            return TryDecodeText(text, out document);
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

    /// <summary>读专有格式：魔数行 + 元数据行 + 一笔一行的笔画。</summary>
    private static bool TryDecodeText(string text, out HandwritingDocument document)
    {
        document = new HandwritingDocument();

        var lines = text.Split('\n');
        var firstLine = lines.Length > 0 ? lines[0].Trim() : string.Empty;

        // 魔数不对：不是我们的格式，或者来自更新的版本（以后加了字段的话宁可不猜着读）。
        if (!string.Equals(firstLine, MagicLine, StringComparison.Ordinal))
            return false;

        var result = new HandwritingDocument { HasAspect = true, HasReferenceWidth = true };

        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith('#'))
            {
                if (DecodeStroke(line) is { } stroke)
                    result.Strokes.Add(stroke);

                continue;
            }

            var separator = line.IndexOf(MetaSeparator);
            if (separator <= 0 || separator == line.Length - 1)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (string.Equals(key, "aspect", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseDouble(value, out var aspect))
                    return false;

                result.Aspect = HandwritingDocument.ClampAspect(aspect);
            }
            else if (string.Equals(key, "ref", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseDouble(value, out var referenceWidth))
                    return false;

                result.ReferenceWidth = HandwritingDocument.ClampReferenceWidth(referenceWidth);
            }

            // 不认识的键不报错：以后加字段时老版本照样能读。
        }

        document = result;
        return true;
    }

    /// <summary>解一笔 <c>颜色|笔宽|橡皮标记|x,y,倍率;…</c>；数据不够就返回 <see langword="null" />。</summary>
    private static HandwritingStroke? DecodeStroke(string line)
    {
        var parts = line.Split(StrokePartSeparator);
        if (parts.Length < 4)
            return null;

        var color = Color.TryParse(parts[0], out var parsedColor) ? parsedColor : HandwritingStyle.DefaultColors[0];

        var width = TryParseDouble(parts[1], out var parsedWidth)
            ? HandwritingStyle.ClampPenWidth(parsedWidth)
            : HandwritingStyle.DefaultPenWidth;

        var erase = parts[2].Trim() is "1" or "true" or "True";

        var points = new List<HandwritingPoint>();
        foreach (var pointText in parts[3].Split(PointSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var coordinates = pointText.Split(CoordinateSeparator);
            if (coordinates.Length < 2
                || !TryParseDouble(coordinates[0], out var x)
                || !TryParseDouble(coordinates[1], out var y))
            {
                continue;
            }

            // 笔锋倍率是后加的：老数据只有两个数，按等宽处理。
            var widthScale = coordinates.Length >= 3 && TryParseDouble(coordinates[2], out var parsedScale)
                ? parsedScale
                : HandwritingPoint.DefaultWidthScale;

            points.Add(HandwritingPoint.Create(x, y, widthScale));
        }

        if (points.Count == 0)
            return null;

        // 多出来的段（曾经写过的「起笔锋利度」）直接不认：这段字段已经废弃，
        // 渲染不再用它，读进来丢掉即可，老文件照样能画。
        return new HandwritingStroke
        {
            Color = color,
            Width = width,
            Erase = erase,
            Points = points
        };
    }

    /// <summary>旧版 JSON 格式：<c>{"version":1,"aspect":1.6,"strokes":[…]}</c>。</summary>
    private static bool TryDecodeLegacyJson(string json, out HandwritingDocument document)
    {
        document = new HandwritingDocument();

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

        if (root.TryGetProperty("ref", out var referenceElement))
        {
            if (referenceElement.ValueKind != JsonValueKind.Number
                || !referenceElement.TryGetDouble(out var referenceWidth))
            {
                return false;
            }

            result.ReferenceWidth = HandwritingDocument.ClampReferenceWidth(referenceWidth);
            result.HasReferenceWidth = true;
        }

        if (root.TryGetProperty("strokes", out var strokesElement))
        {
            if (strokesElement.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var strokeElement in strokesElement.EnumerateArray())
            {
                if (DecodeLegacyStroke(strokeElement) is { } stroke)
                    result.Strokes.Add(stroke);
            }
        }

        document = result;
        return true;
    }

    /// <summary>解旧 JSON 里的一笔；数据不够（没有点、类型不对）就返回 <see langword="null" />。</summary>
    private static HandwritingStroke? DecodeLegacyStroke(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.TryGetProperty("points", out var pointsElement) || pointsElement.ValueKind != JsonValueKind.Array)
            return null;

        var coordinates = new List<double>();
        foreach (var coordinate in pointsElement.EnumerateArray())
        {
            if (coordinate.ValueKind != JsonValueKind.Number
                || !coordinate.TryGetDouble(out var value)
                || !double.IsFinite(value))
            {
                continue;
            }

            coordinates.Add(value);
        }

        var widthScales = new List<double>();
        if (element.TryGetProperty("widths", out var widthsElement) && widthsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var widthScale in widthsElement.EnumerateArray())
            {
                if (widthScale.ValueKind == JsonValueKind.Number
                    && widthScale.TryGetDouble(out var value)
                    && double.IsFinite(value))
                {
                    widthScales.Add(value);
                }
            }
        }

        var points = new List<HandwritingPoint>();
        for (var index = 0; index + 1 < coordinates.Count; index += 2)
        {
            var pointIndex = points.Count;
            var widthScale = pointIndex < widthScales.Count ? widthScales[pointIndex] : HandwritingPoint.DefaultWidthScale;
            points.Add(HandwritingPoint.Create(coordinates[index], coordinates[index + 1], widthScale));
        }

        // 一个点都解不出来就不当一笔。
        if (points.Count == 0)
            return null;

        return new HandwritingStroke
        {
            Color = DecodeLegacyColor(element),
            Width = DecodeLegacyWidth(element),
            Erase = element.TryGetProperty("erase", out var eraseElement) && eraseElement.ValueKind == JsonValueKind.True,
            Points = points
        };
    }

    private static Color DecodeLegacyColor(JsonElement strokeElement)
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

    private static double DecodeLegacyWidth(JsonElement strokeElement)
    {
        if (strokeElement.TryGetProperty("width", out var widthElement)
            && widthElement.ValueKind == JsonValueKind.Number
            && widthElement.TryGetDouble(out var width))
        {
            return HandwritingStyle.ClampPenWidth(width);
        }

        return HandwritingStyle.DefaultPenWidth;
    }

    private static bool TryParseDouble(string text, out double value)
    {
        return double.TryParse(text.Trim(), NumberStyles.Float, Invariant, out value) && double.IsFinite(value);
    }

    private static string Format(double value, int digits)
    {
        return Math.Round(value, digits, MidpointRounding.AwayFromZero).ToString(Invariant);
    }

    /// <summary>aspect 写盘前夹一次，坏值不会流到磁盘上。</summary>
    private static double RoundAspect(double value)
    {
        return HandwritingDocument.ClampAspect(value);
    }

    private static double RoundReferenceWidth(double value)
    {
        return HandwritingDocument.ClampReferenceWidth(value);
    }

    private static double RoundCoordinate(double value)
    {
        return Math.Round(HandwritingPoint.ClampCoordinate(value), CoordinateDigits, MidpointRounding.AwayFromZero);
    }

    private static double RoundWidth(double value)
    {
        return Math.Round(HandwritingStyle.ClampPenWidth(value), WidthDigits, MidpointRounding.AwayFromZero);
    }

    private static double RoundWidthScale(double value)
    {
        return Math.Round(
            Math.Clamp(value, HandwritingPoint.MinWidthScale, HandwritingPoint.MaxWidthScale),
            ScaleDigits,
            MidpointRounding.AwayFromZero);
    }

    private static string FormatColor(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
