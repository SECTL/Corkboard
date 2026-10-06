using Corkboard.Core.Models.Board;

namespace Corkboard.Core.Tests;

/// <summary>
///     字段值文本的反向解析：编辑已有作业时靠它把存下的原始值填回输入框。
///     规则是「宽进」——读不出来当没填，绝不抛异常，免得一条脏数据让编辑表单打不开。
/// </summary>
public class BoardFieldValueParserTests
{
    [Theory]
    [InlineData("12", 12)]
    [InlineData(" 12 ", 12)]
    [InlineData("1.5", 1.5)]
    [InlineData("0", 0)]
    public void ParseNumber_ReadsInvariantNumbers(string raw, decimal expected)
    {
        Assert.Equal(expected, BoardFieldValueParser.ParseNumber(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("十二")]
    [InlineData("12页")]
    public void ParseNumber_ReturnsNullForBlankOrUnparsable(string? raw)
    {
        Assert.Null(BoardFieldValueParser.ParseNumber(raw));
    }

    [Fact]
    public void SplitPageRange_ReadsBothEnds()
    {
        var (from, to) = BoardFieldValueParser.SplitPageRange("12-15");

        Assert.Equal(12, from);
        Assert.Equal(15, to);
    }

    [Fact]
    public void SplitPageRange_KeepsMissingToAsNull()
    {
        var (from, to) = BoardFieldValueParser.SplitPageRange("12-");

        Assert.Equal(12, from);
        Assert.Null(to);
    }

    [Fact]
    public void SplitPageRange_KeepsMissingFromAsNull()
    {
        var (from, to) = BoardFieldValueParser.SplitPageRange("-15");

        Assert.Null(from);
        Assert.Equal(15, to);
    }

    [Fact]
    public void SplitPageRange_TreatsTextWithoutSeparatorAsFrom()
    {
        var (from, to) = BoardFieldValueParser.SplitPageRange("12");

        Assert.Equal(12, from);
        Assert.Null(to);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("没什么-好解析的")]
    public void SplitPageRange_ReturnsEmptyPairForBlankOrUnparsable(string? raw)
    {
        var (from, to) = BoardFieldValueParser.SplitPageRange(raw);

        Assert.Null(from);
        Assert.Null(to);
    }
}
