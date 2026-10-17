using Corkboard.Core.Updates;

namespace Corkboard.Core.Tests;

/// <summary>
///     <see cref="UpdateVersionComparer" /> 的单测：发布 tag 是 <c>v3.0.0-beta.1</c> 这类 SemVer 字符串，
///     比较时必须先去掉 v 前缀与预发布/构建后缀，只比数字核心。
/// </summary>
public class UpdateVersionComparerTests
{
    [Theory]
    [InlineData("v0.2.0", "v0.1.0", true)]
    [InlineData("0.2.0", "v0.1.0", true)]
    [InlineData("v0.1.10", "v0.1.9", true)]
    [InlineData("v1.0.0", "v0.9.9", true)]
    [InlineData("v0.1.0", "v0.1.0", false)]
    [InlineData("v0.1.0", "v0.2.0", false)]
    [InlineData("v0.1.9", "v0.1.10", false)]
    public void IsNewer_ComparesNumericCore(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateVersionComparer.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData("v1.0.0-beta.1", "v0.9.0", true)]
    [InlineData("v1.0.0-beta.1", "v1.0.0-alpha.1", false)]
    [InlineData("v1.0.0+build.5", "v0.9.0", true)]
    public void IsNewer_IgnoresPrereleaseAndBuildSuffix(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateVersionComparer.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData("v2.0.0")]
    [InlineData("2.0.0")]
    [InlineData("v2.0.0-beta.3")]
    [InlineData("v2.0.0+build.42")]
    [InlineData("  v2.0.0  ")]
    public void TryParse_NormalizesReleaseTags(string text)
    {
        Assert.True(UpdateVersionComparer.TryParse(text, out var version));
        Assert.Equal(new Version(2, 0, 0), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("vnext")]
    public void TryParse_RejectsNonVersionText(string text)
    {
        Assert.False(UpdateVersionComparer.TryParse(text, out _));
    }

    [Fact]
    public void IsNewer_ReturnsFalseWhenEitherSideUnparsable()
    {
        Assert.False(UpdateVersionComparer.IsNewer("not-a-version", "v0.1.0"));
        Assert.False(UpdateVersionComparer.IsNewer("v0.2.0", "not-a-version"));
    }
}
