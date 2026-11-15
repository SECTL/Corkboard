using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     老师在网页控制台布置、由设备拉取下来的一条作业。
///     <para>
///         服务端的契约（<c>GET /control/api/device/homework-assignments</c>）：
///         <c>content</c> 是**纯文本**（不是 HTML），要用 <see cref="ControlHomeworkAssignmentHtml.ToHtml" />
///         转成富文本片段；<c>dueDate</c> 是 <c>YYYY-MM-DD</c> 或空串。
///     </para>
/// </summary>
/// <param name="Id">服务端给这条布置的 id（ack 时回传）。</param>
/// <param name="Subject">科目；空表示未分类（本地化文案由应用层补）。</param>
/// <param name="Section">二级分类；原样带到作业上。</param>
/// <param name="Content">纯文本正文（可能带换行）。</param>
/// <param name="DueDate">截止日期原文（<c>YYYY-MM-DD</c> 或空）。</param>
/// <param name="CreatedAt">老师布置的时刻（解析不出来时为 <c>null</c>）。</param>
public sealed record ControlHomeworkAssignment(
    string Id,
    string? Subject,
    string? Section,
    string Content,
    string? DueDate,
    DateTimeOffset? CreatedAt)
{
    /// <summary>把 <see cref="DueDate" /> 解析成 <see cref="DateOnly" />；空串 / 不认识的格式返回 <c>null</c>。</summary>
    public DateOnly? ResolveDueDate() => TryParseDueDate(DueDate, out var dueDate) ? dueDate : null;

    /// <summary>解析 <c>YYYY-MM-DD</c>（服务端契约里 dueDate 只有这一种格式或空串）。</summary>
    public static bool TryParseDueDate(string? text, out DateOnly dueDate)
    {
        dueDate = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return DateOnly.TryParseExact(
            text.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out dueDate);
    }
}

/// <summary>
///     一次作业布置拉取的结果（<c>data</c> 那一层）。
/// </summary>
/// <param name="Items">这次拿到的待处理布置（可能为空）。</param>
/// <param name="ClassRequired">服务端说这台设备还没占班（<c>classRequired</c>）。</param>
public sealed record ControlHomeworkAssignmentPull(
    IReadOnlyList<ControlHomeworkAssignment> Items,
    bool ClassRequired)
{
    /// <summary>空的一批（解析失败时不至于给调用方 null）。</summary>
    public static ControlHomeworkAssignmentPull Empty { get; } =
        new(Array.Empty<ControlHomeworkAssignment>(), false);

    /// <summary>
    ///     从 <c>data</c> 对象解析：<c>{items:[…], classRequired:boolean}</c>。
    ///     <para>
    ///         <c>items</c> 都不是数组时返回 <c>null</c>（调用方按「服务端返回了无法识别的内容」处理）；
    ///         数组里缺 id 的条目跳过——没有 id 就没法 ack，留着也只会重复建。
    ///     </para>
    /// </summary>
    public static ControlHomeworkAssignmentPull? TryParse(JsonObject? data)
    {
        if (data?["items"] is not JsonArray array)
            return null;

        var items = new List<ControlHomeworkAssignment>(array.Count);
        foreach (var node in array)
        {
            if (node is not JsonObject item)
                continue;

            var id = ControlJson.ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            items.Add(new ControlHomeworkAssignment(
                id.Trim(),
                ControlJson.ReadString(item, "subject"),
                ControlJson.ReadString(item, "section"),
                ControlJson.ReadString(item, "content") ?? string.Empty,
                ControlJson.ReadString(item, "dueDate") ?? ControlJson.ReadString(item, "due_date"),
                ReadDateTimeOffset(item, "createdAt") ?? ReadDateTimeOffset(item, "created_at")));
        }

        return new ControlHomeworkAssignmentPull(
            items,
            ControlJson.ReadBool(data, "classRequired") ?? ControlJson.ReadBool(data, "class_required") ?? false);
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonObject obj, string name)
    {
        var text = ControlJson.ReadString(obj, name);
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    }
}

/// <summary>
///     把老师布置的**纯文本**正文转成作业板用的富文本片段。
///     <para>
///         逐行包 <c>&lt;p&gt;</c>（空行是 <c>&lt;p&gt;&lt;br&gt;&lt;/p&gt;</c>），正文里的
///         <c>&amp; &lt; &gt; " '</c> 由 <see cref="WebUtility.HtmlEncode" /> 转义，
///         避免老师写的尖括号被富文本控件当成标签解析。
///     </para>
/// </summary>
public static class ControlHomeworkAssignmentHtml
{
    /// <summary>一条纯文本作业的 HTML 片段（空正文给一个空段落）。</summary>
    public static string ToHtml(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return "<p></p>";

        // 统一换行：Windows 的 CRLF 与旧 Mac 的 CR 都当成一次换行，免得出现空段。
        var normalized = new StringBuilder(plainText.Length);
        for (var i = 0; i < plainText.Length; i++)
        {
            var c = plainText[i];
            if (c == '\r')
            {
                normalized.Append('\n');
                if (i + 1 < plainText.Length && plainText[i + 1] == '\n')
                    i++;
            }
            else
            {
                normalized.Append(c);
            }
        }

        var lines = normalized.ToString().Split('\n');
        var html = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length == 0)
                html.Append("<p><br></p>");
            else
                html.Append("<p>").Append(WebUtility.HtmlEncode(line)).Append("</p>");
        }

        return html.ToString();
    }
}