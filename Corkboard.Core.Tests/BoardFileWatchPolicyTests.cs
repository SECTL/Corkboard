using Corkboard.Core.Board;

namespace Corkboard.Core.Tests;

/// <summary>
///     作业板文件监视的路径判断：只有作业文件、旧版 <c>notes.json</c> 与 <c>board.json</c>
///     值得让板子重读，回收站与写盘临时文件一律忽略——否则本服务自己每存一次盘都会白刷一遍界面。
/// </summary>
public class BoardFileWatchPolicyTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "corkboard-board-watch");

    private static string PathUnderRoot(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    [Theory]
    [InlineData("2026/10/17/8f0c.cbnote", true)]
    [InlineData("2026/10/17/8F0C.CBNOTE", true)]
    [InlineData("2026/10/17/notes.json", true)]
    [InlineData("board.json", true)]
    [InlineData("2026/10/17/8f0c.cbnote.tmp", false)]
    [InlineData("2026/10/17/8f0c.cbnote.bak", false)]
    [InlineData("_trash/2026/10/17/8f0c.cbnote", false)]
    [InlineData("_Trash/2026/10/17/8f0c.cbnote", false)]
    [InlineData("_trash/board.json", false)]
    [InlineData("2026/10/17/other.json", false)]
    [InlineData("2026/10/17/board.json", false)]
    public void IsWatchedFile_OnlyCountsFilesTheBoardReads(string relativePath, bool expected)
    {
        Assert.Equal(expected, BoardFileWatchPolicy.IsWatchedFile(Root, PathUnderRoot(relativePath)));
    }

    [Fact]
    public void IsWatchedFile_AcceptsRootWithTrailingSeparator()
    {
        var path = PathUnderRoot("2026/10/17/8f0c.cbnote");

        Assert.True(BoardFileWatchPolicy.IsWatchedFile(Root + Path.DirectorySeparatorChar, path));
    }

    [Fact]
    public void IsWatchedFile_RejectsPathsOutsideTheRoot()
    {
        Assert.False(BoardFileWatchPolicy.IsWatchedFile(Root, Path.Combine(Path.GetTempPath(), "elsewhere.cbnote")));
    }

    [Fact]
    public void IsWatchedFile_RejectsMissingInput()
    {
        Assert.False(BoardFileWatchPolicy.IsWatchedFile(null, Root));
        Assert.False(BoardFileWatchPolicy.IsWatchedFile(Root, null));
        Assert.False(BoardFileWatchPolicy.IsWatchedFile(Root, string.Empty));
        Assert.False(BoardFileWatchPolicy.IsWatchedFile(Root, Root));
    }
}