using Avalonia.Media;
using Corkboard.Core.Handwriting;

namespace Corkboard.Core.Tests;

/// <summary>
///     手写笔迹的数据层：JSON 编解码、坏数据兜底、坐标夹取、aspect 保留，以及「橡皮擦过之后的数据还能读回来」。
///     用例只断言数据本身（坐标、颜色、粗细、笔画数），不依赖界面语言或渲染结果。
/// </summary>
public class HandwritingTests
{
    /// <summary>按给的归一化坐标点造一笔笔迹：颜色固定近黑、笔宽 4，跟画布默认值一致。</summary>
    private static HandwritingStroke Stroke(params (double X, double Y)[] points)
    {
        return new HandwritingStroke
        {
            Color = Color.Parse("#FF1A1A1A"),
            Width = 4,
            Points = points.Select(point => HandwritingPoint.Create(point.X, point.Y)).ToList()
        };
    }

    private static HandwritingDocument SampleDocument()
    {
        return new HandwritingDocument
        {
            Aspect = 1.6,
            Strokes =
            [
                Stroke((0.1, 0.1), (0.2, 0.2), (0.3, 0.25)),
                Stroke((0.5, 0.5), (0.6, 0.55))
            ]
        };
    }

    #region 编解码往返

    [Fact]
    public void EncodeDecode_RoundTripsStrokes()
    {
        var encoded = HandwritingCodec.Encode(SampleDocument());

        Assert.True(HandwritingCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(2, decoded.Strokes.Count);
        Assert.Equal(1.6, decoded.Aspect, 4);
        Assert.True(decoded.HasAspect);

        var first = decoded.Strokes[0];
        Assert.Equal(Color.Parse("#FF1A1A1A"), first.Color);
        Assert.Equal(4, first.Width, 2);
        Assert.False(first.Erase);
        Assert.Equal(3, first.Points.Count);
        Assert.Equal(0.1, first.Points[0].X, 4);
        Assert.Equal(0.1, first.Points[0].Y, 4);
        Assert.Equal(0.3, first.Points[2].X, 4);
        Assert.Equal(0.25, first.Points[2].Y, 4);
    }

    [Fact]
    public void EncodeDecode_RoundTripsAspect()
    {
        var document = SampleDocument();
        document.Aspect = 2.35;

        Assert.True(HandwritingCodec.TryDecode(HandwritingCodec.Encode(document), out var decoded));
        Assert.Equal(2.35, decoded.Aspect, 4);
        Assert.True(decoded.HasAspect);
    }

    [Fact]
    public void EncodeDecode_EmptyDocumentIsReadable()
    {
        var encoded = HandwritingCodec.Encode(new HandwritingDocument());

        Assert.True(HandwritingCodec.TryDecode(encoded, out var decoded));
        Assert.False(decoded.HasInk);
        Assert.Empty(decoded.Strokes);
        Assert.Equal(HandwritingDocument.DefaultAspect, decoded.Aspect, 4);
    }

    [Fact]
    public void Encode_SkipsStrokeWithoutPoints()
    {
        var document = SampleDocument();
        document.Strokes.Add(Stroke());

        Assert.True(HandwritingCodec.TryDecode(HandwritingCodec.Encode(document), out var decoded));
        Assert.Equal(2, decoded.Strokes.Count);
    }

    [Fact]
    public void Decode_MissingAspectFallsBackToDefault()
    {
        // 老数据 / 手写的 JSON 里可能没有 aspect：不能当成「aspect = 0」画成一条线。
        const string json = """
                            {"version":1,"strokes":[{"color":"#FF000000","width":4,"erase":false,"points":[0.5,0.5]}]}
                            """;

        Assert.True(HandwritingCodec.TryDecode(json, out var decoded));
        Assert.False(decoded.HasAspect);
        Assert.Equal(HandwritingDocument.DefaultAspect, decoded.Aspect, 4);
    }

    #endregion

    #region 坏数据

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("\"手写\"")]
    [InlineData("null")]
    [InlineData("{\"version\":\"1\",\"strokes\":[]}")]
    [InlineData("{\"version\":1,\"strokes\":{}}")]
    [InlineData("{\"aspect\":\"高宽比\",\"strokes\":[]}")]
    public void TryDecode_BadDataReturnsFalseWithoutThrowing(string? json)
    {
        var result = HandwritingCodec.TryDecode(json, out var document);

        Assert.False(result);
        Assert.NotNull(document);
        Assert.False(document.HasInk);
        Assert.Empty(document.Strokes);
    }

    [Fact]
    public void TryDecode_NewerVersionIsRejected()
    {
        // 将来格式变了，旧版本宁可不画，也不要按老规则猜着读。
        const string json = """{"version":99,"aspect":1.6,"strokes":[]}""";

        Assert.False(HandwritingCodec.TryDecode(json, out _));
    }

    [Fact]
    public void TryDecode_BrokenElementsAreSkipped()
    {
        // 笔画数组里混着 null、没有 points、坐标类型不对的项：能读的照读，读不了的跳过。
        const string json = """
                            {"version":1,"aspect":1.6,"strokes":[
                              null,
                              {"color":"#FF000000","width":4,"points":[]},
                              {"color":"#FF000000","width":4,"points":[0.1,"坏坐标"]},
                              {"color":"不是颜色","width":999,"points":[0.25,0.5,0.75,1]}
                            ]}
                            """;

        Assert.True(HandwritingCodec.TryDecode(json, out var decoded));
        Assert.Single(decoded.Strokes);

        var stroke = decoded.Strokes[0];
        Assert.Equal(HandwritingStyle.DefaultColors[0], stroke.Color);
        Assert.Equal(HandwritingStyle.MaxPenWidth, stroke.Width, 2);
        Assert.Equal(2, stroke.Points.Count);
    }

    #endregion

    #region 归一化夹取

    [Fact]
    public void TryDecode_ClampsCoordinatesIntoUnitRange()
    {
        const string json = """
                            {"version":1,"aspect":1.6,"strokes":[{"color":"#FF000000","width":4,
                             "points":[-0.5,-2,1.5,3,0.5,0.5]}]}
                            """;

        Assert.True(HandwritingCodec.TryDecode(json, out var decoded));
        var points = decoded.Strokes[0].Points;

        Assert.Equal(0, points[0].X);
        Assert.Equal(0, points[0].Y);
        Assert.Equal(1, points[1].X);
        Assert.Equal(1, points[1].Y);
        Assert.Equal(0.5, points[2].X, 4);
        Assert.Equal(0.5, points[2].Y, 4);
    }

    [Fact]
    public void Create_ClampsBadCoordinates()
    {
        // NaN / 无穷这类坏值按 0 处理：至少不会把几何画崩。
        Assert.Equal(new HandwritingPoint(0, 0), HandwritingPoint.Create(double.NaN, double.PositiveInfinity));
        Assert.Equal(new HandwritingPoint(1, 1), HandwritingPoint.Create(5, 9));
        Assert.Equal(new HandwritingPoint(0.25, 0.75), HandwritingPoint.Create(0.25, 0.75));
    }

    [Theory]
    [InlineData(0, HandwritingDocument.MinAspect)]
    [InlineData(-5, HandwritingDocument.MinAspect)]
    [InlineData(100, HandwritingDocument.MaxAspect)]
    [InlineData(1.6, 1.6)]
    public void ClampAspect_KeepsAspectInRange(double input, double expected)
    {
        Assert.Equal(expected, HandwritingDocument.ClampAspect(input), 4);
    }

    [Fact]
    public void ClampAspect_NonFiniteFallsBackToDefault()
    {
        Assert.Equal(HandwritingDocument.DefaultAspect, HandwritingDocument.ClampAspect(double.NaN), 4);
        Assert.Equal(HandwritingDocument.DefaultAspect, HandwritingDocument.ClampAspect(double.PositiveInfinity), 4);
    }

    [Fact]
    public void Style_ClampsWidths()
    {
        Assert.Equal(HandwritingStyle.MinPenWidth, HandwritingStyle.ClampPenWidth(0));
        Assert.Equal(HandwritingStyle.MaxPenWidth, HandwritingStyle.ClampPenWidth(999));
        Assert.Equal(HandwritingStyle.MinEraserWidth, HandwritingStyle.ClampEraserWidth(1));
        Assert.Equal(HandwritingStyle.MaxEraserWidth, HandwritingStyle.ClampEraserWidth(500));
    }

    #endregion

    #region 橡皮擦过之后的数据

    [Fact]
    public void Erase_CutsStrokeIntoDecodablePieces()
    {
        // 一条横线（100 个点从 x=0.1 到 0.9），画布 1000×1000：在正中间擦一下，应该断成两截。
        var points = Enumerable.Range(0, 81)
            .Select(index => HandwritingPoint.Create(0.1 + index * 0.01, 0.5))
            .ToList();

        var document = new HandwritingDocument
        {
            Aspect = 1.5,
            Strokes =
            [
                new HandwritingStroke { Color = Color.Parse("#FF1A1A1A"), Width = 4, Points = points }
            ]
        };

        var erased = HandwritingEraser.Erase(document.Strokes, 0.5, 0.5, 50, 1000, 1000, out var changed);

        Assert.True(changed);
        Assert.Equal(2, erased.Count);
        Assert.All(erased, stroke => Assert.True(stroke.Points.Count >= 2));

        // 断口附近的点真的没了：两截都不会再盖住 x = 0.5（不是拿一笔盖上去）。
        Assert.All(erased, stroke => Assert.DoesNotContain(stroke.Points, point => Math.Abs(point.X - 0.5) < 0.03));

        document.Strokes = erased;

        // 擦完的笔迹要还能存、还能读：预览就用同一份数据画。
        Assert.True(HandwritingCodec.TryDecode(HandwritingCodec.Encode(document), out var decoded));
        Assert.True(decoded.HasInk);
        Assert.Equal(2, decoded.VisibleStrokeCount);
        Assert.Equal(1.5, decoded.Aspect, 4);
        Assert.All(decoded.Strokes, stroke => Assert.False(stroke.Erase));
    }

    [Fact]
    public void Erase_RemovesSinglePointStrokeCompletely()
    {
        var document = new HandwritingDocument
        {
            Strokes = [Stroke((0.5, 0.5))]
        };

        var erased = HandwritingEraser.Erase(document.Strokes, 0.5, 0.5, 40, 1000, 1000, out var changed);

        Assert.True(changed);
        Assert.Empty(erased);

        document.Strokes = erased;
        Assert.False(document.HasInk);

        // 擦干净之后编码出来是空文档，预览什么都不画。
        Assert.True(HandwritingCodec.TryDecode(HandwritingCodec.Encode(document), out var decoded));
        Assert.False(decoded.HasInk);
    }

    [Fact]
    public void Erase_OnEmptyCanvasChangesNothing()
    {
        var erased = HandwritingEraser.Erase([], 0.5, 0.5, 40, 1000, 1000, out var changed);

        Assert.False(changed);
        Assert.Empty(erased);
    }

    [Fact]
    public void Erase_MissesStrokeOutsideRadius()
    {
        // 线在左上角，橡皮在右下角：离得远，笔迹要原样留着。
        var document = new HandwritingDocument
        {
            Strokes = [Stroke((0.1, 0.1), (0.2, 0.2))]
        };

        var erased = HandwritingEraser.Erase(document.Strokes, 0.9, 0.9, 40, 1000, 1000, out var changed);

        Assert.False(changed);
        Assert.Single(erased);
        Assert.Equal(2, erased[0].Points.Count);
    }

    #endregion
}
