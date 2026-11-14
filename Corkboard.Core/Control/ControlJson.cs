using System.Text.Json;
using System.Text.Json.Nodes;

namespace Corkboard.Core.ControlCenter;

/// <summary>
///     <see cref="JsonObject" /> 取值工具。
///     <para>
///         <b>为什么不能裸用 <c>TryGetValue&lt;T&gt;</c>：</b><see cref="JsonValue" /> 只在「内部装箱值恰好就是
///         <c>T</c>」或「值来自解析过的 JSON 文本（内部是 <see cref="JsonElement" />）」时才转换成功。
///         本机拼出来的 <c>new JsonObject { ["revision"] = 100 }</c> 内部装的是 <see cref="int" />，
///         问它要 <see cref="long" /> 会**静默返回 <c>false</c>**——revision 直接变 0，
///         「新 revision ≤ 已落盘 revision」的判定永远不过。
///     </para>
///     <para>
///         这里先试精确类型，不对就经一次 JSON 文本往返换成目标类型，两种来源（CLR 装箱值 / 解析出的节点）都认。
///     </para>
/// </summary>
internal static class ControlJson
{
    /// <summary>读字符串；缺失或转换失败返回 <c>null</c>。</summary>
    public static string? ReadString(JsonObject? payload, string name)
    {
        if (payload?[name] is not JsonValue value)
            return null;
        return value.TryGetValue<string>(out var text) ? text : Convert<string>(value);
    }

    /// <summary>读布尔；缺失或转换失败返回 <c>null</c>（调用方据此区分「没带」与「带了 false」）。</summary>
    public static bool? ReadBool(JsonObject? payload, string name)
    {
        if (payload?[name] is not JsonValue value)
            return null;
        return value.TryGetValue<bool>(out var flag) ? flag : Convert<bool?>(value);
    }

    /// <summary>读 32 位整数；缺失、不是数字或越界返回 0。</summary>
    public static int ReadInt32(JsonObject? payload, string name)
    {
        var number = ReadInt64(payload, name);
        return number is > int.MaxValue or < int.MinValue ? 0 : (int)number;
    }

    /// <summary>读 64 位整数；缺失或不是数字返回 0。</summary>
    public static long ReadInt64(JsonObject? payload, string name)
    {
        if (payload?[name] is not JsonValue value)
            return 0;
        if (value.TryGetValue<long>(out var number))
            return number;
        return Convert<long?>(value) ?? 0;
    }

    private static T? Convert<T>(JsonValue value)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(value.ToJsonString(), ControlV4Protocol.JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}