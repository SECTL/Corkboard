using Corkboard.Core.Display;

namespace Corkboard.Core.Tests;

/// <summary>
///     <c>corkboard://display</c> 自定义协议的解析：认哪些写法、怎么读 <c>exit</c> 与 <c>from</c>/<c>to</c>、
///     以及脏串为什么必须被安静地拒绝。
///     <para>
///     这个方法的输入来自浏览器与命令行（Windows 的 URL 关联还会把整条 URI 用引号包起来），
///     什么样的脏串都可能来，所以它**不抛异常**：认不出来就返回 <see langword="false" />、
///     <c>command</c> 留 <see langword="null" />，调用方照常启动。这里把这两件事一起钉住。
///     </para>
/// </summary>
public class DisplayBoardUriTests
{
    /// <summary>协议三件套的常量：注册表与文档里都读它们，改一个就会与注册值脱节。</summary>
    [Fact]
    public void Constants_DescribeTheRegisteredScheme()
    {
        Assert.Equal("corkboard", DisplayBoardUri.Scheme);
        Assert.Equal("display", DisplayBoardUri.Host);
        Assert.Equal("corkboard://", DisplayBoardUri.Prefix);
    }

    /// <summary>
    ///     主机名从「自习」改成「展示」之前，协议已经注册进系统了：老链接（<c>corkboard://study</c>）
    ///     还得认，不然以前存下来的快捷方式、脚本一律失效。
    /// </summary>
    [Theory]
    [InlineData("corkboard://study")]
    [InlineData("corkboard://Study?exit=1")]
    public void TryParse_StillAcceptsTheLegacyStudyHost(string argument)
    {
        Assert.Equal("study", DisplayBoardUri.LegacyHost);
        Assert.True(DisplayBoardUri.TryParse(argument, out var command));
        Assert.NotNull(command);
    }

    /// <summary>最短的那一条：只有主机名 → 打开，这一次不临时改时段。</summary>
    [Fact]
    public void TryParse_BareDisplayUri_OpensWithoutWindow()
    {
        Assert.True(DisplayBoardUri.TryParse("corkboard://display", out var command));

        Assert.NotNull(command);
        Assert.Equal(DisplayBoardAction.Open, command!.Value.Action);
        Assert.Null(command.Value.Window);
    }

    /// <summary>
    ///     scheme 与主机名都按不区分大小写比对：浏览器与手打的大小写不该决定命不命中。
    /// </summary>
    [Theory]
    [InlineData("corkboard://DISPLAY")]
    [InlineData("CORKBOARD://display")]
    [InlineData("CorkBoard://Display")]
    public void TryParse_IsCaseInsensitiveForSchemeAndHost(string argument)
    {
        Assert.True(DisplayBoardUri.TryParse(argument, out var command));

        Assert.NotNull(command);
        Assert.Equal(DisplayBoardAction.Open, command!.Value.Action);
    }

    /// <summary>
    ///     <c>exit</c> 的真值写法：只给键（<c>?exit</c>）、<c>1</c>、<c>true</c>、<c>yes</c> 都算关掉。
    ///     真值判定本身不分大小写。
    /// </summary>
    [Theory]
    [InlineData("corkboard://display?exit=1")]
    [InlineData("corkboard://display?exit")]
    [InlineData("corkboard://display?exit=true")]
    [InlineData("corkboard://display?exit=TRUE")]
    [InlineData("corkboard://display?exit=yes")]
    [InlineData("corkboard://display?exit=Yes")]
    [InlineData("corkboard://display?EXIT=1")] // 键名也不分大小写
    public void TryParse_ExitFlag_Closes(string argument)
    {
        Assert.True(DisplayBoardUri.TryParse(argument, out var command));

        Assert.NotNull(command);
        Assert.Equal(DisplayBoardAction.Close, command!.Value.Action);
        Assert.Null(command.Value.Window);
    }

    /// <summary>假值（<c>0</c> / <c>false</c> / <c>no</c>）一律读成打开，不是「忽略这一项」。</summary>
    [Theory]
    [InlineData("corkboard://display?exit=0")]
    [InlineData("corkboard://display?exit=false")]
    [InlineData("corkboard://display?exit=no")]
    public void TryParse_FalsyExitFlag_Opens(string argument)
    {
        Assert.True(DisplayBoardUri.TryParse(argument, out var command));

        Assert.NotNull(command);
        Assert.Equal(DisplayBoardAction.Open, command!.Value.Action);
    }

    /// <summary>这一次临时用的时段：<c>from</c> 与 <c>to</c> 成对给出时带在指令里。</summary>
    [Fact]
    public void TryParse_FromAndTo_ProducesAWindow()
    {
        Assert.True(DisplayBoardUri.TryParse("corkboard://display?from=19:00&to=21:30", out var command));

        Assert.NotNull(command);
        Assert.Equal(DisplayBoardAction.Open, command!.Value.Action);
        Assert.NotNull(command.Value.Window);
        Assert.Equal(new TimeOnly(19, 0), command.Value.Window!.Value.Enter);
        Assert.Equal(new TimeOnly(21, 30), command.Value.Window.Value.Exit);
    }

    /// <summary>
    ///     冒号在 URL 里常被转义成 <c>%3A</c>，<c>H:mm</c> 这种不补零的写法也认——
    ///     浏览器地址栏与手抄的链接两种都会出现。
    /// </summary>
    [Theory]
    [InlineData("corkboard://display?from=19%3A00&to=21%3A30")]
    [InlineData("corkboard://display?from=19%3a00&to=21%3a30")] // 转义码大小写都认
    [InlineData("corkboard://display?from=9:00&to=21:30")] // H:mm
    [InlineData("corkboard://display?from=19:00&to=9:30")] // 只有 to 不补零
    public void TryParse_AcceptsEscapedAndUnpaddedTimes(string argument)
    {
        Assert.True(DisplayBoardUri.TryParse(argument, out var command));

        Assert.NotNull(command);
        Assert.NotNull(command!.Value.Window);
    }

    /// <summary>跨零点的时段也照原样带出来：22:00 → 06:00 是「今晚到明晨」那一段。</summary>
    [Fact]
    public void TryParse_KeepsCrossMidnightWindow()
    {
        Assert.True(DisplayBoardUri.TryParse("corkboard://display?from=22:00&to=06:00", out var command));

        Assert.NotNull(command);
        Assert.NotNull(command!.Value.Window);
        Assert.Equal(new TimeOnly(22, 0), command.Value.Window!.Value.Enter);
        Assert.Equal(new TimeOnly(6, 0), command.Value.Window.Value.Exit);
    }

    /// <summary>
    ///     <c>from</c> 与 <c>to</c> 必须成对：只给一个的话那段时段没有意义，
    ///     两个一起忽略——但仍然是一次「打开」，不能因此整条指令作废。
    /// </summary>
    [Theory]
    [InlineData("corkboard://display?from=19:00")]
    [InlineData("corkboard://display?to=21:30")]
    [InlineData("corkboard://display?from=19:00&to=abc")]
    [InlineData("corkboard://display?from=abc&to=21:30")]
    public void TryParse_HalfAWindow_StillOpensWithoutWindow(string argument)
    {
        Assert.True(DisplayBoardUri.TryParse(argument, out var command));

        Assert.NotNull(command);
        Assert.Equal(DisplayBoardAction.Open, command!.Value.Action);
        Assert.Null(command.Value.Window);
    }

    /// <summary>
    ///     主机名到第一个 <c>/</c>、<c>?</c>、<c>#</c> 为止，所以结尾多一条斜杠或带个片段都还是同一条指令。
    /// </summary>
    [Theory]
    [InlineData("corkboard://display/")]
    [InlineData("corkboard://display#x")]
    [InlineData("corkboard://display/#x")]
    [InlineData("corkboard://display?")]
    public void TryParse_IgnoresTrailingSlashAndFragment(string argument)
    {
        Assert.True(DisplayBoardUri.TryParse(argument, out var command));

        Assert.NotNull(command);
        Assert.Equal(DisplayBoardAction.Open, command!.Value.Action);
        Assert.Null(command.Value.Window);
    }

    /// <summary>关掉的同时也能带时段：两个字段各读各的，互不影响。</summary>
    [Fact]
    public void TryParse_ExitAndWindowTogether()
    {
        Assert.True(DisplayBoardUri.TryParse("corkboard://display?exit=1&from=19:00&to=21:30", out var command));

        Assert.NotNull(command);
        Assert.Equal(DisplayBoardAction.Close, command!.Value.Action);
        Assert.NotNull(command.Value.Window);
    }

    /// <summary>参数顺序不固定：查询串是键值对，先写 <c>to</c> 也照样读得出来。</summary>
    [Fact]
    public void TryParse_DoesNotDependOnParameterOrder()
    {
        Assert.True(DisplayBoardUri.TryParse("corkboard://display?to=21:30&from=19:00", out var command));

        Assert.NotNull(command);
        Assert.Equal(new TimeOnly(19, 0), command!.Value.Window!.Value.Enter);
        Assert.Equal(new TimeOnly(21, 30), command.Value.Window.Value.Exit);
    }

    /// <summary>
    ///     Windows 的 URL 关联会把整条 URI 用一对双引号包起来传进来，剥掉之后要能认出来。
    /// </summary>
    [Theory]
    [InlineData("\"corkboard://display\"")]
    [InlineData("\"corkboard://display?exit=1\"")]
    [InlineData("  \"corkboard://display\"  ")] // 引号外面还带空格
    public void TryParse_StripsSurroundingQuotes(string argument)
    {
        Assert.True(DisplayBoardUri.TryParse(argument, out var command));
        Assert.NotNull(command);
    }

    /// <summary>带引号的那一条要读成「打开」，不能因为剥引号把主机名一起吃掉。</summary>
    [Fact]
    public void TryParse_QuotedUri_KeepsTheHostName()
    {
        Assert.True(DisplayBoardUri.TryParse("\"corkboard://display\"", out var command));

        Assert.NotNull(command);
        Assert.Equal(DisplayBoardAction.Open, command!.Value.Action);
        Assert.Null(command.Value.Window);
    }

    /// <summary>
    ///     认不出来的输入一律返回 false、<c>command</c> 留 null：别的参数、别的协议、
    ///     空串与纯空白都不该让调用方拿到一条「空的指令」。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("https://display")] // 别的 scheme
    [InlineData("corkboard://exam")] // 别的主机名
    [InlineData("corkboard:")] // 只有 scheme
    [InlineData("corkboard://")] // 没有主机名
    [InlineData("not a uri")]
    [InlineData("--open-settings")]
    [InlineData("corkboard://display extra")] // 主机名之后不是 / ? #，整段都算主机名
    public void TryParse_RejectsUnknownInputAndLeavesCommandNull(string? argument)
    {
        Assert.False(DisplayBoardUri.TryParse(argument, out var command));
        Assert.Null(command);
    }

    /// <summary>
    ///     一条真正的命令行里 URI 常常排在别的开关后面：<see cref="DisplayBoardUri.TryParseAny" />
    ///     要能一路找过去，并且把找到的那一条带回来。
    /// </summary>
    [Fact]
    public void TryParseAny_FindsTheUriAmongOtherArguments()
    {
        string[] arguments = ["--open-settings", "corkboard://display?exit=1", "--verbose"];

        Assert.True(DisplayBoardUri.TryParseAny(arguments, out var command));

        Assert.NotNull(command);
        Assert.Equal(DisplayBoardAction.Close, command!.Value.Action);
    }

    /// <summary>取第一条命中的：后面再有一条也不覆盖前面那条。</summary>
    [Fact]
    public void TryParseAny_ReturnsTheFirstMatch()
    {
        string[] arguments = ["corkboard://display?exit=1", "corkboard://display"];

        Assert.True(DisplayBoardUri.TryParseAny(arguments, out var command));

        Assert.NotNull(command);
        Assert.Equal(DisplayBoardAction.Close, command!.Value.Action);
    }

    /// <summary>没有参数 / 一个 URI 都没有：false 且 <c>command</c> 为 null。</summary>
    [Fact]
    public void TryParseAny_ReturnsFalseWithoutAMatch()
    {
        Assert.False(DisplayBoardUri.TryParseAny(null, out var fromNull));
        Assert.Null(fromNull);

        Assert.False(DisplayBoardUri.TryParseAny([], out var fromEmpty));
        Assert.Null(fromEmpty);

        Assert.False(DisplayBoardUri.TryParseAny(["--open-settings", "https://display"], out var fromOther));
        Assert.Null(fromOther);
    }

    /// <summary>
    ///     越界的时间值（<c>25:00</c>、<c>12:99</c>）与不是时间的串（<c>abc</c>）一律读成「没有这个时刻」，
    ///     **不抛异常**——这两个值来自地址栏，谁都能手打。
    /// </summary>
    [Theory]
    [InlineData("corkboard://display?from=25:00&to=21:30")]
    [InlineData("corkboard://display?from=19:00&to=12:99")]
    [InlineData("corkboard://display?from=abc&to=21:30")]
    [InlineData("corkboard://display?from=19:00&to=abc")]
    [InlineData("corkboard://display?from=-1:00&to=21:30")]
    [InlineData("corkboard://display?from=19:00&to=21:30:99")] // 秒越界
    [InlineData("corkboard://display?from=19&to=21")] // 没有冒号
    [InlineData("corkboard://display?from=19:00:00:00&to=21:30")] // 段数太多
    public void TryParse_IgnoresInvalidTimesWithoutThrowing(string argument)
    {
        // 解析器对这些输入的唯一承诺是「不炸、不给出半截时段」。
        Assert.True(DisplayBoardUri.TryParse(argument, out var command));

        Assert.NotNull(command);
        Assert.Equal(DisplayBoardAction.Open, command!.Value.Action);
        Assert.Null(command.Value.Window);
    }

    /// <summary>带秒的写法也认（<c>HH:mm:ss</c>），秒原样带进时刻里。</summary>
    [Fact]
    public void TryParse_AcceptsSeconds()
    {
        Assert.True(DisplayBoardUri.TryParse("corkboard://display?from=19:00:30&to=21:30:15", out var command));

        Assert.NotNull(command);
        Assert.Equal(new TimeOnly(19, 0, 30), command!.Value.Window!.Value.Enter);
        Assert.Equal(new TimeOnly(21, 30, 15), command.Value.Window.Value.Exit);
    }
}
