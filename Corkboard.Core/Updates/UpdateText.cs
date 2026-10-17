using System.Globalization;
using Corkboard.Core.Langs.Common;

namespace Corkboard.Core.Updates;

/// <summary>
///     更新服务的文案入口。资源键统一挂在 Core 的公共资源上，键名是 <c>Update_</c> + 调用方给的短键，
///     例如 <c>Get("M_StatusChecking")</c> 实际取 <c>Update_M_StatusChecking</c>。
///     <para>
///         资源缺失时回退到短键本身（与上游一致）：更新流程不能因为一条文案缺失就抛异常。
///     </para>
/// </summary>
public static class UpdateText
{
    private const string KeyPrefix = "Update_";

    public static string Get([System.ComponentModel.Localizable(false)] string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Resources.ResourceManager.GetString(KeyPrefix + key, CultureInfo.CurrentUICulture) ?? key;
    }
}
