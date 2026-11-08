using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using CR = Corkboard.Core.Langs.Common.Resources;

namespace Corkboard.Views;

/// <summary>用户在「已经在运行」询问窗里选了什么。</summary>
public enum DuplicateInstanceAction
{
    /// <summary>让已经在跑的那一份重启（它自己拉起新进程）。</summary>
    Restart,

    /// <summary>什么都不做，就当这次启动没发生过。</summary>
    Cancel
}

/// <summary>
///     「Corkboard 已经在运行」的询问窗（照 SecRandom-C 的 <c>DuplicateInstanceDialog</c> 搬的）。
///     <para>
///         只在「设置 → 基础设置 → 启动 → 再次打开时」选了「每次都问我」时才出现；其余档位是
///         直接打开对应窗口，不问。后启动的那一份现在只把一句
///         <see cref="Corkboard.Services.Desktop.SingleInstanceGate.DuplicateLaunchCommand" />
///         转给已经在跑的那一份、自己退出，所以这个窗由**第一份**自己弹。
///     </para>
///     <para>
///         只有「重启 / 取消」两颗按钮：原来那颗「打开已有窗口」在真机上经常叫不回窗口
///         （主窗口可能收着、置底着、或压根没建），用户明确要求去掉。想「再启动就直接开某个窗口」
///         去设置里那一项选，而不是在这里赌一次唤醒。
///     </para>
/// </summary>
public static class DuplicateInstanceDialog
{
    /// <summary>排障用的旁路日志（正式运行时为 null）。</summary>
    public static Action<string>? Trace { get; set; }

    public static async Task<DuplicateInstanceAction> ShowAsync(TopLevel parent)
    {
        var dialog = new FAContentDialog
        {
            Title = CR.MultiInstance_Title,
            Content = CR.MultiInstance_Message,
            PrimaryButtonText = CR.MultiInstance_Restart,
            CloseButtonText = CR.MultiInstance_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        };

        dialog.Opening += (_, _) => Trace?.Invoke("对话框 Opening");
        dialog.Opened += (_, _) => Trace?.Invoke("对话框 Opened");
        dialog.Closing += (_, e) => Trace?.Invoke($"对话框 Closing：{e.Result}");
        dialog.Closed += (_, e) => Trace?.Invoke($"对话框 Closed：{e.Result}");
        dialog.CloseButtonClick += (_, _) => Trace?.Invoke("对话框 取消钮被点");

        var result = await dialog.ShowAsync(parent);
        Trace?.Invoke($"对话框 ShowAsync 返回：{result}");

        return result == FAContentDialogResult.Primary
            ? DuplicateInstanceAction.Restart
            : DuplicateInstanceAction.Cancel;
    }
}
