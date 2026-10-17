using System.Globalization;
using System.Text;
using Corkboard.Core.RichText;

namespace Corkboard.Core.Notes;

/// <summary>
///     一条作业的专有文件格式（扩展名 <c>.cbnote</c>）：元数据、富文本内容、手写笔迹都在这一个文件里。
///     <para>
///         格式是「一行一个字段」的文本，任意文本字段用「长度 + 原文」写成块，所以内容里有没有换行、
///         分号、等号都不影响解析：
///         <code>
///         CORKBOARD-NOTE/1
///         id=8f0c…
///         created=2026-10-17T22:03:11.1234567+08:00
///         updated=2026-10-17T22:03:11.1234567+08:00
///         order=0
///         kind=1
///         subject=2
///         英语
///         tag=5
///         上课前写完
///         text=113
///         &lt;p style="text-align:left;"&gt;…&lt;/p&gt;
///         ink=0
///         </code>
///         块字段先写 <c>键=字符数</c> 一行，紧接着正好这么多个字符的原文；其余字段是 <c>键=值</c> 单行。
///         解码对未知键、未知块一律跳过（以后加字段不破坏旧版），坏文件返回 <see langword="false" />、
///         绝不抛异常。
///     </para>
///     <para>
///         ⚠️ 文件名就是作业 Id（<c>&lt;guid:N&gt;.cbnote</c>）：作业改名/改内容都不换文件，
///         一天一个目录里一条作业一个文件，删掉一条作业就删对应文件。
///     </para>
/// </summary>
public static class NoteFileFormat
{
    /// <summary>作业文件扩展名（专有格式，不是 json）。</summary>
    public const string FileExtension = ".cbnote";

    /// <summary>魔数：第一行就是它加版本号，一眼认出这是作业文件。</summary>
    public const string FormatMagic = "CORKBOARD-NOTE";

    /// <summary>当前格式版本。</summary>
    public const int FormatVersion = 1;

    private const string MagicLine = FormatMagic + "/1";

    /// <summary>一条作业的文件名（相对它所在的日期目录）。</summary>
    public static string GetFileName(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return note.Id.ToString("N") + FileExtension;
    }

    /// <summary>一条作业 → 专有格式文本。</summary>
    public static string Encode(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);

        var builder = new StringBuilder();
        builder.Append(MagicLine).Append('\n');
        AppendLine(builder, "id", note.Id.ToString("N"));
        AppendLine(builder, "created", note.CreatedAt.ToString("o", CultureInfo.InvariantCulture));
        AppendLine(builder, "updated", note.UpdatedAt.ToString("o", CultureInfo.InvariantCulture));
        AppendLine(builder, "order", note.Order.ToString(CultureInfo.InvariantCulture));
        AppendLine(builder, "kind", ((int)note.ContentKind).ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrEmpty(note.Subject))
            AppendBlock(builder, "subject", note.Subject);

        if (!string.IsNullOrEmpty(note.Section))
            AppendBlock(builder, "section", note.Section);

        if (note.DueDate is { } dueDate)
            AppendLine(builder, "due", dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        if (note.CleanedAt is { } cleanedAt)
            AppendLine(builder, "cleaned", cleanedAt.ToString("o", CultureInfo.InvariantCulture));

        if (note.Tags is { Count: > 0 } tags)
        {
            foreach (var tag in tags)
                AppendBlock(builder, "tag", tag);
        }

        // 内容与笔迹总是写成块：即使为空也留一个 text=0，读的时候就不用猜「是没写还是空内容」。
        AppendBlock(builder, "text", note.Content ?? string.Empty);

        if (!string.IsNullOrEmpty(note.Handwriting))
            AppendBlock(builder, "ink", note.Handwriting);

        return builder.ToString();
    }

    /// <summary>
    ///     专有格式文本 → 一条作业。空数据、坏数据、版本不认识的一律返回 <see langword="false" />
    ///     并给出一个空作业（<b>绝不抛异常</b>）。
    /// </summary>
    public static bool TryDecode(string? text, out Note note)
    {
        note = new Note();

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var source = text;

        // 先切第一行魔数：不是我们的格式 / 版本比当前新，都不猜着读。
        var firstNewline = source.IndexOf('\n');
        var firstLine = (firstNewline < 0 ? source : source[..firstNewline]).TrimEnd('\r');
        if (!string.Equals(firstLine.Trim(), MagicLine, StringComparison.Ordinal))
            return false;

        var result = new Note();
        var position = firstNewline < 0 ? source.Length : firstNewline + 1;

        while (position < source.Length)
        {
            var lineEnd = source.IndexOf('\n', position);
            var line = (lineEnd < 0 ? source[position..] : source[position..lineEnd]).TrimEnd('\r');
            position = lineEnd < 0 ? source.Length : lineEnd + 1;

            if (line.Length == 0)
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..];

            // 块字段：值先给出字符数，紧接着正好这么多个字符（可能跨很多行）。
            if (key is "subject" or "section" or "tag" or "text" or "ink"
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length)
                && length >= 0)
            {
                var payload = position + length <= source.Length
                    ? source.Substring(position, length)
                    : source[position..];

                position += payload.Length;

                // 块后面那个换行（如果还有下文）顺手吃掉。
                if (position < source.Length && source[position] == '\r')
                    position++;
                if (position < source.Length && source[position] == '\n')
                    position++;

                ApplyBlock(result, key, payload);
                continue;
            }

            ApplyScalar(result, key, value);
        }

        // 数据修复：kind 写着 Html、但正文是空的、ink 却有内容——旧版本在打字页签里保存过一次
        // 会留下这种「笔迹还在、形态被洗掉」的作业。按手写读，否则编辑一进来就是打字页签，
        // 再保存一次就把笔迹抹掉了（用户反馈过「编辑手写作业后内容丢了」就是这个）。
        if (result.ContentKind != RichTextContentKind.Handwriting
            && string.IsNullOrWhiteSpace(result.Content)
            && !string.IsNullOrWhiteSpace(result.Handwriting))
        {
            result.ContentKind = RichTextContentKind.Handwriting;
        }

        note = result;
        return true;
    }

    private static void ApplyBlock(Note note, string key, string payload)
    {
        switch (key)
        {
            case "subject":
                note.Subject = payload;
                break;
            case "section":
                note.Section = payload;
                break;
            case "tag":
                var tags = note.Tags is null ? new List<string>() : [.. note.Tags];
                tags.Add(payload);
                note.Tags = tags;
                break;
            case "text":
                note.Content = payload;
                break;
            case "ink":
                note.Handwriting = string.IsNullOrWhiteSpace(payload) ? null : payload;
                break;
        }
    }

    private static void ApplyScalar(Note note, string key, string value)
    {
        switch (key)
        {
            case "id":
                if (Guid.TryParse(value.Trim(), out var id))
                    note.Id = id;
                break;
            case "created":
                if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var createdAt))
                {
                    note.CreatedAt = createdAt;
                }

                break;
            case "updated":
                if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var updatedAt))
                {
                    note.UpdatedAt = updatedAt;
                }

                break;
            case "order":
                if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
                    note.Order = order;
                break;
            case "kind":
                if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind)
                    && Enum.IsDefined(typeof(RichTextContentKind), kind))
                {
                    note.ContentKind = (RichTextContentKind)kind;
                }

                break;
            case "due":
                if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dueDate))
                {
                    note.DueDate = dueDate;
                }

                break;
            case "cleaned":
                if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var cleanedAt))
                {
                    note.CleanedAt = cleanedAt;
                }

                break;
        }
    }

    private static void AppendLine(StringBuilder builder, string key, string value)
    {
        builder.Append(key).Append('=').Append(value).Append('\n');
    }

    private static void AppendBlock(StringBuilder builder, string key, string value)
    {
        builder.Append(key).Append('=').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append(value).Append('\n');
    }
}
