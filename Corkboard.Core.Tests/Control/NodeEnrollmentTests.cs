using Corkboard.Core.ControlCenter;

namespace Corkboard.Core.Tests;

/// <summary>双因子接入输入的归一化与整句粘贴解析（协议 §1.2 + §1.3）。</summary>
public class NodeEnrollmentTests
{
    [Theory]
    [InlineData("SC-k7q2m9", "SC-K7Q2M9")]
    [InlineData("sc k7q2 m9", "SC-K7Q2M9")]
    [InlineData("SC K7Q2 M9", "SC-K7Q2M9")]
    [InlineData("sc-k7q2-m9", "SC-K7Q2M9")]
    [InlineData("K7Q2M9", "SC-K7Q2M9")]
    [InlineData("SCK7Q2M9", "SC-K7Q2M9")]
    [InlineData("  sc：k7q2：m9  ", "SC-K7Q2M9")]
    public void NormalizeSiteId_AcceptsCommonShapes(string input, string expected) =>
        Assert.Equal(expected, NodeEnrollment.NormalizeSiteId(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("SC-12345")]
    [InlineData("SC-1234567")]
    [InlineData("k7q2m")]
    public void NormalizeSiteId_RejectsIncompleteInput(string? input) =>
        Assert.Null(NodeEnrollment.NormalizeSiteId(input));

    [Theory]
    [InlineData("abcd7kq2", "ABCD-7KQ2")]
    [InlineData("abcd-7kq2", "ABCD-7KQ2")]
    [InlineData(" abcd 7kq2 ", "ABCD-7KQ2")]
    [InlineData("ABCD-7KQ2", "ABCD-7KQ2")]
    public void NormalizePairCode_FormatsWithDash(string input, string expected) =>
        Assert.Equal(expected, NodeEnrollment.NormalizePairCode(input));

    [Theory]
    [InlineData("abcd7kq")]
    [InlineData("abcd7kq22")]
    [InlineData("")]
    [InlineData(null)]
    public void NormalizePairCode_RejectsWrongLength(string? input) =>
        Assert.Null(NodeEnrollment.NormalizePairCode(input));

    [Fact]
    public void TryParseEnrollmentText_AcceptsWholeSentence()
    {
        // 老师从网页上整段复制的那一行（标签文字不参与解析，只认地点与配对码的形状）。
        const string sentence = "地点ID：SC-6M34RQ　配对码：K7Q2-9XMD　有效期 24 小时";

        Assert.True(NodeEnrollment.TryParseEnrollmentText(sentence, out var siteId, out var pairCode));
        Assert.Equal("SC-6M34RQ", siteId);
        Assert.Equal("K7Q2-9XMD", pairCode);
    }

    [Fact]
    public void TryParseEnrollmentText_AcceptsCodeBeforeSite()
    {
        Assert.True(NodeEnrollment.TryParseEnrollmentText(
            "pair code K7Q2-9XMD for site sc-6m34rq", out var siteId, out var pairCode));
        Assert.Equal("SC-6M34RQ", siteId);
        Assert.Equal("K7Q2-9XMD", pairCode);
    }

    [Fact]
    public void TryParseEnrollmentText_FindsSiteBeforeCode()
    {
        // 顺序陷阱：地点那 6 位本体紧贴配对码时，先找码会拼出一个假的 4+4。
        // 正确实现先摘掉地点再找码，所以这里必须得到真正的配对码。
        Assert.True(NodeEnrollment.TryParseEnrollmentText(
            "SC-6M34RQK7Q2-9XMD", out var siteId, out var pairCode));
        Assert.Equal("SC-6M34RQ", siteId);
        Assert.Equal("K7Q2-9XMD", pairCode);
    }

    [Fact]
    public void TryParseEnrollmentText_OnlySiteFound_ReturnsFalseAndBothNull()
    {
        var parsed = NodeEnrollment.TryParseEnrollmentText("地点ID：SC-6M34RQ", out var siteId, out var pairCode);

        Assert.False(parsed);
        Assert.Null(siteId);
        Assert.Null(pairCode);
    }

    [Fact]
    public void TryParseEnrollmentText_OnlyCodeFound_ReturnsFalseAndBothNull()
    {
        var parsed = NodeEnrollment.TryParseEnrollmentText("配对码：K7Q2-9XMD", out var siteId, out var pairCode);

        Assert.False(parsed);
        Assert.Null(siteId);
        Assert.Null(pairCode);
    }

    [Fact]
    public void TryParseEnrollmentText_RejectsOverlongInput()
    {
        var text = "SC-6M34RQ K7Q2-9XMD " + new string('x', NodeEnrollment.MaxInputLength);

        Assert.False(NodeEnrollment.TryParseEnrollmentText(text, out var siteId, out var pairCode));
        Assert.Null(siteId);
        Assert.Null(pairCode);
    }

    [Fact]
    public void FormatPairCode_KeepsShortInput()
    {
        Assert.Equal("ABCD-7KQ2", NodeEnrollment.FormatPairCode("ABCD7KQ2"));
        Assert.Equal("ABC", NodeEnrollment.FormatPairCode("ABC"));
    }
}
