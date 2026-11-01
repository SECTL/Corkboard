using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Corkboard.Core.Attributes;

namespace Corkboard.Core.Services.Cloud;

/// <summary>
///     反射遍历配置模型的属性树，收集所有被 <see cref="LocalOnlyAttribute" /> 标记的 JSON 路径。
///     <para>
///         属性名按 <see cref="JsonNamingPolicy.SnakeCaseLower" /> 转键名，与 <c>settings.json</c> 的写法一致，
///         所以这里的输出可以直接当 <see cref="CloudSyncExclusions" /> 的排除路径用。
///     </para>
///     <para>
///         只做纯计算，不碰实例、不读盘——配置对象的属性由源生成器产出，
///         <c>[ObservableProperty]</c> 生成的公开属性同样带特性（源文件写 <c>[property: LocalOnly]</c>），
///         所以反射类型就够了。
///     </para>
/// </summary>
public static class LocalOnlyPaths
{
    /// <summary>收集 <paramref name="rootType" /> 下所有本机专属项的 JSON 路径（已排序）。</summary>
    public static IReadOnlyList<string> Build(Type rootType)
    {
        ArgumentNullException.ThrowIfNull(rootType);

        var result = new List<string>();
        Walk(rootType, string.Empty, result, new HashSet<Type>());
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static void Walk(Type type, string prefix, List<string> result, HashSet<Type> visiting)
    {
        // 配置类理论上不会自引用，但反射遍历不能被一份写坏的类型拖进死循环。
        if (!visiting.Add(type))
            return;

        try
        {
            // 贴在类上：整段都不参与同步，不必再往里走。
            if (type.IsDefined(typeof(LocalOnlyAttribute), inherit: false))
            {
                if (prefix.Length > 0)
                    result.Add(prefix);
                return;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                // [JsonIgnore] 的转发属性不落盘，也就没有 JSON 路径可言
                // （例：MainConfigModel.Basic 只是指到 general.basic，走进去会把同一批路径再收一遍）。
                if (property.IsDefined(typeof(JsonIgnoreAttribute), inherit: false))
                    continue;

                var path = Combine(prefix, ResolveKey(property));
                if (property.IsDefined(typeof(LocalOnlyAttribute), inherit: false))
                {
                    result.Add(path);
                    continue;
                }

                var propertyType = Unwrap(property.PropertyType);
                if (ShouldDescend(propertyType))
                    Walk(propertyType, path, result, visiting);
            }
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    private static Type Unwrap(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        return underlying ?? type;
    }

    /// <summary>
    ///     这个类型还该进去看吗：只有「自己带好几个配置项」的类才值得往下走，
    ///     标量与集合都是叶子（集合里的记录不是配置项，不参与同步）。
    /// </summary>
    private static bool ShouldDescend(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type.IsPointer || type.IsArray)
            return false;
        if (type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime)
            || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid)
            || type == typeof(Uri))
            return false;
        if (typeof(IEnumerable).IsAssignableFrom(type))
            return false;
        if (type.IsAbstract || type.IsInterface || type.IsSealed && type == typeof(object))
            return false;

        return type.IsClass || type.IsValueType;
    }

    private static string Combine(string prefix, string key) =>
        prefix.Length == 0 ? key : $"{prefix}.{key}";

    /// <summary>
    ///     属性落到 JSON 上的键名：显式 <see cref="JsonPropertyNameAttribute" /> 优先，
    ///     否则按 <c>settings.json</c> 的 snake_case 规则转。
    /// </summary>
    private static string ResolveKey(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);
}
