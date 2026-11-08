using Corkboard.Core.Exam;

namespace Corkboard.Core.Tests;

/// <summary>
///     「打开考试看板面板窗口」的启动开关解析：认哪些写法、为什么不能与全屏放映的
///     <c>--open-exam-board</c> 串味，以及脏输入为什么必须被安静地拒绝。
///     <para>
///     这个方法的输入来自命令行（Windows 会把整条参数用引号包起来）与单实例转发，
///     什么样的脏串都可能来，所以它**不抛异常**：认不出来就返回 <see langword="false" />，
///     调用方照常启动。这里把这两件事一起钉住。
///     </para>
/// </summary>
public class ExamBoardLaunchArgumentsTests
{
    /// <summary>开关的字面量：快捷方式与文档里都照抄它，改一个字符就等于把已有入口全废掉。</summary>
    [Fact]
    public void Switch_HasTheExpectedSpelling() =>
        Assert.Equal("--open-exam-board-panel", ExamBoardLaunchArguments.OpenPanelSwitch);

    /// <summary>最短的那一条：只有这个开关 → 打开面板窗口。</summary>
    [Fact]
    public void TryOpenPanel_BareSwitch_ReturnsTrue() =>
        Assert.True(ExamBoardLaunchArguments.TryOpenPanel(["--open-exam-board-panel"]));

    /// <summary>开关本身不分大小写：快捷方式里手打的大小写不该决定命不命中。</summary>
    [Theory]
    [InlineData("--OPEN-Exam-Board-Panel")]
    [InlineData("--open-exam-board-PANEL")]
    [InlineData("--Open-Exam-Board-Panel")]
    public void TryOpenPanel_IsCaseInsensitive(string argument) =>
        Assert.True(ExamBoardLaunchArguments.TryOpenPanel([argument]));

    /// <summary>
    ///     真正的命令行里开关常常排在别的参数中间（第 0 项还是 exe 路径）：
    ///     要能一路找过去，不能只看第一项。
    /// </summary>
    [Fact]
    public void TryOpenPanel_FindsTheSwitchAmongOtherArguments()
    {
        Assert.True(ExamBoardLaunchArguments.TryOpenPanel(
        [
            @"C:\Program Files\Corkboard\Corkboard.Desktop.exe",
            "--open-settings",
            "--open-exam-board-panel",
            "--verbose"
        ]));

        Assert.True(ExamBoardLaunchArguments.TryOpenPanel(
            ["--open-exam-board-panel", "--open-settings"]));

        Assert.True(ExamBoardLaunchArguments.TryOpenPanel(
            ["--verbose", "Corkboard.Desktop.exe", "--open-exam-board-panel"]));
    }

    /// <summary>不带这个开关：哪怕别的参数长得再像也不算命中。</summary>
    [Fact]
    public void TryOpenPanel_WithoutTheSwitch_ReturnsFalse()
    {
        Assert.False(ExamBoardLaunchArguments.TryOpenPanel(["app.exe", "--open-settings"]));
        Assert.False(ExamBoardLaunchArguments.TryOpenPanel(["app.exe"]));
        Assert.False(ExamBoardLaunchArguments.TryOpenPanel(["--verbose", "--open-about"]));
    }

    /// <summary>没有参数 / 空集合：不是「命中」，也不能因此抛异常。</summary>
    [Fact]
    public void TryOpenPanel_NullOrEmptyArguments_ReturnFalse()
    {
        Assert.False(ExamBoardLaunchArguments.TryOpenPanel(null));
        Assert.False(ExamBoardLaunchArguments.TryOpenPanel([]));
        Assert.False(ExamBoardLaunchArguments.TryOpenPanel(Array.Empty<string>()));
    }

    /// <summary>
    ///     ⚠️ <c>--open-exam-board</c> 是**全屏放映**那个开关，不能被面板开关认走——
    ///     两个语义完全不同的入口，串味就会「想开面板却铺满全屏」。
    /// </summary>
    [Theory]
    [InlineData("--open-exam-board")]
    [InlineData("--OPEN-EXAM-BOARD")]
    [InlineData("\"--open-exam-board\"")]
    public void TryOpenPanel_DoesNotMatchTheFullScreenSwitch(string argument) =>
        Assert.False(ExamBoardLaunchArguments.TryOpenPanel([argument]));

    /// <summary>
    ///     不做前缀 / 后缀 / 等号匹配：<c>panelx</c>、<c>panel=true</c> 与单横线的写法都不是这个开关。
    ///     前缀匹配看起来「更宽容」，代价是把别人的开关当成自己的。
    /// </summary>
    [Theory]
    [InlineData("--open-exam-board-panelx")]
    [InlineData("--open-exam-board-panel=true")]
    [InlineData("--open-exam-board-panel=1")]
    [InlineData("-open-exam-board-panel")]
    [InlineData("--open-exam-board-pane")]
    [InlineData("open-exam-board-panel")]
    public void TryOpenPanel_RejectsPrefixAndSuffixVariants(string argument) =>
        Assert.False(ExamBoardLaunchArguments.TryOpenPanel([argument]));

    /// <summary>
    ///     <see langword="null" /> 项、空串与纯空白要安静跳过：转发来的参数表里什么都可能有，
    ///     一次空引用或一次空白串比较都不该把启动链路带走。
    /// </summary>
    [Fact]
    public void TryOpenPanel_SkipsNullAndBlankElementsWithoutThrowing()
    {
        string?[] onlyJunk = [null, "", "   ", "\t"];

        Assert.False(ExamBoardLaunchArguments.TryOpenPanel(onlyJunk));

        string?[] junkBeforeTheSwitch = [null, "", "   ", "\t", "--open-exam-board-panel"];

        Assert.True(ExamBoardLaunchArguments.TryOpenPanel(junkBeforeTheSwitch));
    }

    /// <summary>
    ///     Windows 会把整条参数用一对双引号包起来传进来，剥掉之后要能认出来；
    ///     引号外面还带空格也一样。
    /// </summary>
    [Theory]
    [InlineData("\"--open-exam-board-panel\"")]
    [InlineData("  \"--open-exam-board-panel\"  ")]
    [InlineData("\"--OPEN-Exam-Board-Panel\"")]
    public void TryOpenPanel_StripsSurroundingQuotes(string argument) =>
        Assert.True(ExamBoardLaunchArguments.TryOpenPanel([argument]));

    /// <summary>带引号的整条参数同样受「整条相等」约束：引号不是绕过前缀判断的后门。</summary>
    [Theory]
    [InlineData("\"--open-exam-board-panelx\"")]
    [InlineData("\"--open-exam-board-panel=true\"")]
    public void TryOpenPanel_QuotedVariants_StillDoNotMatch(string argument) =>
        Assert.False(ExamBoardLaunchArguments.TryOpenPanel([argument]));

    /// <summary>单个引号字符不成对，不能被当成「引号包起来的开关」而剥空。</summary>
    [Fact]
    public void TryOpenPanel_LoneQuote_DoesNotThrowOrMatch()
    {
        Assert.False(ExamBoardLaunchArguments.TryOpenPanel(["\""]));
        Assert.False(ExamBoardLaunchArguments.TryOpenPanel(["\"--open-exam-board-panel"]));
    }
}
