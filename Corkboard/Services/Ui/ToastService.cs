using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Corkboard.Views.Controls;

namespace Corkboard.Services.Ui;

/// <summary>右下角通知的语气。</summary>
public enum ToastSeverity
{
    /// <summary>操作成功（默认）。</summary>
    Success = 0,

    /// <summary>中性提示。</summary>
    Info = 1,

    /// <summary>失败或需要用户注意。</summary>
    Error = 2
}

/// <summary>
///     右下角的轻提示：只用于「已保存 / 已添加 / 已删除」这类一闪而过的反馈，
///     需要用户读或做决定的内容（校验失败、导入错误）仍然走页面里的文字或对话框。
/// </summary>
public interface IToastService
{
    /// <summary>弹一条提示；没有挂上宿主的窗口里调用会被安全忽略。</summary>
    void Show(string message, ToastSeverity severity = ToastSeverity.Success);
}

/// <summary>
///     通知的宿主注册表：设置壳在 <c>Loaded</c> 里 <see cref="Attach" />、
///     <c>Unloaded</c> 里 <see cref="Detach" />，提示交给当前活着的 <see cref="ToastHost" /> 去画。
/// </summary>
public sealed class ToastService : IToastService
{
    private readonly List<WeakReference<ToastHost>> _hosts = [];

    /// <summary>挂上一个 Toast 宿主（同一窗口重复挂会去重）。</summary>
    public void Attach(ToastHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        lock (_hosts)
        {
            Prune();
            foreach (var reference in _hosts)
            {
                if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, host))
                    return;
            }

            _hosts.Add(new WeakReference<ToastHost>(host));
        }
    }

    /// <summary>窗口离开可视树时摘掉宿主。</summary>
    public void Detach(ToastHost host)
    {
        lock (_hosts)
        {
            for (var index = _hosts.Count - 1; index >= 0; index--)
            {
                if (!_hosts[index].TryGetTarget(out var existing) || ReferenceEquals(existing, host))
                    _hosts.RemoveAt(index);
            }
        }
    }

    public void Show(string message, ToastSeverity severity = ToastSeverity.Success)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var host = FindHost();
        if (host is null)
            return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            host.Show(message, severity);
            return;
        }

        Dispatcher.UIThread.Post(() => host.Show(message, severity));
    }

    /// <summary>取最后一个还挂在可视树上的宿主（多窗口时发给最近登记的那扇）。</summary>
    private ToastHost? FindHost()
    {
        lock (_hosts)
        {
            Prune();
            for (var index = _hosts.Count - 1; index >= 0; index--)
            {
                if (_hosts[index].TryGetTarget(out var host))
                    return host;
            }

            return null;
        }
    }

    private void Prune() => _hosts.RemoveAll(reference => !reference.TryGetTarget(out _));
}