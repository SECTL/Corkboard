using Corkboard.Core.Models.SubConfigs.General;
using Corkboard.Core.Services.Config;

namespace Corkboard.Core.Services.FirstRun;

/// <summary>
///     首次运行引导（OOBE）的判定与收尾。这里只有「要不要引导」与「记下走完了」两件事，
///     界面在 <c>Corkboard/Views/FirstRunOobeWindow</c>，两者靠 <see cref="Requirement" /> 串起来。
/// </summary>
/// <remarks>
///     <para>
///         分两种，正好对应两次不同的问法：
///         <list type="bullet">
///             <item><see cref="OobeRequirement.FullSetup" />：整段引导（欢迎 / 协议 / 功能 / 外观 /
///             启动 / 完成）。只在 <c>GuideCompleted</c> 还是假、也就是这份配置从没走过引导时出现。</item>
///             <item><see cref="OobeRequirement.LegalOnly" />：只补「协议」那一步。引导走完之后
///             协议改版了，不该让老用户重看一遍引导——只要确认新版本就行。</item>
///         </list>
///     </para>
///     <para>
///         <b>协议版本号只增不改</b>：文案改了要不要重新确认由这里说了算，写在这里而不是 scatter
///         到界面里，否则以后改文案时很容易忘了升版本。
///     </para>
/// </remarks>
public sealed class FirstRunOobeService(MainConfigHandler configHandler)
{
    /// <summary>当前用户协议版本。协议正文有实质变化时 +1，老用户会被要求重新确认一次。</summary>
    public const int CurrentEulaVersion = 1;

    /// <summary>当前隐私政策版本。与 <see cref="CurrentEulaVersion" /> 同理。</summary>
    public const int CurrentPrivacyPolicyVersion = 1;

    /// <summary>
    ///     这次启动要不要引导、引导哪一段。判断只看配置里的两个版本号与
    ///     <see cref="BasicSettingsConfig.GuideCompleted" />，不读磁盘上的其它状态。
    /// </summary>
    public OobeRequirement Requirement
    {
        get
        {
            var basic = configHandler.Data.Basic;

            if (!basic.GuideCompleted)
                return OobeRequirement.FullSetup;

            return basic.AcceptedEulaVersion < CurrentEulaVersion
                   || basic.AcceptedPrivacyPolicyVersion < CurrentPrivacyPolicyVersion
                ? OobeRequirement.LegalOnly
                : OobeRequirement.None;
        }
    }

    /// <summary>这次启动是不是要铺引导窗口。</summary>
    public bool IsRequired => Requirement != OobeRequirement.None;

    /// <summary>
    ///     收尾：把两个协议版本号与「引导已走完」落盘。
    ///     <para>
    ///         <c>ConfigHandlerBase</c> 是「写一个属性落一次盘」（没有批量写），所以这里天然是三次写。
    ///         因此**顺序有讲究**：<see cref="BasicSettingsConfig.GuideCompleted" /> 放在最后，
    ///         中途崩掉时盘上仍是「引导没走完」，下次启动重新引导一遍；反过来先写它，就会出现
    ///         「引导算走完、协议却没确认」的中间态，下次启动莫名其妙只弹协议页。
    ///     </para>
    ///     <para>
    ///         末尾那次显式 <see cref="MainConfigHandler.Save" /> 是兜底（三次自动落盘之后再确认一次
    ///         盘上是最新内容），不是唯一的那次写。
    ///     </para>
    /// </summary>
    public void Complete()
    {
        var basic = configHandler.Data.Basic;
        // 顺序：先两个协议版本，最后才置「引导已走完」——中途断掉时盘上仍是「没走完」。
        basic.AcceptedEulaVersion = CurrentEulaVersion;
        basic.AcceptedPrivacyPolicyVersion = CurrentPrivacyPolicyVersion;
        basic.GuideCompleted = true;
        configHandler.Save();
    }
}

/// <summary>这次启动要走哪一段引导。</summary>
public enum OobeRequirement
{
    /// <summary>不用引导，直接进主界面。</summary>
    None = 0,

    /// <summary>只补「协议」这一步（引导走过、协议改版了）。</summary>
    LegalOnly = 1,

    /// <summary>整段引导（这份配置从没走过）。</summary>
    FullSetup = 2
}
