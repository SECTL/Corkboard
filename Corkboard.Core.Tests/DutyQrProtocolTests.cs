using System.Globalization;
using Corkboard.Core.DutyRoster.Import;

namespace Corkboard.Core.Tests;

/// <summary>
///     离线二维码分包协议：编解码往返、乱序/重复帧、跨会话、旧 320 字节帧、上限与校验失败。
///     <para>
///         会话 ID 全部写成固定值，帧内容与断言不依赖 <see cref="Guid.NewGuid" /> 或当前时间，重跑结果一致。
///     </para>
/// </summary>
public class DutyQrProtocolTests
{
    private const string SessionA = "0123456789abcdef0123456789abcdef";

    private const string SessionB = "fedcba9876543210fedcba9876543210";

    [Fact]
    public void RoundTrip_RestoresDocument()
    {
        var document = CreateDocument(96);
        var frames = OfflineQrProtocol.CreateFrames(document, SessionA);
        var accumulator = new OfflineQrFrameAccumulator();

        foreach (var frame in frames)
            Assert.Equal(RosterQrFrameImportResult.Accepted, accumulator.AddFrame(frame));

        Assert.True(accumulator.IsComplete);
        Assert.Equal(SessionA, accumulator.SessionId);
        Assert.Equal(frames.Count - 1, accumulator.TotalFrameCount);
        Assert.Equal(frames.Count - 1, accumulator.ReceivedFrameCount);

        var result = accumulator.Complete();

        Assert.True(result.Success);
        AssertSameDocument(document, result.Document);
    }

    [Fact]
    public void FrameFormat_UsesFixedWidthNumbersAndPrefixes()
    {
        var document = CreateDocument(96);
        var frames = OfflineQrProtocol.CreateFrames(document, SessionA);

        var manifest = frames[0].Split('|');
        Assert.Equal(OfflineQrProtocol.ManifestPrefix, manifest[0]);
        Assert.Equal(SessionA, manifest[1]);
        Assert.True(int.Parse(manifest[3], CultureInfo.InvariantCulture) > 0);
        Assert.Equal(frames.Count - 1, int.Parse(manifest[4], CultureInfo.InvariantCulture));
        Assert.Equal(64, manifest[5].Length);

        var data = frames[1].Split('|');
        Assert.Equal(OfflineQrProtocol.DataPrefix, data[0]);
        Assert.Equal(SessionA, data[1]);
        Assert.Equal(OfflineQrProtocol.FrameIndexDigits, data[2].Length);
        Assert.Equal(OfflineQrProtocol.FrameOffsetDigits, data[3].Length);
        Assert.Equal("0000", data[2]);
        Assert.Equal("0000000", data[3]);
        Assert.Equal(OfflineQrProtocol.FramePayloadLength, Convert.FromBase64String(data[6]).Length);
    }

    [Fact]
    public void OutOfOrderDataFrames_StillComplete()
    {
        var document = CreateDocument(96);
        var frames = OfflineQrProtocol.CreateFrames(document, SessionA);
        var accumulator = new OfflineQrFrameAccumulator();

        Assert.Equal(RosterQrFrameImportResult.Accepted, accumulator.AddFrame(frames[0]));
        for (var index = frames.Count - 1; index >= 1; index--)
            Assert.Equal(RosterQrFrameImportResult.Accepted, accumulator.AddFrame(frames[index]));

        var result = accumulator.Complete();

        Assert.True(result.Success);
        AssertSameDocument(document, result.Document);
    }

    [Fact]
    public void DuplicateFrames_AreReportedAsDuplicate()
    {
        var frames = OfflineQrProtocol.CreateFrames(CreateDocument(96), SessionA);
        var accumulator = new OfflineQrFrameAccumulator();

        Assert.Equal(RosterQrFrameImportResult.Accepted, accumulator.AddFrame(frames[0]));
        Assert.Equal(RosterQrFrameImportResult.Duplicate, accumulator.AddFrame(frames[0]));

        Assert.Equal(RosterQrFrameImportResult.Accepted, accumulator.AddFrame(frames[1]));
        Assert.Equal(RosterQrFrameImportResult.Duplicate, accumulator.AddFrame(frames[1]));
        Assert.Equal(1, accumulator.ReceivedFrameCount);
    }

    [Fact]
    public void MalformedFrames_AreRejected()
    {
        var accumulator = new OfflineQrFrameAccumulator();

        Assert.Equal(RosterQrFrameImportResult.Rejected, accumulator.AddFrame(null));
        Assert.Equal(RosterQrFrameImportResult.Rejected, accumulator.AddFrame(""));
        Assert.Equal(RosterQrFrameImportResult.Rejected, accumulator.AddFrame("garbage"));
        Assert.Equal(RosterQrFrameImportResult.Rejected, accumulator.AddFrame("SRQR1M|too-short"));
        Assert.Equal(RosterQrFrameImportResult.Rejected, accumulator.AddFrame("SRQR9M|" + SessionA + "|x|1|1|" + new string('a', 64)));
        Assert.False(accumulator.HasManifest);
    }

    [Fact]
    public void DataFrameBeforeManifest_IsRejected()
    {
        var frames = OfflineQrProtocol.CreateFrames(CreateDocument(96), SessionA);
        var accumulator = new OfflineQrFrameAccumulator();

        Assert.Equal(RosterQrFrameImportResult.Rejected, accumulator.AddFrame(frames[1]));
        Assert.Equal(0, accumulator.ReceivedFrameCount);
    }

    [Fact]
    public void DifferentSession_ClearsPreviousProgress()
    {
        var framesA = OfflineQrProtocol.CreateFrames(CreateDocument(96), SessionA);
        var documentB = CreateDocument(200);
        var framesB = OfflineQrProtocol.CreateFrames(documentB, SessionB);
        var accumulator = new OfflineQrFrameAccumulator();

        Assert.Equal(RosterQrFrameImportResult.Accepted, accumulator.AddFrame(framesA[0]));
        Assert.Equal(RosterQrFrameImportResult.Accepted, accumulator.AddFrame(framesA[1]));
        Assert.False(accumulator.IsComplete);

        Assert.Equal(RosterQrFrameImportResult.Accepted, accumulator.AddFrame(framesB[0]));
        Assert.Equal(SessionB, accumulator.SessionId);
        Assert.Equal(0, accumulator.ReceivedFrameCount);

        foreach (var frame in framesB.Skip(1))
            Assert.Equal(RosterQrFrameImportResult.Accepted, accumulator.AddFrame(frame));

        var result = accumulator.Complete();

        Assert.True(result.Success);
        AssertSameDocument(documentB, result.Document);
    }

    [Fact]
    public void MissingDataFrame_ReturnsIncomplete()
    {
        var frames = OfflineQrProtocol.CreateFrames(CreateDocument(96), SessionA);
        var accumulator = new OfflineQrFrameAccumulator();

        foreach (var frame in frames.Take(frames.Count - 1))
            accumulator.AddFrame(frame);

        var result = accumulator.Complete();

        Assert.False(result.Success);
        Assert.Equal(RosterQrImportError.Incomplete, result.Error);
    }

    [Fact]
    public void NoManifest_ReturnsNoManifest()
    {
        var result = new OfflineQrFrameAccumulator().Complete();

        Assert.False(result.Success);
        Assert.Equal(RosterQrImportError.NoManifest, result.Error);
    }

    [Fact]
    public void TamperedPayload_ReturnsChecksumMismatch()
    {
        var frames = OfflineQrProtocol.CreateFrames(CreateDocument(96), SessionA).ToList();
        var accumulator = new OfflineQrFrameAccumulator();

        // 改掉最后一帧第一个字节：它在压缩内容范围内，一定参与校验。
        var parts = frames[^1].Split('|');
        var chunk = Convert.FromBase64String(parts[6]);
        chunk[0] ^= 0xFF;
        parts[6] = Convert.ToBase64String(chunk);
        frames[^1] = string.Join('|', parts);

        foreach (var frame in frames)
            accumulator.AddFrame(frame);

        var result = accumulator.Complete();

        Assert.False(result.Success);
        Assert.Equal(RosterQrImportError.ChecksumMismatch, result.Error);
    }

    [Fact]
    public void Legacy320ByteFrames_AreAccepted()
    {
        var document = CreateDocument(96);
        var frames = OfflineQrProtocol.CreateFrames(document, SessionA, OfflineQrProtocol.LegacyFramePayloadLength);
        var accumulator = new OfflineQrFrameAccumulator();

        foreach (var frame in frames)
            Assert.Equal(RosterQrFrameImportResult.Accepted, accumulator.AddFrame(frame));

        var result = accumulator.Complete();

        Assert.True(result.Success);
        AssertSameDocument(document, result.Document);
    }

    [Fact]
    public void PayloadAbove300KiB_IsRejectedAtExport()
    {
        // GUID 文本压缩不动：一万多行叠起来必然超过 300 KiB。
        var rows = Enumerable.Range(0, 15000)
            .Select(index => new RosterTransferRow(
                true,
                (index + 1).ToString(CultureInfo.InvariantCulture),
                Guid.NewGuid().ToString(),
                "f",
                "group",
                "tag"))
            .ToArray();
        var document = new RosterTransferDocument(
            OfflineQrProtocol.DocumentVersion,
            RosterTransferKind.DutyRoster,
            "roster.xlsx",
            rows);

        var exception = Assert.Throws<InvalidDataException>(() => OfflineQrProtocol.CreateFrames(document, SessionA));
        Assert.Contains("300 KiB", exception.Message);
    }

    [Fact]
    public void DecompressionBeyondSafetyLimit_ReturnsDecompressionLimit()
    {
        // 内容高度重复：压缩后只有几 KB，但解开后上 MB，正好撞上「解压上限 = 2 倍」的保护。
        var document = CreateDocument(5000, nameLength: 300);
        var frames = OfflineQrProtocol.CreateFrames(document, SessionA);
        var accumulator = new OfflineQrFrameAccumulator();

        foreach (var frame in frames)
            accumulator.AddFrame(frame);

        var result = accumulator.Complete();

        Assert.False(result.Success);
        Assert.Equal(RosterQrImportError.DecompressionLimit, result.Error);
    }

    [Fact]
    public void Base64Url_IsUrlSafeAndRoundTrips()
    {
        var bytes = new byte[] { 0xFB, 0xFF };

        var encoded = OfflineQrProtocol.EncodeBase64Url(bytes);

        Assert.Equal("-_8", encoded);
        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
        Assert.Equal(bytes, OfflineQrProtocol.DecodeBase64Url(encoded));
        Assert.Null(OfflineQrProtocol.DecodeBase64Url("****"));
        Assert.Null(OfflineQrProtocol.DecodeBase64Url(null));
    }

    // ── 辅助 ────────────────────────────────────────────────────────────────────

    private static RosterTransferDocument CreateDocument(int rowCount, int nameLength = 0)
    {
        var rows = new List<RosterTransferRow>(rowCount);
        for (var index = 0; index < rowCount; index++)
        {
            var name = nameLength > 0 ? new string('a', nameLength) : $"student-{index:D4}";
            rows.Add(new RosterTransferRow(
                true,
                (index + 1).ToString(CultureInfo.InvariantCulture),
                name,
                "f",
                "group-a",
                "tag"));
        }

        return new RosterTransferDocument(
            OfflineQrProtocol.DocumentVersion,
            RosterTransferKind.DutyRoster,
            "roster.xlsx",
            rows);
    }

    private static void AssertSameDocument(RosterTransferDocument expected, RosterTransferDocument? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Version, actual!.Version);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.FileName, actual.FileName);
        Assert.Equal(expected.Rows.Count, actual.Rows.Count);
        for (var index = 0; index < expected.Rows.Count; index++)
        {
            Assert.Equal(expected.Rows[index].Exists, actual.Rows[index].Exists);
            Assert.Equal(expected.Rows[index].Id, actual.Rows[index].Id);
            Assert.Equal(expected.Rows[index].Name, actual.Rows[index].Name);
            Assert.Equal(expected.Rows[index].DetailOne, actual.Rows[index].DetailOne);
            Assert.Equal(expected.Rows[index].DetailTwo, actual.Rows[index].DetailTwo);
            Assert.Equal(expected.Rows[index].Tags, actual.Rows[index].Tags);
        }
    }
}
