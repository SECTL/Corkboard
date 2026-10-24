using System.Text.Json;
using System.Text.Json.Nodes;

namespace Corkboard.Core.Services.Cloud;

/// <summary>
///     云同步时对 <c>settings.json</c> 做的裁剪与合并。
///     <para>
///         有些设置只对**当前这台机器**有意义：窗口尺寸与位置（屏幕不一样、分辨率不一样）、
///         开机自启（每台机器自己决定）、云同步自己的开关（一个账号下的多台设备要能各自开关）。
///         这些项**永不上传**，从云端恢复时也**永不覆盖本机**，否则换台设备同步一下就会把对端的
///         窗口摆歪、或者替对端决定要不要开机自启。
///     </para>
///     <para>路径是 JSON 点号路径，键名与 <c>settings.json</c> 一致（snake_case）。</para>
/// </summary>
public static class CloudSyncExclusions
{
    /// <summary>
    ///     不同步的设置路径前缀。命中规则：完全相等，或者前缀后面紧跟 <c>.</c> / <c>_</c>。
    ///     用前缀而不是逐个字段，是为了以后新增 <c>main_window_*</c> / <c>settings_window_*</c>
    ///     字段时不会漏掉（漏掉的后果是把对端的窗口尺寸带过来）。
    /// </summary>
    public static readonly IReadOnlyList<string> Prefixes =
    [
        // 开机自启：每台机器自己决定
        "general.basic.autostart",
        // 主窗口尺寸、位置、不透明度、点击穿透：都是本机窗口状态
        "general.basic.main_window",
        // 设置窗口尺寸与位置
        "general.basic.settings_window",
        // 自动保存的目录：本机的绝对路径，搬到对端多半是个不存在的盘符，
        // 结果是「开了自动保存却什么都没存」而且失败只记日志。开关与时机照旧同步。
        "board_auto_save.folder",
        // 云同步开关与上次同步时间：每个账号、每台设备各自决定
        "cloud"
    ];

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>这个路径（含它下面的子项）是否不参与同步。</summary>
    public static bool IsExcluded(string path)
    {
        foreach (var prefix in Prefixes)
        {
            if (path.Length < prefix.Length)
                continue;
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (path.Length == prefix.Length)
                return true;
            var next = path[prefix.Length];
            if (next is '.' or '_')
                return true;
        }

        return false;
    }

    /// <summary>把本机设置裁成可以上传的样子：去掉所有本机专属项。</summary>
    public static string StripForUpload(string settingsJson)
    {
        var root = Parse(settingsJson);
        RemoveExcluded(root, string.Empty);
        return root.ToJsonString(WriteOptions);
    }

    /// <summary>
    ///     把云端设置合进本机设置：远端有的项按远端覆盖，**本机专属项保持本机原值**，
    ///     本机独有的项与远端也不认识的项都原样保留。
    /// </summary>
    public static string MergeRemoteOntoLocal(string localJson, string remoteJson)
    {
        var local = Parse(localJson);
        var remote = Parse(remoteJson);
        MergeInto(local, remote, string.Empty);
        return local.ToJsonString(WriteOptions);
    }

    private static JsonObject Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static void RemoveExcluded(JsonObject node, string prefix)
    {
        foreach (var key in node.Select(pair => pair.Key).ToList())
        {
            var path = Combine(prefix, key);
            if (IsExcluded(path))
            {
                node.Remove(key);
                continue;
            }

            if (node[key] is JsonObject child)
                RemoveExcluded(child, path);
        }
    }

    private static void MergeInto(JsonObject local, JsonObject remote, string prefix)
    {
        foreach (var (key, value) in remote)
        {
            var path = Combine(prefix, key);
            if (IsExcluded(path) || value is null)
                continue;

            if (value is JsonObject remoteChild && local[key] is JsonObject localChild)
            {
                MergeInto(localChild, remoteChild, path);
                continue;
            }

            local[key] = value.DeepClone();
        }
    }

    private static string Combine(string prefix, string key) =>
        prefix.Length == 0 ? key : $"{prefix}.{key}";
}
