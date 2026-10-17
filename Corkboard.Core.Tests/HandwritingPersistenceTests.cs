using System.Text.Json;
using Corkboard.Core.Abstraction;
using Corkboard.Core.Handwriting;
using Corkboard.Core.Notes;
using Corkboard.Core.RichText;

namespace Corkboard.Core.Tests;

/// <summary>
///     手写作业的落盘契约：手写的笔迹与「这是手写」这个标记必须经得住一次 JSON 往返，
///     否则会出现「关了应用再打开，手写内容没了」（用户报过）。
/// </summary>
public class HandwritingPersistenceTests
{
    private static string SampleHandwritingJson()
    {
        var document = new HandwritingDocument();
        document.Strokes.Add(new HandwritingStroke
        {
            Color = Avalonia.Media.Colors.Red,
            Width = 4,
            Points =
            [
                HandwritingPoint.Create(0.1, 0.2),
                HandwritingPoint.Create(0.3, 0.4, 1.2),
                HandwritingPoint.Create(0.5, 0.6, 0.7)
            ]
        });

        return HandwritingCodec.Encode(document);
    }

    [Fact]
    public void Note_WithHandwriting_SurvivesJsonRoundTrip()
    {
        var note = new Note { Subject = "英语", Section = "A组" };
        note.SetHandwriting(SampleHandwritingJson());

        var json = JsonSerializer.Serialize(note, ConfigServiceBase.JsonOptions);
        Assert.Contains("handwriting", json, StringComparison.Ordinal);

        var restored = JsonSerializer.Deserialize<Note>(json, ConfigServiceBase.JsonOptions)!;
        Assert.Equal(RichTextContentKind.Handwriting, restored.ContentKind);
        Assert.False(string.IsNullOrWhiteSpace(restored.Handwriting));
        Assert.True(HandwritingCodec.TryDecode(restored.Handwriting, out var decoded));
        Assert.True(decoded.HasInk);
        Assert.Equal(1.2, decoded.Strokes[0].Points[1].WidthScale, 3);
    }

    [Fact]
    public void NoteDayFile_WithHandwritingNote_SurvivesJsonRoundTrip()
    {
        var note = new Note { Subject = "英语" };
        note.SetHandwriting(SampleHandwritingJson());
        var day = new NoteDayFile { Notes = [note] };

        var json = JsonSerializer.Serialize(day, ConfigServiceBase.JsonOptions);
        var restored = JsonSerializer.Deserialize<NoteDayFile>(json, ConfigServiceBase.JsonOptions)!;

        var single = Assert.Single(restored.Notes);
        Assert.Equal(RichTextContentKind.Handwriting, single.ContentKind);
        Assert.True(HandwritingCodec.TryDecode(single.Handwriting, out var decoded));
        Assert.True(decoded.HasInk);
    }

    [Fact]
    public void Codec_WithoutWidths_DecodesAsEvenWidth()
    {
        // 旧数据没有笔锋倍率：读出来必须按等宽画，不能当坏数据丢掉。
        const string legacy = """{"version":1,"aspect":1.6,"strokes":[{"color":"#FF1A1A1A","width":4,"erase":false,"points":[0.1,0.2,0.3,0.4]}]}""";

        Assert.True(HandwritingCodec.TryDecode(legacy, out var document));
        Assert.Equal(2, document.Strokes[0].Points.Count);
        Assert.All(document.Strokes[0].Points, point => Assert.Equal(HandwritingPoint.DefaultWidthScale, point.WidthScale));
    }

    [Fact]
    public void Codec_PreservesPerPointWidths()
    {
        var json = SampleHandwritingJson();

        Assert.True(HandwritingCodec.TryDecode(json, out var document));
        var points = document.Strokes[0].Points;
        Assert.Equal(3, points.Count);
        Assert.Equal(1.0, points[0].WidthScale, 3);
        Assert.Equal(1.2, points[1].WidthScale, 3);
        Assert.Equal(0.7, points[2].WidthScale, 3);
    }
}
