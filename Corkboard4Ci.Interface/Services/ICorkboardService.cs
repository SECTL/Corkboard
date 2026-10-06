using System;
using Corkboard4Ci.Interface.Models;
using dotnetCampus.Ipc.CompilerServices.Attributes;

namespace Corkboard4Ci.Interface.Services;

/// <summary>
/// Corkboard 投递通知到 ClassIsland 侧插件（Corkboard4Ci）的 IPC 契约。
/// 与抽取/名单/课程表业务无关：只承载「一条通知」这个最小语义。
/// </summary>
[IpcPublic(IgnoresIpcException = true)]
public interface ICorkboardService
{
    /// <summary>
    /// 投递一条通知。
    /// </summary>
    /// <remarks>
    /// 投递失败必须能传到调用方（否则调用方配置的内置回退通知不会触发），
    /// 所以这里 <see cref="IpcMethodAttribute.IgnoresIpcException"/> 为 <c>false</c>、
    /// <see cref="IpcMethodAttribute.WaitsVoid"/> 为 <c>true</c>：同步等待远端真正处理完。
    /// </remarks>
    [IpcMethod(WaitsVoid = true, IgnoresIpcException = false)]
    void ShowNotification(NotificationData data);

    /// <summary>插件自报版本（含修订号）。</summary>
    Version GetPluginVersion();

    /// <summary>插件自报存活；返回 <c>Yes</c> 表示可用。</summary>
    string IsAlive();
}
