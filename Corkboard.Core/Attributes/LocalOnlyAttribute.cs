namespace Corkboard.Core.Attributes;

/// <summary>
///     标记「只对当前这台机器有意义」的配置：云同步**永不上传**这些项，从云端恢复时也**永不覆盖**本机。
///     <para>
///         典型成员是窗口尺寸与位置（屏幕不一样）、开机自启（每台机器自己决定）、
///         自动保存目录（本机绝对路径）、云同步自己的开关（一个账号下多台设备各自决定）。
///         这些项一旦跟着云同步跑，换台机器同步一下就有人「窗口摆到屏幕外」或者「开机自启被替对端决定」。
///     </para>
///     <para>
///         <b>贴在属性上</b>：只排除这一项（JSON 路径形如 <c>general.basic.autostart</c>）。<br />
///         <b>贴在类上</b>：整段都不参与同步（JSON 路径形如 <c>cloud</c>，含它下面所有子项）。
///     </para>
///     <para>
///         排除清单由 <c>CloudSyncExclusions</c> 现算（反射遍历 <c>MainConfigModel</c> 的属性树，
///         属性名按 <c>JsonNamingPolicy.SnakeCaseLower</c> 转成 JSON 键）。**别再手写路径字符串**——
///         手写清单的注释写着「用前缀是为了以后新增字段不漏」，那正是这个特性要替掉的东西。
///         新增本机专属项时只要贴特性，别处不用改。
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class LocalOnlyAttribute : Attribute;
