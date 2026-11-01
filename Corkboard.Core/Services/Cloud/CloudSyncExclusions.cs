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
    ///     不同步的设置路径。命中规则：完全相等，或者它后面还接着子项（<c>.</c> 分隔）。
    ///     <para>
    ///         清单不是手写的：由 <see cref="LocalOnlyPaths" /> 反射遍历 <see cref="Models.MainConfigModel" />，
    ///         收出所有贴了 <see cref="Attributes.LocalOnlyAttribute" /> 的属性/整段。
    ///         新增本机专属项时只要在配置类上贴那个特性，这里不用改，也不会漏。
    ///     </para>
    /// </summary>
    public static readonly IReadOnlyList<string> Prefixes =
        LocalOnlyPaths.Build(typeof(Models.MainConfigModel));

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
