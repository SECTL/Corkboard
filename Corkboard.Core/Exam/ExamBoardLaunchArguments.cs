namespace Corkboard.Core.Exam;

/// <summary>
///     「考试看板面板窗口」的启动参数解析 —— 纯函数，有单测。
///     <para>
///         考试看板平时走全屏放映（<c>--open-exam-board</c>），但用户也可能想让它以一块面板的形式
///         落在桌面上。快捷方式、开机自启、外部脚本能表达意图的唯一渠道就是命令行，所以启动链路需要
///         一个可靠的地方回答「这次是不是要开面板窗口」——就是这里。
///     </para>
///     <para>
///         ⚠️ <b>只认整条参数完全相等</b>（忽略大小写、剥掉首尾引号）：<c>--open-exam-board</c> 是另一个
///         开关（全屏放映），前缀匹配又会把 <c>--open-exam-board-panel=x</c> 这类变体误当成命中，
///         所以<b>不做</b> <c>StartsWith</c>。
///     </para>
///     <para>
///         ⚠️ <b>不抛异常</b>：输入来自命令行与单实例转发，可能是 <see langword="null" />、空集合，
///         也可能夹着空串、纯空白或 <see langword="null" /> 项——一律安静跳过，调用方照常启动。
///     </para>
/// </summary>
public static class ExamBoardLaunchArguments
{
    /// <summary>打开考试看板面板窗口的开关（全屏放映的 <c>--open-exam-board</c> 是另一个开关）。</summary>
    public const string OpenPanelSwitch = "--open-exam-board-panel";

    /// <summary>
    ///     启动参数里是否带「打开考试看板面板窗口」这个开关。
    /// </summary>
    /// <param name="arguments">
    ///     进程命令行参数（第 0 项通常是 exe 路径，照常参与比对即可）；可以是 <see langword="null" />。
    /// </param>
    /// <returns>带这个开关就返回 <see langword="true" />，否则返回 <see langword="false" />。</returns>
    public static bool TryOpenPanel(IEnumerable<string?>? arguments)
    {
        if (arguments is null)
            return false;

        foreach (var argument in arguments)
        {
            if (IsOpenPanelSwitch(argument))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     把一条参数收拾成「纯开关文本」再比对：去首尾空白、剥掉成对的首尾引号，
    ///     最后按 <see cref="StringComparison.OrdinalIgnoreCase" /> 与 <see cref="OpenPanelSwitch" /> 全等比较。
    /// </summary>
    private static bool IsOpenPanelSwitch(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return false;

        var text = argument.Trim();

        // 命令行里常常带着一对引号（Windows 会把整条参数用双引号包起来）。
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = text[1..^1].Trim();

        return string.Equals(text, OpenPanelSwitch, StringComparison.OrdinalIgnoreCase);
    }
}
