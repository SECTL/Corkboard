using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Corkboard.Core.ControlCenter;

// 命名空间为什么叫 ControlCenter 而不是 Control：`Corkboard.Core.Control` 会成为
// `Corkboard.Core` 的命名空间成员，于是 `Corkboard.Core.Controls/**` 里所有裸写的 `Control`
// （Avalonia 的控件基类）都会被解析成这个命名空间，整个工程直接编不过（CS0118）。
// 目录仍按计划留在 `Corkboard.Core/Control/`。

/// <summary>
///     集控中心协议 v4 的常量与线上帧模型。
///     <para>
///         协议唯一事实源：<c>D:\code\SECTL-auth\docs\control-center-protocol-v4.md</c>；
///         帧形状照参考客户端 <c>D:\code\SECTL-auth\server\control-hub\clients\dotnet\SectlControlClient.cs</c>，
///         **不要自己发明字段名**——服务端只认那份契约，改一个下划线就是「服务端以为发了、设备永远不动」。
///     </para>
///     <para>
///         三封信封（v3 起没变）：<c>req</c> / <c>res</c> / <c>evt</c>。
///         上行限速 50 帧/秒、单帧上限 64 KiB；<c>evt</c> 只有服务端 → 客户端一个方向。
///     </para>
/// </summary>
public static class ControlV4Protocol
{
    /// <summary>本客户端实现的协议版本（握手里报 <c>hello.payload.protocol</c>）。</summary>
    public const int Version = 4;

    /// <summary>设备长连接路径（相对站点根，不带任何 query 参数）。</summary>
    public const string WebSocketPath = "/control/ws";

    /// <summary>双因子接入（唯一把结果装进 <c>data</c> 的客户端接口）。</summary>
    public const string EnrollPath = "/control/api/enroll";

    /// <summary>设备自助读结构（凭证放请求体，不放 URL）。</summary>
    public const string DeviceTreePath = "/control/api/device/tree";

    /// <summary>设备自助改自己所属的班（凭证放请求体）。</summary>
    public const string DeviceAssignPath = "/control/api/device/assign";

    /// <summary>设备自助退订：凭 device secret 删掉自己这一台设备记录（协议 §2.10.3）。</summary>
    public const string DeviceUnenrollPath = "/control/api/device/unenroll";

    /// <summary>上传作业板只读快照（凭证放请求体，快照形状同 <c>homework.read</c> 回执）。</summary>
    public const string DeviceHomeworkPath = "/control/api/device/homework-snapshots";

    /// <summary>拉取老师布置的作业（服务端契约要求凭证放 query，见 <c>homework-assignments</c>）。</summary>
    public const string DeviceHomeworkAssignmentsPath = "/control/api/device/homework-assignments";

    /// <summary>
    ///     一条布置的回执路径（<c>POST /control/api/device/homework-assignments/&lt;id&gt;/ack</c>）。
    /// </summary>
    public static string HomeworkAssignmentAckPath(string assignmentId) =>
        DeviceHomeworkAssignmentsPath + "/" + Uri.EscapeDataString(assignmentId) + "/ack";

    /// <summary>服务端默认的单条指令超时；握手回包 <c>limits.cmdTimeoutMs</c> 有值就以服务端为准。</summary>
    public const int DefaultCommandTimeoutMs = 5000;

    /// <summary>握手前的心跳缺省值；握过手就以服务端下发的 <c>heartbeat_seconds</c> 为准。</summary>
    public const int DefaultHeartbeatSeconds = 25;

    /// <summary>握手前的离线判定缺省值；握过手就以服务端下发的 <c>offline_after_seconds</c> 为准。</summary>
    public const int DefaultOfflineAfterSeconds = 75;

    /// <summary>单帧上限（字节）。超限上行方向服务端直接关连接。</summary>
    public const int MaxFrameBytes = 65536;

    /// <summary>状态能力：本机「允许远程控制」的开关状态。</summary>
    public const string CapabilityStatusRead = "status.read";

    /// <summary>状态能力：选班（占班 = 上课）。</summary>
    public const string CapabilityClassSelect = "class.select";

    /// <summary>状态能力：释放班（下课）。</summary>
    public const string CapabilityClassRelease = "class.release";

    /// <summary>
    ///     <b>永远不要声明</b>：广播是服务端 → 客户端方向的动作，Corkboard 没有广播面；
    ///     声明了只会让网页上多一个点了没反应的按钮。
    /// </summary>
    public const string CapabilityBroadcast = "broadcast";

    /// <summary>三个固定状态能力，不来自指令处理器。</summary>
    public static IReadOnlyList<string> StateCapabilities { get; } =
        [CapabilityStatusRead, CapabilityClassSelect, CapabilityClassRelease];

    /// <summary>线上 JSON 选项：字段名全部由 <see cref="JsonPropertyNameAttribute" /> 决定，空值不写。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>把一帧序列化成线上文本。</summary>
    public static string Serialize(ControlFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return JsonSerializer.Serialize(frame, JsonOptions);
    }

    /// <summary>解析一帧；坏 JSON 或 <c>type</c> 缺失时返回 <c>null</c>（调用方记日志后丢帧，不能因此断连）。</summary>
    public static ControlFrame? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var frame = JsonSerializer.Deserialize<ControlFrame>(json, JsonOptions);
            // 只认 req / res / evt：未知 type 要当坏帧丢掉，不能放行给上层猜。
            return frame?.Type is ControlFrameTypes.Request or ControlFrameTypes.Response or ControlFrameTypes.Event
                ? frame
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>信封 <c>type</c> 的三个合法取值。</summary>
public static class ControlFrameTypes
{
    /// <summary>客户端 → 服务端的请求（<c>hello</c> / <c>cmd.ack</c> / <c>cmd.result</c> / …）。</summary>
    public const string Request = "req";

    /// <summary>服务端 → 客户端的回包，<c>id</c> 回填请求的 <c>id</c>。</summary>
    public const string Response = "res";

    /// <summary>服务端 → 客户端的推送（<c>cmd</c> / <c>desired_state</c> / <c>evicted</c>）。</summary>
    public const string Event = "evt";
}

/// <summary>协议里的 <c>topic</c> 字面量。</summary>
public static class ControlTopics
{
    /// <summary>首帧握手。</summary>
    public const string Hello = "hello";

    /// <summary>下行指令推送（<c>evt</c>）。</summary>
    public const string Command = "cmd";

    /// <summary>指令回执第一帧：只答「接不接」。</summary>
    public const string CommandAck = "cmd.ack";

    /// <summary>指令回执第二帧：只答「成没成」。</summary>
    public const string CommandResult = "cmd.result";

    /// <summary>期望状态（班级占用 / 抽取锁）。</summary>
    public const string DesiredState = "desired_state";

    /// <summary>被顶掉 / 被移除。</summary>
    public const string Evicted = "evicted";

    /// <summary>探活（服务端回 <c>{server_time}</c>）。</summary>
    public const string Ping = "ping";

    /// <summary>拉结构树。</summary>
    public const string TreeGet = "tree.get";

    /// <summary>占班。</summary>
    public const string ClassSelect = "class.select";

    /// <summary>释放班。</summary>
    public const string ClassRelease = "class.release";
}

/// <summary>协议错误码字面量（客户端要用到的几个）。</summary>
public static class ControlErrorCodes
{
    /// <summary>凭证无效（网页上可能已经把这台设备移除了）。**不重试**。</summary>
    public const string BadSecret = "bad_secret";

    /// <summary>设备已被移除。**不重试**。</summary>
    public const string DeviceRevoked = "device_revoked";

    /// <summary>没带凭证 / 凭证过期。**不重试**。</summary>
    public const string NotAuthed = "not_authed";

    /// <summary>协议版本太旧。**不重试**。</summary>
    public const string ProtocolTooOld = "protocol_too_old";

    /// <summary>指令已过期（收到时已过 <c>expires_at</c>）。</summary>
    public const string Expired = "expired";

    /// <summary>指令超时（客户端在 deadline − 200ms 代回）。</summary>
    public const string Timeout = "timeout";

    /// <summary>本机不允许远程控制。</summary>
    public const string LocalRemoteDisabled = "local_remote_disabled";

    /// <summary>客户端没声明该能力 / 没注册处理器。</summary>
    public const string CapabilityUnsupported = "capability_unsupported";

    /// <summary>执行失败（客户端自己兜的码）。</summary>
    public const string Failed = "failed";

    /// <summary>占班被 3 秒防抖挡住（服务端码，按文档不自动重试）。</summary>
    public const string Busy = "busy";
}

/// <summary>
///     一帧的通用信封。请求与回包共用：<c>type</c> 区分方向，
///     <c>id</c> 做请求/回包配对（<c>cmd.ack</c> / <c>cmd.result</c> 的 <c>id</c> 必须回填 <c>command_id</c>）。
/// </summary>
public sealed class ControlFrame
{
    /// <summary><c>req</c> / <c>res</c> / <c>evt</c>。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>请求与回包的配对键；<c>evt</c> 没有 <c>id</c>。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>请求 / 推送的主题（见 <see cref="ControlTopics" />）。</summary>
    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    /// <summary>回包是否成功；请求与推送不带。</summary>
    [JsonPropertyName("ok")]
    public bool? Ok { get; set; }

    /// <summary>失败码（回包）。</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>失败的人话（回包，可直接进日志）。</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>载荷；协议里所有业务字段都在这里。</summary>
    [JsonPropertyName("payload")]
    public JsonObject? Payload { get; set; }

    /// <summary>构造一帧上行请求。</summary>
    public static ControlFrame Request(string id, string topic, JsonObject? payload = null) => new()
    {
        Type = ControlFrameTypes.Request,
        Id = id,
        Topic = topic,
        Payload = payload
    };

    /// <summary>构造一帧回包（客户端回复服务端发的 JSON <c>req</c>）。</summary>
    public static ControlFrame Response(string? id, bool ok, string? code = null, JsonObject? payload = null) => new()
    {
        Type = ControlFrameTypes.Response,
        Id = id,
        Ok = ok,
        Code = ok ? null : code,
        Payload = payload
    };
}

/// <summary>握手 <c>hello</c> 的上行载荷（<c>hello.payload</c>）。</summary>
public sealed class ControlHelloPayload
{
    /// <summary>协议版本，固定 <see cref="ControlV4Protocol.Version" />。</summary>
    [JsonPropertyName("protocol")]
    public int Protocol { get; set; } = ControlV4Protocol.Version;

    /// <summary>平台标识（Corkboard 在服务端登记为 <c>corkboard</c>）。</summary>
    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    /// <summary>客户端版本（只用于网页上展示与排障）。</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>设备名（网页上显示的「讲台机」之类）。</summary>
    [JsonPropertyName("device_name")]
    public string? DeviceName { get; set; }

    /// <summary>服务端签发的设备 id（不是本机 UUID）。</summary>
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>服务端签发的一次性 secret（只存加密文件里）。</summary>
    [JsonPropertyName("secret")]
    public string Secret { get; set; } = string.Empty;

    /// <summary>本机声明能处理的指令 / 状态能力；服务端不认识的项会被静默过滤。</summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string> Capabilities { get; set; } = [];

    /// <summary>已落盘的期望状态 revision，服务端据此决定要不要补投。</summary>
    [JsonPropertyName("applied_state_revision")]
    public long AppliedStateRevision { get; set; }
}

/// <summary>握手回包里的设备信息。</summary>
public sealed class ControlDeviceInfo
{
    /// <summary>设备 id。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>设备名。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>平台。</summary>
    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    /// <summary>所属地点 id。</summary>
    [JsonPropertyName("site_id")]
    public string? SiteId { get; set; }
}

/// <summary>握手回包里的服务端限额。</summary>
public sealed class ControlLimits
{
    /// <summary>单条指令超时（毫秒）。</summary>
    [JsonPropertyName("cmdTimeoutMs")]
    public int CommandTimeoutMs { get; set; }
}

/// <summary>握手回包载荷（<c>res.payload</c>）。</summary>
public sealed class ControlHelloReply
{
    /// <summary>协商后的协议版本（高于服务端时以服务端为准）。</summary>
    [JsonPropertyName("protocol")]
    public int Protocol { get; set; }

    /// <summary>设备信息。</summary>
    [JsonPropertyName("device")]
    public ControlDeviceInfo? Device { get; set; }

    /// <summary>服务端授予的能力（客户端声明的子集）。</summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string>? Capabilities { get; set; }

    /// <summary>结构树版本。</summary>
    [JsonPropertyName("tree_version")]
    public long TreeVersion { get; set; }

    /// <summary>限额。</summary>
    [JsonPropertyName("limits")]
    public ControlLimits? Limits { get; set; }

    /// <summary>服务端下发的心跳间隔（秒）。</summary>
    [JsonPropertyName("heartbeat_seconds")]
    public int HeartbeatSeconds { get; set; }

    /// <summary>服务端下发的离线判定阈值（秒）。</summary>
    [JsonPropertyName("offline_after_seconds")]
    public int OfflineAfterSeconds { get; set; }

    /// <summary>服务端当前的期望状态 revision。</summary>
    [JsonPropertyName("desired_state_revision")]
    public long DesiredStateRevision { get; set; }

    /// <summary>服务端时间戳（毫秒）。</summary>
    [JsonPropertyName("server_time")]
    public long ServerTime { get; set; }
}

/// <summary>
///     一条下行指令的调用描述（<c>evt cmd.payload</c>）。
///     <para>
///         <c>CommandId</c> 是幂等键：同一个 <c>command_id</c> 重投时**不能**重复执行，
///         必须把上次的 <see cref="ControlCommandOutcome" /> 再回一遍。
///     </para>
/// </summary>
public sealed class ControlCommandInvocation
{
    /// <summary>幂等键，形如 <c>cmd_3a7f9c2e1b8d4056</c>。</summary>
    [JsonPropertyName("command_id")]
    public string CommandId { get; set; } = string.Empty;

    /// <summary>指令名（Corkboard 只认注册过的那几个）。</summary>
    [JsonPropertyName("cmd")]
    public string Command { get; set; } = string.Empty;

    /// <summary>指令参数；缺省是空对象，不是 <c>null</c>。</summary>
    [JsonPropertyName("args")]
    public JsonObject Args { get; set; } = new();

    /// <summary>服务端期望的回执时限（毫秒）；≤0 时用握手下发的默认值。</summary>
    [JsonPropertyName("deadline_ms")]
    public int DeadlineMs { get; set; }

    /// <summary>过期时间（毫秒时间戳）；0 表示不带。</summary>
    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }

    /// <summary>发起方（<c>web</c> 之类的标签，只用于日志）。</summary>
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    /// <summary>从 <c>evt cmd.payload</c> 解析；<c>command_id</c> 缺失时退回 <c>id</c>（老服务端的写法）。</summary>
    public static ControlCommandInvocation FromPayload(JsonObject? payload)
    {
        payload ??= new JsonObject();
        var commandId = ReadString(payload, "command_id");
        if (string.IsNullOrEmpty(commandId))
            commandId = ReadString(payload, "id");

        return new ControlCommandInvocation
        {
            CommandId = commandId ?? string.Empty,
            Command = ReadString(payload, "cmd") ?? string.Empty,
            Args = payload["args"] is JsonObject args ? (JsonObject)args.DeepClone() : new JsonObject(),
            DeadlineMs = ReadInt(payload, "deadline_ms"),
            ExpiresAt = ReadLong(payload, "expires_at"),
            Issuer = ReadString(payload, "issuer")
        };
    }

    private static string? ReadString(JsonObject payload, string name) => ControlJson.ReadString(payload, name);

    private static int ReadInt(JsonObject payload, string name) => ControlJson.ReadInt32(payload, name);

    private static long ReadLong(JsonObject payload, string name) => ControlJson.ReadInt64(payload, name);
}

/// <summary>指令回执第一帧（<c>cmd.ack</c>）的载荷。</summary>
public sealed class ControlCommandAckPayload
{
    /// <summary>接不接这条指令。</summary>
    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; }

    /// <summary>不接受的原因码（<c>expired</c> / <c>capability_unsupported</c> / …）。</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }
}

/// <summary>指令回执第二帧（<c>cmd.result</c>）的载荷。</summary>
public sealed class ControlCommandResultPayload
{
    /// <summary>成没成。</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    /// <summary>业务数据（客户端处理器返回的对象）。</summary>
    [JsonPropertyName("data")]
    public JsonObject? Data { get; set; }

    /// <summary>失败码，默认 <c>failed</c>。</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>失败的人话（只进服务端流水，不展示给老师）。</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>期望状态（<c>evt desired_state.payload</c>）。</summary>
public sealed class ControlDesiredStatePayload
{
    /// <summary>单调整数；≤ 已落盘的 revision 一律丢弃。</summary>
    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    /// <summary>班级 id；<c>null</c> 表示已释放。</summary>
    [JsonPropertyName("class_id")]
    public string? ClassId { get; set; }

    /// <summary>平台。</summary>
    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    /// <summary>本班是否禁止抽取；缺失按 <c>true</c>（fail-closed）。</summary>
    [JsonPropertyName("draw_locked")]
    public bool? DrawLocked { get; set; }

    /// <summary>从载荷解析。</summary>
    public static ControlDesiredStatePayload FromPayload(JsonObject? payload)
    {
        payload ??= new JsonObject();
        var revision = ControlJson.ReadInt64(payload, "revision");
        var classId = ControlJson.ReadString(payload, "class_id") ?? ControlJson.ReadString(payload, "classId");
        var platform = ControlJson.ReadString(payload, "platform");
        var drawLocked = ControlJson.ReadBool(payload, "draw_locked");

        return new ControlDesiredStatePayload
        {
            Revision = revision,
            ClassId = classId,
            Platform = platform,
            DrawLocked = drawLocked
        };
    }
}

/// <summary>踢出（<c>evt evicted.payload</c>）。</summary>
public sealed class ControlEvictedPayload
{
    /// <summary>服务端在 v4 文档里用 <c>classId</c>，参考实现里见过 <c>class_id</c>，两种都认。</summary>
    [JsonPropertyName("classId")]
    public string? ClassId { get; set; }

    /// <summary>蛇形写法（兼容）。</summary>
    [JsonPropertyName("class_id")]
    public string? ClassIdSnake { get; set; }

    /// <summary>平台。</summary>
    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    /// <summary>顶掉本机的设备（<c>by</c>，可能是设备 id 或对象）。</summary>
    [JsonPropertyName("by")]
    public JsonNode? By { get; set; }

    /// <summary>原因：<c>superseded</c> / <c>takeover</c> / <c>revoked</c> / <c>replaced</c>。</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>真正要释放的班级 id（两种写法合一）。</summary>
    public string? ResolveClassId() => ClassId ?? ClassIdSnake;
}

/// <summary>选班 / 释放班的请求载荷。</summary>
public sealed class ControlClassSelectPayload
{
    /// <summary>班级 id；释放时可以省略（服务端按设备当前占用反查）。</summary>
    [JsonPropertyName("class_id")]
    public string? ClassId { get; set; }

    /// <summary>平台。</summary>
    [JsonPropertyName("platform")]
    public string? Platform { get; set; }
}

/// <summary>结构树（<c>tree.get</c> 回包 / 设备自助接口的载荷）。</summary>
public sealed class ControlTreePayload
{
    /// <summary>地点。</summary>
    [JsonPropertyName("site")]
    public ControlTreeSite? Site { get; set; }

    /// <summary>年级 → 班级。</summary>
    [JsonPropertyName("grades")]
    public IReadOnlyList<ControlTreeGrade>? Grades { get; set; }

    /// <summary>本设备信息。</summary>
    [JsonPropertyName("device")]
    public ControlTreeDevice? Device { get; set; }

    /// <summary>把协议里的树转成 Core 的树模型。</summary>
    public IReadOnlyList<ControlGradeNode> ToGradeNodes()
    {
        if (Grades is null || Grades.Count == 0)
            return [];

        var result = new List<ControlGradeNode>(Grades.Count);
        foreach (var grade in Grades)
        {
            var classes = new List<ControlClassNode>(grade.Classes?.Count ?? 0);
            if (grade.Classes is not null)
            {
                foreach (var cls in grade.Classes)
                    classes.Add(new ControlClassNode(cls.Id ?? string.Empty, cls.Name ?? string.Empty));
            }

            result.Add(new ControlGradeNode(grade.Id ?? string.Empty, grade.Name ?? string.Empty, classes));
        }

        return result;
    }
}

/// <summary>结构树里的地点。</summary>
public sealed class ControlTreeSite
{
    /// <summary>地点 id（<c>SC-XXXXXX</c>）。</summary>
    [JsonPropertyName("publicId")]
    public string? PublicId { get; set; }

    /// <summary>地点名。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>结构树里的年级。</summary>
public sealed class ControlTreeGrade
{
    /// <summary>年级 id。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>年级名。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>年级下的班级。</summary>
    [JsonPropertyName("classes")]
    public IReadOnlyList<ControlTreeClass>? Classes { get; set; }
}

/// <summary>结构树里的班级。</summary>
public sealed class ControlTreeClass
{
    /// <summary>班级 id。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>班级名。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>结构树里的本设备信息。</summary>
public sealed class ControlTreeDevice
{
    /// <summary>设备 id。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>设备名。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>设备当前所属班级（没分配时为 <c>null</c>）。</summary>
    [JsonPropertyName("classId")]
    public string? ClassId { get; set; }
}
